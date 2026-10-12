using Merp.Application.Abstractions.Approvals;
using System.Text.Json;
using Merp.Application.Abstractions.Messaging;
using Merp.Modules.Platform.Application.Access;
using Merp.Modules.Platform.Application.Calendars;
using Merp.Modules.Platform.Application.Notifications;
using Merp.Modules.Platform.Contracts.Approvals;
using Merp.Modules.Platform.Contracts.Notifications;
using Merp.Modules.Platform.Domain.Approvals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Merp.Modules.Platform.Application.Approvals;

/// <summary>
/// Runs approval flows (docs/domain/yetki-ve-onay.md §2): chooses the definition, moves a request through its steps
/// with the skip rules, assigns approvers, and tells the module and the people involved. Changes are only added to
/// the request; the caller's unit of work saves them with the outbox messages.
/// </summary>
public sealed partial class ApprovalEngine(
    IPlatformDbContext db,
    ApprovalDocumentTypeRegistry documentTypes,
    ApproverResolverRegistry resolvers,
    IIntegrationEventPublisher publisher,
    TimeProvider timeProvider,
    ILogger<ApprovalEngine> logger)
{
    /// <summary>Timeline reasons and the reasons of ApprovalRequestCompleted.</summary>
    public static class Reasons
    {
        public const string NoDefinition = "no_definition";
        public const string Condition = "condition";
        public const string Requester = "requester";
        public const string AlreadyApproved = "already_approved";
        public const string NoApprover = "no_approver";
        public const string InvalidCondition = "invalid_condition";
    }

    /// <summary>Path of a request in the web application; notifications link to it.</summary>
    public static string Link(ApprovalRequestId id) => $"/approvals/requests/{id.Value}";

    /// <summary>
    /// The definition for a document (§2.2): a company's own before the general ones, lower priority first, the
    /// first whose entry condition the facts meet. Null when none matches.
    /// </summary>
    public async Task<ApprovalDefinition?> SelectDefinitionAsync(ApprovalDocumentType type, Guid? companyId, JsonElement facts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(type);
        var candidates = await db.ApprovalDefinitions
            .Where(d => d.DocumentType == type.Key && d.IsActive && (d.CompanyId == null || d.CompanyId == companyId))
            .ToListAsync(cancellationToken);

        foreach (var definition in candidates.OrderBy(d => d.CompanyId is null).ThenBy(d => d.Priority).ThenBy(d => d.Id.Value))
        {
            var condition = ApprovalConditions.Parse(definition.EntryCondition, type.Facts, "entryCondition");
            if (condition.IsFailure)
            {
                // Saved conditions are checked; a fact the module removed later makes the definition unusable
                LogInvalidCondition(logger, definition.Id.Value, type.Key);
                continue;
            }

            if (ApprovalConditions.Evaluate(condition.Value, facts))
            {
                return definition;
            }
        }

        return null;
    }

    /// <summary>
    /// Moves the request from its current step to the next one that has approvers, skipping steps whose condition
    /// fails or whose approvers need not decide (§2.5). Approves the request when no step is left; marks it "needs
    /// attention" when a step finds nobody (§2.4).
    /// </summary>
    public async Task AdvanceAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var type = documentTypes.Find(request.DocumentType)
            ?? throw new InvalidOperationException($"Approval document type {request.DocumentType} is not registered.");
        var now = timeProvider.GetUtcNow();
        using var facts = JsonDocument.Parse(request.Facts);
        var context = new ApproverResolutionContext(request.DocumentType, request.CompanyId, request.RequesterUserId, request.RequesterEmployeeId, facts.RootElement);

        while (request.NextStep is { } index)
        {
            var step = request.Steps[index];
            var condition = ApprovalConditions.Parse(step.Condition, type.Facts, "condition");
            if (condition.IsFailure)
            {
                LogInvalidCondition(logger, request.DefinitionId ?? Guid.Empty, type.Key);
                await NeedAttentionAsync(request, index, Reasons.InvalidCondition, now, cancellationToken);
                return;
            }

            if (!ApprovalConditions.Evaluate(condition.Value, facts.RootElement))
            {
                request.SkipStep(index, Reasons.Condition, now);
                continue;
            }

            var (approvers, skip) = Choose(request, step, await ResolveAsync(step.Approver, context, cancellationToken));
            if (skip is not null)
            {
                request.SkipStep(index, skip, now);
                continue;
            }

            if (approvers.Count == 0 && request.Fallback is { } fallback)
            {
                approvers = [.. (await ResolveAsync(fallback, context, cancellationToken))
                    .Where(user => !step.SkipIfApproverIsRequester || user != request.RequesterUserId)];
            }

            if (approvers.Count == 0)
            {
                await NeedAttentionAsync(request, index, Reasons.NoApprover, now, cancellationToken);
                return;
            }

            var tasks = request.OpenStep(index, approvers, step.DueHours is { } hours ? now.AddHours(hours) : null, now);
            await AssignedAsync(request, [.. tasks.Select(t => t.AssigneeUserId)], cancellationToken);
            return;
        }

        request.Approve(now);
        await CompletedAsync(request, ApprovalOutcome.Approved, null, cancellationToken);
    }

    /// <summary>No definition matched (§2.2): approved at once or left to an administrator.</summary>
    public async Task NoDefinitionAsync(ApprovalRequest request, ApprovalDocumentType type, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(type);
        var now = timeProvider.GetUtcNow();
        if (type.AutoApproveWhenNoDefinition)
        {
            request.Approve(now, Reasons.NoDefinition);
            await CompletedAsync(request, ApprovalOutcome.Approved, Reasons.NoDefinition, cancellationToken);
            return;
        }

        await NeedAttentionAsync(request, null, Reasons.NoDefinition, now, cancellationToken);
    }

    /// <summary>The module learns the outcome (§2.7).</summary>
    public Task CompletedAsync(ApprovalRequest request, ApprovalOutcome outcome, string? reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return publisher.PublishAsync(
            new ApprovalRequestCompleted(request.DocumentType, request.DocumentId, outcome, request.CompletedAt ?? timeProvider.GetUtcNow(), reason)
            {
                CompanyId = request.CompanyId,
            },
            cancellationToken);
    }

    /// <summary>The approvers and those who act for them get a notification; the module learns who got the task.</summary>
    public async Task AssignedAsync(ApprovalRequest request, IReadOnlyList<Guid> assignees, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(assignees);
        await publisher.PublishAsync(
            new ApprovalTaskAssigned(request.DocumentType, request.DocumentId, request.Id.Value, request.CurrentStep ?? 0, assignees) { CompanyId = request.CompanyId },
            cancellationToken);

        var delegates = await DelegatesAsync(request.DocumentType, assignees, cancellationToken);
        await NotifyAsync(request, PlatformNotificationTypes.ApprovalAssigned, [.. assignees.Concat(delegates).Distinct()], cancellationToken);
    }

    public Task NotifyAsync(ApprovalRequest request, string type, IReadOnlyList<Guid> recipients, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (recipients.Count == 0)
        {
            return Task.CompletedTask;
        }

        return publisher.PublishAsync(
            new NotificationRequested(
                type,
                recipients,
                new Dictionary<string, string> { ["summary"] = request.Summary ?? request.DocumentId },
                Link(request.Id))
            {
                CompanyId = request.CompanyId,
            },
            cancellationToken);
    }

    /// <summary>Users who may act for <paramref name="principals"/> on the document type today (§2.6).</summary>
    public async Task<IReadOnlyList<Guid>> DelegatesAsync(string documentType, IReadOnlyCollection<Guid> principals, CancellationToken cancellationToken)
    {
        var today = PlatformClock.Today(timeProvider);
        var delegations = await db.Delegations
            .Where(d => principals.Contains(d.PrincipalUserId) && !d.IsRevoked && d.From <= today && d.To >= today)
            .ToListAsync(cancellationToken);
        return [.. delegations.Where(d => d.Covers(documentType, today)).Select(d => d.DelegateUserId).Distinct()];
    }

    /// <summary>Resolver result limited to active users; an unknown resolver finds nobody.</summary>
    public async Task<IReadOnlyList<Guid>> ResolveAsync(ApproverRule rule, ApproverResolutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (resolvers.Find(rule.Resolver) is not { } resolver)
        {
            LogUnknownResolver(logger, rule.Resolver, context.DocumentType);
            return [];
        }

        var users = await resolver.ResolveAsync(context, rule.Parameters, cancellationToken);
        return await ActiveUsers.FilterAsync(db, users, cancellationToken);
    }

    /// <summary>
    /// The step's approvers after the skip rules (§2.5), or why the step is skipped. The requester is dropped, so a
    /// step only they would approve is skipped; in Any mode an approver who approved earlier in the round completes
    /// the step at once, in All mode only those who did not approve yet are asked. Empty without a reason: nobody
    /// was found.
    /// </summary>
    internal static (IReadOnlyList<Guid> Approvers, string? Skip) Choose(ApprovalRequest request, ApprovalStep step, IReadOnlyList<Guid> resolved) =>
        Choose(request.RequesterUserId, request.ApprovedInRound(), step, resolved);

    internal static (IReadOnlyList<Guid> Approvers, string? Skip) Choose(Guid requester, IReadOnlySet<Guid> approved, ApprovalStep step, IReadOnlyList<Guid> resolved)
    {
        if (resolved.Count == 0)
        {
            return ([], null);
        }

        var approvers = resolved.Distinct().ToList();
        if (step.SkipIfApproverIsRequester)
        {
            approvers.Remove(requester);
            if (approvers.Count == 0)
            {
                return ([], Reasons.Requester);
            }
        }

        if (step.SkipIfAlreadyApproved)
        {
            if (step.Mode == ApprovalMode.Any && approvers.Exists(approved.Contains))
            {
                return ([], Reasons.AlreadyApproved);
            }

            approvers.RemoveAll(approved.Contains);
            if (approvers.Count == 0)
            {
                return ([], Reasons.AlreadyApproved);
            }
        }

        return (approvers, null);
    }

    /// <summary>
    /// The steps a document would go through now, with their approvers (§2.7 preview). Nothing is saved. Who
    /// approves a step of several approvers in Any mode is not known yet, so only a step's single approver, or every
    /// approver of an All step, counts as having approved for <see cref="ApprovalStep.SkipIfAlreadyApproved"/>.
    /// </summary>
    public async Task<ApprovalPreviewPlan> PreviewAsync(ApprovalDocumentType type, ApproverResolutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(context);
        var definition = await SelectDefinitionAsync(type, context.CompanyId, context.Facts, cancellationToken);
        if (definition is null)
        {
            return new ApprovalPreviewPlan(null, []);
        }

        var approved = new HashSet<Guid>();
        var steps = new List<ApprovalPreviewPlanStep>();
        for (var index = 0; index < definition.Steps.Count; index++)
        {
            var step = definition.Steps[index];
            var condition = ApprovalConditions.Parse(step.Condition, type.Facts, "condition");
            if (condition.IsFailure)
            {
                steps.Add(new ApprovalPreviewPlanStep(index, step, [], null, NeedsAttention: true));
                break;
            }

            if (!ApprovalConditions.Evaluate(condition.Value, context.Facts))
            {
                steps.Add(new ApprovalPreviewPlanStep(index, step, [], Reasons.Condition, NeedsAttention: false));
                continue;
            }

            var (approvers, skip) = Choose(context.RequesterUserId, approved, step, await ResolveAsync(step.Approver, context, cancellationToken));
            if (skip is null && approvers.Count == 0 && definition.Fallback is { } fallback)
            {
                approvers = [.. (await ResolveAsync(fallback, context, cancellationToken))
                    .Where(user => !step.SkipIfApproverIsRequester || user != context.RequesterUserId)];
            }

            var needsAttention = skip is null && approvers.Count == 0;
            steps.Add(new ApprovalPreviewPlanStep(index, step, approvers, skip, needsAttention));
            if (needsAttention)
            {
                break;
            }

            if (approvers.Count == 1 || step.Mode == ApprovalMode.All)
            {
                approved.UnionWith(approvers);
            }
        }

        return new ApprovalPreviewPlan(definition, steps);
    }

    private async Task NeedAttentionAsync(ApprovalRequest request, int? step, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        request.NeedAttention(step, reason, now);
        var administrators = await PermissionHolders.UserIdsAsync(db, PlatformPermissions.Approvals.Admin, PlatformClock.Today(timeProvider), cancellationToken);
        await NotifyAsync(request, PlatformNotificationTypes.ApprovalNeedsAttention, administrators, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Approval definition {DefinitionId} of {DocumentType} has a condition that no longer fits the facts")]
    private static partial void LogInvalidCondition(ILogger logger, Guid definitionId, string documentType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Approver resolver {Resolver} used by {DocumentType} is not registered")]
    private static partial void LogUnknownResolver(ILogger logger, string resolver, string documentType);
}

/// <summary>The flow a document would get (null: none matches) and what each step would do.</summary>
public sealed record ApprovalPreviewPlan(ApprovalDefinition? Definition, IReadOnlyList<ApprovalPreviewPlanStep> Steps);

public sealed record ApprovalPreviewPlanStep(int Index, ApprovalStep Step, IReadOnlyList<Guid> Approvers, string? SkipReason, bool NeedsAttention);
