using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Application.Access;
using Merp.Modules.Platform.Application.Notifications;
using Merp.Modules.Platform.Contracts.Approvals;
using Merp.Modules.Platform.Domain.Approvals;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Merp.Modules.Platform.Application.Approvals;

public static class ApprovalErrors
{
    public static readonly Error TenantScopeRequired = Error.Forbidden("platform.approvals.errors.tenant_scope_required");
    public static readonly Error OwnRequest = Error.Forbidden("platform.approvals.errors.own_request");
    public static readonly Error NoStep = Error.Conflict("platform.approvals.errors.no_step");
    public const string UnknownDocumentTypeCode = "platform.approvals.errors.unknown_document_type";
    public const string UnknownUserCode = "platform.approvals.errors.unknown_user";
}

/// <summary>
/// Starts the flow of a document sent with <see cref="ApprovalRequested"/>, or a new round of a returned one (§2.7).
/// A document already under approval or decided is left alone, so a repeated event changes nothing. System command
/// of the inbox handler.
/// </summary>
public sealed record StartApprovalCommand(ApprovalRequested Request) : ICommand;

/// <summary>The module withdrew the document (§2.7): its open tasks close. System command of the inbox handler.</summary>
public sealed record CancelApprovalCommand(string DocumentType, string DocumentId) : ICommand;

/// <summary>
/// The caller decides a task: as its assignee, or for the assignee as their delegate (§2.6). Reject and return need
/// a comment. Two decisions at once on a request: the second gets 409.
/// </summary>
public sealed record DecideApprovalTaskCommand(ApprovalDecision Decision, string? Comment) : ICommand
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid TaskId { get; init; }
}

/// <summary>An administrator approves the current step, or a request that needs attention (§2.6).</summary>
public sealed record ForceApproveApprovalCommand(string? Comment) : ICommand, IRequirePermission
{
    [JsonIgnore]
    public Guid RequestId { get; init; }

    [JsonIgnore]
    public string Permission => PlatformPermissions.Approvals.Admin;
}

/// <summary>An administrator moves an open task to another user (§2.6).</summary>
public sealed record ReassignApprovalTaskCommand(Guid UserId) : ICommand, IRequirePermission
{
    [JsonIgnore]
    public Guid TaskId { get; init; }

    [JsonIgnore]
    public string Permission => PlatformPermissions.Approvals.Admin;
}

/// <summary>An administrator gives approvers to the step of a request that needs attention (§2.4, §2.6).</summary>
public sealed record AssignApproversCommand(IReadOnlyList<Guid>? UserIds) : ICommand, IRequirePermission
{
    [JsonIgnore]
    public Guid RequestId { get; init; }

    [JsonIgnore]
    public string Permission => PlatformPermissions.Approvals.Admin;
}

/// <summary>Handles the overdue tasks of a request by its step's escalation (§2.5). System command of the job.</summary>
public sealed record EscalateApprovalCommand(Guid RequestId) : ICommand;

/// <summary>Requests with an overdue task not handled yet, for the escalation job.</summary>
public sealed record GetOverdueApprovalRequestsQuery(int BatchSize = 200) : IQuery<IReadOnlyList<Guid>>;

internal static class ApprovalRequests
{
    public static Task<ApprovalRequest?> FindAsync(IPlatformDbContext db, ApprovalRequestId id, CancellationToken cancellationToken) =>
        db.ApprovalRequests.Include(r => r.Tasks).Include(r => r.History).SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

    public static Task<ApprovalRequest?> FindByTaskAsync(IPlatformDbContext db, ApprovalTaskId taskId, CancellationToken cancellationToken) =>
        db.ApprovalRequests.Include(r => r.Tasks).Include(r => r.History).SingleOrDefaultAsync(r => r.Tasks.Any(t => t.Id == taskId), cancellationToken);
}

internal sealed partial class StartApprovalCommandHandler(
    IPlatformDbContext db,
    ApprovalDocumentTypeRegistry documentTypes,
    ApprovalEngine engine,
    TimeProvider timeProvider,
    ILogger<StartApprovalCommandHandler> logger) : ICommandHandler<StartApprovalCommand>
{
    public async Task<Result> Handle(StartApprovalCommand request, CancellationToken cancellationToken)
    {
        var requested = request.Request;
        if (documentTypes.Find(requested.DocumentType) is not { } type)
        {
            return Error.Validation([new FieldError("documentType", ApprovalErrors.UnknownDocumentTypeCode)]);
        }

        var now = timeProvider.GetUtcNow();
        var content = new ApprovalContent(requested.Facts.GetRawText(), requested.TitleKey, requested.TitleArgs, requested.Summary, requested.Link);
        var existing = await db.ApprovalRequests.Include(r => r.Tasks).Include(r => r.History)
            .SingleOrDefaultAsync(r => r.DocumentType == requested.DocumentType && r.DocumentId == requested.DocumentId, cancellationToken);
        if (existing is not null)
        {
            if (existing.Status != ApprovalStatus.Returned)
            {
                LogAlreadyStarted(logger, requested.DocumentType, requested.DocumentId, existing.Status);
                return Result.Success();
            }

            var resubmitted = existing.Resubmit(content, now);
            if (resubmitted.IsSuccess)
            {
                await engine.AdvanceAsync(existing, cancellationToken);
            }

            return resubmitted;
        }

        var definition = await engine.SelectDefinitionAsync(type, requested.CompanyId, requested.Facts, cancellationToken);
        var approval = ApprovalRequest.Submit(
            requested.DocumentType,
            requested.DocumentId,
            requested.CompanyId,
            requested.RequesterUserId,
            requested.RequesterEmployeeId,
            content,
            definition,
            now);
        db.ApprovalRequests.Add(approval);

        if (definition is null)
        {
            await engine.NoDefinitionAsync(approval, type, cancellationToken);
        }
        else
        {
            await engine.AdvanceAsync(approval, cancellationToken);
        }

        return Result.Success();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Approval of {DocumentType} {DocumentId} is already {Status}; the request is ignored")]
    private static partial void LogAlreadyStarted(ILogger logger, string documentType, string documentId, ApprovalStatus status);
}

internal sealed class CancelApprovalCommandHandler(IPlatformDbContext db, TimeProvider timeProvider) : ICommandHandler<CancelApprovalCommand>
{
    public async Task<Result> Handle(CancelApprovalCommand request, CancellationToken cancellationToken)
    {
        var approval = await db.ApprovalRequests.Include(r => r.Tasks).Include(r => r.History)
            .SingleOrDefaultAsync(r => r.DocumentType == request.DocumentType && r.DocumentId == request.DocumentId, cancellationToken);

        // Not there yet: the inbox retries, so a cancellation overtaking its request still lands
        if (approval is null)
        {
            return CommonErrors.NotFound;
        }

        // Decided or cancelled already: nothing is open
        _ = approval.Cancel(timeProvider.GetUtcNow());
        return Result.Success();
    }
}

internal sealed class DecideApprovalTaskCommandHandler(
    IPlatformDbContext db,
    ICurrentUser currentUser,
    ApprovalEngine engine,
    IIntegrationEventPublisher publisher,
    TimeProvider timeProvider) : ICommandHandler<DecideApprovalTaskCommand>
{
    public async Task<Result> Handle(DecideApprovalTaskCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return CommonErrors.Forbidden;
        }

        var taskId = ApprovalTaskId.From(request.TaskId);
        var approval = await ApprovalRequests.FindByTaskAsync(db, taskId, cancellationToken);
        var task = approval?.Tasks.Single(t => t.Id == taskId);
        if (approval is null || task is null)
        {
            return CommonErrors.NotFound;
        }

        if (task.AssigneeUserId != userId)
        {
            // Someone else's task: only a delegate of the assignee may see it (§2.6)
            var delegates = await engine.DelegatesAsync(approval.DocumentType, [task.AssigneeUserId], cancellationToken);
            if (!delegates.Contains(userId))
            {
                return CommonErrors.NotFound;
            }

            if (approval.RequesterUserId == userId)
            {
                return ApprovalErrors.OwnRequest;
            }
        }

        var now = timeProvider.GetUtcNow();
        var decided = approval.Decide(taskId, userId, request.Decision, request.Comment, now);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        switch (decided.Value)
        {
            case DecisionEffect.StepCompleted:
                await engine.AdvanceAsync(approval, cancellationToken);
                break;
            case DecisionEffect.Rejected:
                await engine.CompletedAsync(approval, ApprovalOutcome.Rejected, null, cancellationToken);
                break;
            case DecisionEffect.Returned:
                await publisher.PublishAsync(
                    new ApprovalRequestReturned(approval.DocumentType, approval.DocumentId, userId, request.Comment!.Trim(), now) { CompanyId = approval.CompanyId },
                    cancellationToken);
                break;
        }

        return Result.Success();
    }
}

internal sealed class ForceApproveApprovalCommandHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    ICurrentUser currentUser,
    ApprovalEngine engine,
    TimeProvider timeProvider) : ICommandHandler<ForceApproveApprovalCommand>
{
    public async Task<Result> Handle(ForceApproveApprovalCommand request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return ApprovalErrors.TenantScopeRequired;
        }

        var approval = await ApprovalRequests.FindAsync(db, ApprovalRequestId.From(request.RequestId), cancellationToken);
        if (approval is null)
        {
            return CommonErrors.NotFound;
        }

        var approved = approval.ForceApprove(currentUser.UserId!.Value, request.Comment, timeProvider.GetUtcNow());
        if (approved.IsSuccess)
        {
            await engine.AdvanceAsync(approval, cancellationToken);
        }

        return approved;
    }
}

internal sealed class ReassignApprovalTaskCommandHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    ICurrentUser currentUser,
    ApprovalEngine engine,
    TimeProvider timeProvider) : ICommandHandler<ReassignApprovalTaskCommand>
{
    public async Task<Result> Handle(ReassignApprovalTaskCommand request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return ApprovalErrors.TenantScopeRequired;
        }

        var taskId = ApprovalTaskId.From(request.TaskId);
        var approval = await ApprovalRequests.FindByTaskAsync(db, taskId, cancellationToken);
        if (approval is null)
        {
            return CommonErrors.NotFound;
        }

        if ((await ActiveUsers.FilterAsync(db, [request.UserId], cancellationToken)).Count == 0)
        {
            return Error.Validation([new FieldError("userId", ApprovalErrors.UnknownUserCode)]);
        }

        var moved = approval.Reassign(taskId, request.UserId, currentUser.UserId, escalation: false, timeProvider.GetUtcNow());
        if (moved.IsFailure)
        {
            return moved.Error;
        }

        await engine.AssignedAsync(approval, [request.UserId], cancellationToken);
        return Result.Success();
    }
}

internal sealed class AssignApproversCommandValidator : AbstractValidator<AssignApproversCommand>
{
    public AssignApproversCommandValidator() => RuleFor(c => c.UserIds).NotEmpty();
}

internal sealed class AssignApproversCommandHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    ApprovalEngine engine,
    TimeProvider timeProvider) : ICommandHandler<AssignApproversCommand>
{
    public async Task<Result> Handle(AssignApproversCommand request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return ApprovalErrors.TenantScopeRequired;
        }

        var approval = await ApprovalRequests.FindAsync(db, ApprovalRequestId.From(request.RequestId), cancellationToken);
        if (approval is null)
        {
            return CommonErrors.NotFound;
        }

        if (approval.Status != ApprovalStatus.NeedsAttention)
        {
            return ApprovalRequest.NotPending;
        }

        // Without a definition there is no step to assign; the administrator approves instead
        if (approval.Step is not { } step)
        {
            return ApprovalErrors.NoStep;
        }

        var users = request.UserIds!.Distinct().ToList();
        var active = await ActiveUsers.FilterAsync(db, users, cancellationToken);
        if (active.Count != users.Count)
        {
            return Error.Validation([new FieldError("userIds", ApprovalErrors.UnknownUserCode)]);
        }

        var now = timeProvider.GetUtcNow();
        var tasks = approval.OpenStep(approval.CurrentStep!.Value, active, step.DueHours is { } hours ? now.AddHours(hours) : null, now);
        await engine.AssignedAsync(approval, [.. tasks.Select(t => t.AssigneeUserId)], cancellationToken);
        return Result.Success();
    }
}

internal sealed class EscalateApprovalCommandHandler(
    IPlatformDbContext db,
    ApprovalEngine engine,
    IEnumerable<IApproverManagerLookup> managerLookups,
    TimeProvider timeProvider) : ICommandHandler<EscalateApprovalCommand>
{
    public async Task<Result> Handle(EscalateApprovalCommand request, CancellationToken cancellationToken)
    {
        var approval = await ApprovalRequests.FindAsync(db, ApprovalRequestId.From(request.RequestId), cancellationToken);
        if (approval is not { Status: ApprovalStatus.Pending, Step: { } step })
        {
            return Result.Success();
        }

        var now = timeProvider.GetUtcNow();
        var overdue = approval.OpenTasks.Where(t => t.DueAt <= now && t.EscalatedAt is null).ToList();
        if (overdue.Count == 0)
        {
            return Result.Success();
        }

        switch (step.Escalation)
        {
            case ApprovalEscalation.AutoApprove:
                if (approval.DecideOverdue(ApprovalDecision.Approve, now).IsSuccess)
                {
                    await engine.AdvanceAsync(approval, cancellationToken);
                }

                return Result.Success();

            case ApprovalEscalation.AutoReject:
                if (approval.DecideOverdue(ApprovalDecision.Reject, now).IsSuccess)
                {
                    await engine.CompletedAsync(approval, ApprovalOutcome.Rejected, ApprovalRequest.OverdueReason, cancellationToken);
                }

                return Result.Success();

            case ApprovalEscalation.EscalateToManager:
                await EscalateToManagersAsync(approval, overdue, now, cancellationToken);
                return Result.Success();

            default:
                await RemindAsync(approval, overdue, now, cancellationToken);
                return Result.Success();
        }
    }

    /// <summary>Each overdue task moves to its approver's manager; a task whose approver has none is reminded (D34).</summary>
    private async Task EscalateToManagersAsync(ApprovalRequest approval, List<ApprovalTask> overdue, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var lookup = managerLookups.FirstOrDefault();
        var unmoved = new List<ApprovalTask>();
        foreach (var task in overdue)
        {
            var manager = lookup is null ? null : await lookup.FindManagerUserIdAsync(task.AssigneeUserId, cancellationToken);
            var assigned = approval.OpenTasks.Select(t => t.AssigneeUserId).ToHashSet();
            if (manager is { } managerId
                && !assigned.Contains(managerId)
                && managerId != approval.RequesterUserId
                && (await ActiveUsers.FilterAsync(db, [managerId], cancellationToken)).Count == 1)
            {
                _ = approval.Reassign(task.Id, managerId, actor: null, escalation: true, now);
                await engine.AssignedAsync(approval, [managerId], cancellationToken);
            }
            else
            {
                unmoved.Add(task);
            }
        }

        await RemindAsync(approval, unmoved, now, cancellationToken);
    }

    private async Task RemindAsync(ApprovalRequest approval, List<ApprovalTask> tasks, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (tasks.Count == 0)
        {
            return;
        }

        foreach (var task in tasks)
        {
            approval.Remind(task.Id, now);
        }

        var assignees = tasks.Select(t => t.AssigneeUserId).Distinct().ToList();
        var delegates = await engine.DelegatesAsync(approval.DocumentType, assignees, cancellationToken);
        await engine.NotifyAsync(approval, PlatformNotificationTypes.ApprovalOverdue, [.. assignees.Concat(delegates).Distinct()], cancellationToken);
    }
}

internal sealed class GetOverdueApprovalRequestsQueryHandler(IPlatformDbContext db, TimeProvider timeProvider)
    : IQueryHandler<GetOverdueApprovalRequestsQuery, IReadOnlyList<Guid>>
{
    public async Task<Result<IReadOnlyList<Guid>>> Handle(GetOverdueApprovalRequestsQuery request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var ids = await db.ApprovalRequests.AsNoTracking()
            .Where(r => r.Status == ApprovalStatus.Pending
                && r.Tasks.Any(t => t.Status == ApprovalTaskStatus.Pending && t.DueAt <= now && t.EscalatedAt == null))
            .OrderBy(r => r.LastActivityAt)
            .Select(r => r.Id.Value)
            .Take(request.BatchSize)
            .ToListAsync(cancellationToken);
        return ids;
    }
}

/// <summary>Inbox: a module sent a document for approval (§2.7).</summary>
internal sealed class ApprovalRequestedHandler(ISender sender) : IIntegrationEventHandler<ApprovalRequested>
{
    public async Task Handle(ApprovalRequested integrationEvent, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new StartApprovalCommand(integrationEvent), cancellationToken);

        // A wrong event is the sender's programming error: let the inbox retry and dead-letter it visibly
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Starting the approval of {integrationEvent.DocumentType} {integrationEvent.DocumentId} failed: {result.Error.Code}");
        }
    }
}

/// <summary>Inbox: a module withdrew a document (§2.7).</summary>
internal sealed class ApprovalCancellationRequestedHandler(ISender sender) : IIntegrationEventHandler<ApprovalCancellationRequested>
{
    public async Task Handle(ApprovalCancellationRequested integrationEvent, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CancelApprovalCommand(integrationEvent.DocumentType, integrationEvent.DocumentId), cancellationToken);
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Cancelling the approval of {integrationEvent.DocumentType} {integrationEvent.DocumentId} failed: {result.Error.Code}");
        }
    }
}
