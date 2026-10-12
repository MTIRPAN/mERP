using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Merp.Application.Abstractions.Approvals;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Persistence;
using Merp.Modules.Platform.Application.Access;
using Merp.Modules.Platform.Contracts.Approvals;
using Merp.Modules.Platform.Domain.Approvals;
using Merp.Modules.Platform.Domain.Companies;
using Merp.Modules.Platform.Domain.Users;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Approvals;

// Reading approvals (PLAN P10b). Document types, the preview, the caller's tasks, requests they may see and their
// delegations only need a signed-in user (D22, D34); definitions and the request list need their permissions with
// the Tenant scope.

public sealed record ApprovalFactItem(string Name, ApprovalFactType Type, string Label);

public sealed record ApprovalDocumentTypeItem(string Key, string Name, IReadOnlyList<ApprovalFactItem> Facts, bool AutoApproveWhenNoDefinition);

/// <summary>Approvable document types of all modules, for the flow editor and delegations.</summary>
public sealed record GetApprovalDocumentTypesQuery : IQuery<IReadOnlyList<ApprovalDocumentTypeItem>>;

public sealed record ApproverParameterItem(string Name, string Label, bool Required);

public sealed record ApproverResolverItem(string Key, string Name, IReadOnlyList<ApproverParameterItem> Parameters);

/// <summary>Approver resolvers of all modules with their parameters, for the flow editor.</summary>
public sealed record GetApproverResolversQuery : IQuery<IReadOnlyList<ApproverResolverItem>>, IRequirePermission
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.ApprovalDefinitions.Read;
}

/// <remarks>Name holds a text per culture.</remarks>
public sealed record ApprovalDefinitionListItem(
    Guid Id,
    string DocumentType,
    string DocumentTypeName,
    Guid? CompanyId,
    string? CompanyName,
    IReadOnlyDictionary<string, string> Name,
    int Priority,
    int StepCount,
    bool IsActive,
    int Revision);

/// <summary>Flows of a document type (or all), active or not.</summary>
public sealed record GetApprovalDefinitionsQuery(string? DocumentType) : IQuery<IReadOnlyList<ApprovalDefinitionListItem>>, IRequirePermission
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.ApprovalDefinitions.Read;
}

public sealed record ApproverRuleItem(string Resolver, IReadOnlyDictionary<string, string> Parameters);

public sealed record ApprovalStepItem(
    IReadOnlyDictionary<string, string> Name,
    ApproverRuleItem Approver,
    ApprovalMode Mode,
    JsonElement? Condition,
    int? DueHours,
    ApprovalEscalation Escalation,
    bool SkipIfApproverIsRequester,
    bool SkipIfAlreadyApproved,
    bool AllowReturn);

/// <remarks>Send Version back when updating.</remarks>
public sealed record ApprovalDefinitionDetails(
    Guid Id,
    string DocumentType,
    Guid? CompanyId,
    IReadOnlyDictionary<string, string> Name,
    int Priority,
    JsonElement? EntryCondition,
    IReadOnlyList<ApprovalStepItem> Steps,
    ApproverRuleItem? Fallback,
    bool IsActive,
    int Revision,
    uint Version);

public sealed record GetApprovalDefinitionQuery(Guid Id) : IQuery<ApprovalDefinitionDetails>, IRequirePermission
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.ApprovalDefinitions.Read;
}

/// <summary>Who would approve a document of the caller now (§2.7); the module's form shows it before sending.</summary>
public sealed record PreviewApprovalQuery(string? DocumentType, Guid? CompanyId, JsonElement? Facts) : IQuery<ApprovalPreview>;

/// <remarks>OnBehalfOf is set when the caller acts as a delegate: the approver the task belongs to.</remarks>
public sealed record MyApprovalTaskItem(
    Guid TaskId,
    Guid RequestId,
    string DocumentType,
    string DocumentTypeName,
    string DocumentId,
    string Title,
    string? Summary,
    string? Link,
    Guid RequesterUserId,
    string RequesterName,
    int Step,
    string StepName,
    int StepCount,
    bool AllowReturn,
    Guid? OnBehalfOfUserId,
    string? OnBehalfOfName,
    DateTimeOffset SubmittedAt,
    DateTimeOffset AssignedAt,
    DateTimeOffset? DueAt);

/// <summary>
/// The caller's open tasks and those of the approvers they act for today (§2.6), the most urgent first: due ones by
/// their due time, then the oldest.
/// </summary>
public sealed record GetMyApprovalTasksQuery(PageRequest Page) : IQuery<PagedResult<MyApprovalTaskItem>>;

public sealed record ApprovalTaskCount(int Count);

/// <summary>The number on the approval badge.</summary>
public sealed record GetMyApprovalTaskCountQuery : IQuery<ApprovalTaskCount>;

public sealed record ApprovalRequestStepItem(int Index, string Name, ApprovalMode Mode, bool AllowReturn, int? DueHours);

public sealed record ApprovalTaskItem(
    Guid Id,
    int Round,
    int Step,
    Guid AssigneeUserId,
    string AssigneeName,
    ApprovalTaskStatus Status,
    Guid? DecidedByUserId,
    string? DecidedByName,
    Guid? OnBehalfOfUserId,
    string? OnBehalfOfName,
    string? Comment,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DueAt,
    DateTimeOffset? DecidedAt);

/// <remarks>
/// Detail is a comment, a skip or completion reason, or the user id a task moved to (then DetailUserName names
/// them). UserId is null for the system.
/// </remarks>
public sealed record ApprovalHistoryItem(
    int Sequence,
    ApprovalEventKind Kind,
    int Round,
    int? Step,
    Guid? UserId,
    string? UserName,
    Guid? OnBehalfOfUserId,
    string? OnBehalfOfName,
    string? Detail,
    string? DetailUserName,
    DateTimeOffset At);

/// <remarks>
/// MyTaskId is the open task the caller may decide (as approver or delegate); CanAdminister allows reassigning,
/// approving in place of the approvers and assigning approvers.
/// </remarks>
public sealed record ApprovalRequestDetails(
    Guid Id,
    string DocumentType,
    string DocumentTypeName,
    string DocumentId,
    Guid? CompanyId,
    string Title,
    string? Summary,
    string? Link,
    Guid RequesterUserId,
    string RequesterName,
    ApprovalStatus Status,
    int Round,
    int? CurrentStep,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<ApprovalRequestStepItem> Steps,
    IReadOnlyList<ApprovalTaskItem> Tasks,
    IReadOnlyList<ApprovalHistoryItem> History,
    Guid? MyTaskId,
    bool CanAdminister);

/// <summary>
/// A request with its tasks and timeline. Visible to the requester, its approvers and their delegates, approval
/// administrators and those the document's module lets read the document; otherwise 404.
/// </summary>
public sealed record GetApprovalRequestQuery(Guid Id) : IQuery<ApprovalRequestDetails>;

/// <summary>The request of a document, for <c>&lt;ApprovalTimeline/&gt;</c> on the module's screen; same visibility.</summary>
public sealed record GetApprovalTimelineQuery(string? DocumentType, string? DocumentId) : IQuery<ApprovalRequestDetails>;

public sealed record ApprovalRequestListItem(
    Guid Id,
    string DocumentType,
    string DocumentTypeName,
    string DocumentId,
    string Title,
    string? Summary,
    string RequesterName,
    ApprovalStatus Status,
    int? CurrentStep,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? CompletedAt);

/// <summary>Requests of all modules for approval administrators, e.g. those that need attention; newest first.</summary>
public sealed record GetApprovalRequestsQuery(PageRequest Page, ApprovalStatus? Status, string? DocumentType)
    : IQuery<PagedResult<ApprovalRequestListItem>>, IRequirePermission
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.Approvals.Admin;
}

/// <remarks>Given is true for the caller's own delegations, false for those given to them.</remarks>
public sealed record DelegationItem(
    Guid Id,
    Guid PrincipalUserId,
    string PrincipalName,
    Guid DelegateUserId,
    string DelegateName,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<string> DocumentTypes,
    IReadOnlyList<string> DocumentTypeNames,
    bool IsRevoked,
    bool Given);

/// <summary>Delegations the caller gave and received, latest first.</summary>
public sealed record GetMyDelegationsQuery : IQuery<IReadOnlyList<DelegationItem>>;

public sealed record ApprovalUserOption(Guid Id, string Name);

/// <summary>Active users matching a search, at most 20, to pick a delegate or an approver.</summary>
public sealed record SearchApprovalUsersQuery(string? Search) : IQuery<IReadOnlyList<ApprovalUserOption>>;

internal static class ApprovalNames
{
    public static async Task<Dictionary<Guid, string>> UsersAsync(IPlatformDbContext db, IEnumerable<Guid?> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.OfType<Guid>().Distinct().Select(UserId.From).ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToListAsync(cancellationToken);
        return users.ToDictionary(u => u.Id.Value, u => u.DisplayName);
    }

    public static string? Name(this Dictionary<Guid, string> names, Guid? userId) =>
        userId is { } id ? names.GetValueOrDefault(id, id.ToString()) : null;

    public static string DocumentType(ApprovalDocumentTypeRegistry types, ITextLocalizer localizer, string key) =>
        types.Find(key) is { } type ? localizer.Get(type.NameKey) : key;

    public static string Title(ITextLocalizer localizer, string key, IReadOnlyDictionary<string, string> args) =>
        localizer.Get(key, args.ToDictionary(a => a.Key, object? (a) => a.Value));

    public static string Text(IReadOnlyDictionary<string, string> texts, string culture, string defaultCulture) =>
        LocalizedString.Create(texts.ToDictionary(t => t.Key, string? (t) => t.Value)) is { IsSuccess: true } text
            ? text.Value.Get(culture, defaultCulture)
            : string.Empty;

    public static JsonElement? Json(string? json) => json is null ? null : JsonDocument.Parse(json).RootElement.Clone();

    public static ApproverRuleItem Rule(ApproverRule rule) => new(rule.Resolver, rule.Parameters);
}

internal sealed class GetApprovalDocumentTypesQueryHandler(ApprovalDocumentTypeRegistry types, ITextLocalizer localizer)
    : IQueryHandler<GetApprovalDocumentTypesQuery, IReadOnlyList<ApprovalDocumentTypeItem>>
{
    public Task<Result<IReadOnlyList<ApprovalDocumentTypeItem>>> Handle(GetApprovalDocumentTypesQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success<IReadOnlyList<ApprovalDocumentTypeItem>>(
            [.. types.All
                .Select(t => new ApprovalDocumentTypeItem(
                    t.Key,
                    localizer.Get(t.NameKey),
                    [.. t.Facts.Select(f => new ApprovalFactItem(f.Name, f.Type, localizer.Get(f.LabelKey)))],
                    t.AutoApproveWhenNoDefinition))
                .OrderBy(t => t.Name, StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: true))]));
}

internal sealed class GetApproverResolversQueryHandler(ApproverResolverRegistry resolvers, IAccessScopeProvider scopes, ITextLocalizer localizer)
    : IQueryHandler<GetApproverResolversQuery, IReadOnlyList<ApproverResolverItem>>
{
    public async Task<Result<IReadOnlyList<ApproverResolverItem>>> Handle(GetApproverResolversQuery request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return ApprovalErrors.TenantScopeRequired;
        }

        return resolvers.All
            .Select(r => new ApproverResolverItem(
                r.Key,
                localizer.Get(r.NameKey),
                [.. r.Parameters.Select(p => new ApproverParameterItem(p.Name, localizer.Get(p.LabelKey), p.Required))]))
            .OrderBy(r => r.Key, StringComparer.Ordinal)
            .ToList();
    }
}

internal sealed class GetApprovalDefinitionsQueryHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    ApprovalDocumentTypeRegistry types,
    ITextLocalizer localizer) : IQueryHandler<GetApprovalDefinitionsQuery, IReadOnlyList<ApprovalDefinitionListItem>>
{
    public async Task<Result<IReadOnlyList<ApprovalDefinitionListItem>>> Handle(GetApprovalDefinitionsQuery request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return ApprovalErrors.TenantScopeRequired;
        }

        var query = db.ApprovalDefinitions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.DocumentType))
        {
            query = query.Where(d => d.DocumentType == request.DocumentType);
        }

        var definitions = await query.ToListAsync(cancellationToken);
        var companyIds = definitions.Select(d => d.CompanyId).OfType<Guid>().Distinct().Select(CompanyId.From).ToList();
        var companies = await db.Companies.AsNoTracking().Where(c => companyIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id.Value, c => c.ShortName, cancellationToken);

        return definitions
            .OrderBy(d => d.DocumentType, StringComparer.Ordinal)
            .ThenBy(d => d.CompanyId is null)
            .ThenBy(d => d.Priority)
            .Select(d => new ApprovalDefinitionListItem(
                d.Id.Value,
                d.DocumentType,
                ApprovalNames.DocumentType(types, localizer, d.DocumentType),
                d.CompanyId,
                d.CompanyId is { } id ? companies.GetValueOrDefault(id) : null,
                d.Name.Values,
                d.Priority,
                d.Steps.Count,
                d.IsActive,
                d.Revision))
            .ToList();
    }
}

internal sealed class GetApprovalDefinitionQueryHandler(IPlatformDbContext db, IAccessScopeProvider scopes)
    : IQueryHandler<GetApprovalDefinitionQuery, ApprovalDefinitionDetails>
{
    public async Task<Result<ApprovalDefinitionDetails>> Handle(GetApprovalDefinitionQuery request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return ApprovalErrors.TenantScopeRequired;
        }

        var row = await db.ApprovalDefinitions.AsNoTracking()
            .Where(d => d.Id == ApprovalDefinitionId.From(request.Id))
            .Select(d => new { Definition = d, Version = EF.Property<uint>(d, ConcurrencyVersion.PropertyName) })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return CommonErrors.NotFound;
        }

        var d = row.Definition;
        return new ApprovalDefinitionDetails(
            d.Id.Value,
            d.DocumentType,
            d.CompanyId,
            d.Name.Values,
            d.Priority,
            ApprovalNames.Json(d.EntryCondition),
            [.. d.Steps.Select(s => new ApprovalStepItem(
                s.Name,
                ApprovalNames.Rule(s.Approver),
                s.Mode,
                ApprovalNames.Json(s.Condition),
                s.DueHours,
                s.Escalation,
                s.SkipIfApproverIsRequester,
                s.SkipIfAlreadyApproved,
                s.AllowReturn))],
            d.Fallback is { } fallback ? ApprovalNames.Rule(fallback) : null,
            d.IsActive,
            d.Revision,
            row.Version);
    }
}

internal sealed class PreviewApprovalQueryHandler(ICurrentUser currentUser, IApprovalPreview preview, ApprovalDocumentTypeRegistry types)
    : IQueryHandler<PreviewApprovalQuery, ApprovalPreview>
{
    public async Task<Result<ApprovalPreview>> Handle(PreviewApprovalQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return CommonErrors.Forbidden;
        }

        if (types.Find(request.DocumentType) is null)
        {
            return Error.Validation([new FieldError("documentType", ApprovalErrors.UnknownDocumentTypeCode)]);
        }

        var facts = request.Facts is { ValueKind: JsonValueKind.Object } given ? given : JsonDocument.Parse("{}").RootElement;
        return await preview.PreviewAsync(request.DocumentType!, request.CompanyId, facts, userId, currentUser.EmployeeId, cancellationToken);
    }
}

/// <summary><see cref="IApprovalPreview"/> for modules and the preview endpoint; texts in the caller's language.</summary>
public sealed class ApprovalPreviewService(
    IPlatformDbContext db,
    ApprovalEngine engine,
    ApprovalDocumentTypeRegistry types,
    ICurrentUser currentUser,
    ILocalizationSource localization) : IApprovalPreview
{
    public async Task<ApprovalPreview> PreviewAsync(
        string documentType,
        Guid? companyId,
        JsonElement facts,
        Guid requesterUserId,
        Guid? requesterEmployeeId,
        CancellationToken cancellationToken = default)
    {
        var type = types.Find(documentType) ?? throw new ArgumentException($"Approval document type {documentType} is not registered.", nameof(documentType));
        var context = new ApproverResolutionContext(documentType, companyId, requesterUserId, requesterEmployeeId, facts);
        var plan = await engine.PreviewAsync(type, context, cancellationToken);
        if (plan.Definition is null)
        {
            return new ApprovalPreview(HasDefinition: false, type.AutoApproveWhenNoDefinition, []);
        }

        var names = await ApprovalNames.UsersAsync(db, plan.Steps.SelectMany(s => s.Approvers).Cast<Guid?>(), cancellationToken);
        var defaultCulture = (await localization.GetAsync(cancellationToken)).DefaultCulture;
        return new ApprovalPreview(
            HasDefinition: true,
            AutoApproved: false,
            [.. plan.Steps.Select(s => new ApprovalPreviewStep(
                s.Index,
                ApprovalNames.Text(s.Step.Name, currentUser.Culture, defaultCulture),
                [.. s.Approvers.Select(id => new ApprovalPreviewApprover(id, names.Name(id)!))],
                s.SkipReason,
                s.NeedsAttention))]);
    }
}

/// <summary>Delegations that let a user act for others today (§2.6).</summary>
internal static class ActingFor
{
    public static async Task<List<Delegation>> PrincipalsAsync(IPlatformDbContext db, Guid userId, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var today = PlatformClock.Today(timeProvider);
        return await db.Delegations.AsNoTracking()
            .Where(d => d.DelegateUserId == userId && !d.IsRevoked && d.From <= today && d.To >= today)
            .ToListAsync(cancellationToken);
    }
}

internal static class MyTasks
{
    public sealed record Row(ApprovalTask Task, ApprovalRequest Request);

    /// <summary>
    /// Open tasks of the caller and of those they act for. Few per user, so the delegations' document types are
    /// checked in memory.
    /// </summary>
    public static async Task<List<Row>> LoadAsync(IPlatformDbContext db, Guid userId, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var today = PlatformClock.Today(timeProvider);
        var delegations = await ActingFor.PrincipalsAsync(db, userId, timeProvider, cancellationToken);
        var principals = delegations.Select(d => d.PrincipalUserId).Append(userId).Distinct().ToList();

        var rows = await db.ApprovalRequests.AsNoTracking()
            .Where(r => r.Status == ApprovalStatus.Pending)
            .SelectMany(r => r.Tasks
                .Where(t => t.Status == ApprovalTaskStatus.Pending && principals.Contains(t.AssigneeUserId))
                .Select(t => new Row(t, r)))
            .ToListAsync(cancellationToken);

        return [.. rows.Where(row => row.Task.AssigneeUserId == userId
            || (row.Request.RequesterUserId != userId
                && delegations.Exists(d => d.PrincipalUserId == row.Task.AssigneeUserId && d.Covers(row.Request.DocumentType, today))))];
    }
}

internal sealed class GetMyApprovalTasksQueryHandler(
    IPlatformDbContext db,
    ICurrentUser currentUser,
    ApprovalDocumentTypeRegistry types,
    ITextLocalizer localizer,
    ILocalizationSource localization,
    TimeProvider timeProvider) : IQueryHandler<GetMyApprovalTasksQuery, PagedResult<MyApprovalTaskItem>>
{
    public async Task<Result<PagedResult<MyApprovalTaskItem>>> Handle(GetMyApprovalTasksQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return CommonErrors.Forbidden;
        }

        var rows = await MyTasks.LoadAsync(db, userId, timeProvider, cancellationToken);
        var page = rows
            .OrderBy(r => r.Task.DueAt is null)
            .ThenBy(r => r.Task.DueAt)
            .ThenBy(r => r.Task.CreatedAt)
            .Skip(request.Page.Skip)
            .Take(request.Page.Size)
            .ToList();
        var names = await ApprovalNames.UsersAsync(db, page.SelectMany(r => new Guid?[] { r.Request.RequesterUserId, r.Task.AssigneeUserId }), cancellationToken);
        var defaultCulture = (await localization.GetAsync(cancellationToken)).DefaultCulture;

        var items = page.Select(r =>
        {
            var (task, approval) = (r.Task, r.Request);
            var step = approval.Steps[task.Step];
            Guid? onBehalfOf = task.AssigneeUserId == userId ? null : task.AssigneeUserId;
            return new MyApprovalTaskItem(
                task.Id.Value,
                approval.Id.Value,
                approval.DocumentType,
                ApprovalNames.DocumentType(types, localizer, approval.DocumentType),
                approval.DocumentId,
                ApprovalNames.Title(localizer, approval.TitleKey, approval.TitleArgs),
                approval.Summary,
                approval.Link,
                approval.RequesterUserId,
                names.Name(approval.RequesterUserId)!,
                task.Step,
                ApprovalNames.Text(step.Name, currentUser.Culture, defaultCulture),
                approval.Steps.Count,
                step.AllowReturn,
                onBehalfOf,
                names.Name(onBehalfOf),
                approval.SubmittedAt,
                task.CreatedAt,
                task.DueAt);
        }).ToList();

        return new PagedResult<MyApprovalTaskItem>(items, request.Page.PageNumber, request.Page.Size, rows.Count);
    }
}

internal sealed class GetMyApprovalTaskCountQueryHandler(IPlatformDbContext db, ICurrentUser currentUser, TimeProvider timeProvider)
    : IQueryHandler<GetMyApprovalTaskCountQuery, ApprovalTaskCount>
{
    public async Task<Result<ApprovalTaskCount>> Handle(GetMyApprovalTaskCountQuery request, CancellationToken cancellationToken) =>
        currentUser.UserId is { } userId
            ? new ApprovalTaskCount((await MyTasks.LoadAsync(db, userId, timeProvider, cancellationToken)).Count)
            : CommonErrors.Forbidden;
}

/// <summary>Builds the details of a request the caller may see (or 404), for both the request and the timeline query.</summary>
public sealed class ApprovalRequestReader(
    IPlatformDbContext db,
    ICurrentUser currentUser,
    IAccessScopeProvider scopes,
    IEnumerable<IApprovalDocumentAccessPolicy> policies,
    ApprovalDocumentTypeRegistry types,
    ITextLocalizer localizer,
    ILocalizationSource localization,
    TimeProvider timeProvider)
{
    public async Task<Result<ApprovalRequestDetails>> ReadAsync(ApprovalRequest? approval, CancellationToken cancellationToken)
    {
        if (approval is null || currentUser.UserId is not { } userId)
        {
            return CommonErrors.NotFound;
        }

        var today = PlatformClock.Today(timeProvider);
        var delegations = await ActingFor.PrincipalsAsync(db, userId, timeProvider, cancellationToken);
        bool ActsFor(Guid assignee) =>
            approval.RequesterUserId != userId && delegations.Exists(d => d.PrincipalUserId == assignee && d.Covers(approval.DocumentType, today));

        var myTask = approval.OpenTasks.FirstOrDefault(t => t.AssigneeUserId == userId) ?? approval.OpenTasks.FirstOrDefault(t => ActsFor(t.AssigneeUserId));
        var involved = approval.RequesterUserId == userId
            || myTask is not null
            || approval.Tasks.Any(t => t.AssigneeUserId == userId || t.DecidedByUserId == userId);
        var canAdminister = await TenantAdministration.IsAllowedAsync(scopes, PlatformPermissions.Approvals.Admin, cancellationToken);
        if (!involved && !canAdminister && !await ModuleAllowsAsync(approval, cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        var history = approval.History.OrderBy(h => h.Sequence).ToList();
        var detailUsers = history.Select(h => Guid.TryParse(h.Detail, out var id) ? id : (Guid?)null).ToList();
        var names = await ApprovalNames.UsersAsync(
            db,
            approval.Tasks.SelectMany(t => new[] { t.AssigneeUserId, t.DecidedByUserId, t.OnBehalfOfUserId })
                .Concat(history.SelectMany(h => new[] { h.UserId, h.OnBehalfOfUserId }))
                .Concat(detailUsers)
                .Append(approval.RequesterUserId),
            cancellationToken);
        var defaultCulture = (await localization.GetAsync(cancellationToken)).DefaultCulture;

        return new ApprovalRequestDetails(
            approval.Id.Value,
            approval.DocumentType,
            ApprovalNames.DocumentType(types, localizer, approval.DocumentType),
            approval.DocumentId,
            approval.CompanyId,
            ApprovalNames.Title(localizer, approval.TitleKey, approval.TitleArgs),
            approval.Summary,
            approval.Link,
            approval.RequesterUserId,
            names.Name(approval.RequesterUserId)!,
            approval.Status,
            approval.Round,
            approval.CurrentStep,
            approval.SubmittedAt,
            approval.CompletedAt,
            [.. approval.Steps.Select((s, i) => new ApprovalRequestStepItem(i, ApprovalNames.Text(s.Name, currentUser.Culture, defaultCulture), s.Mode, s.AllowReturn, s.DueHours))],
            [.. approval.Tasks.OrderBy(t => t.CreatedAt).Select(t => new ApprovalTaskItem(
                t.Id.Value,
                t.Round,
                t.Step,
                t.AssigneeUserId,
                names.Name(t.AssigneeUserId)!,
                t.Status,
                t.DecidedByUserId,
                names.Name(t.DecidedByUserId),
                t.OnBehalfOfUserId,
                names.Name(t.OnBehalfOfUserId),
                t.Comment,
                t.CreatedAt,
                t.DueAt,
                t.DecidedAt))],
            [.. history.Select((h, i) => new ApprovalHistoryItem(
                h.Sequence,
                h.Kind,
                h.Round,
                h.Step,
                h.UserId,
                names.Name(h.UserId),
                h.OnBehalfOfUserId,
                names.Name(h.OnBehalfOfUserId),
                h.Detail,
                names.Name(detailUsers[i]),
                h.At))],
            myTask?.Id.Value,
            canAdminister);
    }

    private async Task<bool> ModuleAllowsAsync(ApprovalRequest approval, CancellationToken cancellationToken)
    {
        foreach (var policy in policies.Where(p => p.DocumentTypes.Contains(approval.DocumentType, StringComparer.Ordinal)))
        {
            if (await policy.CanReadAsync(approval.DocumentType, approval.DocumentId, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed class GetApprovalRequestQueryHandler(IPlatformDbContext db, ApprovalRequestReader reader)
    : IQueryHandler<GetApprovalRequestQuery, ApprovalRequestDetails>
{
    public async Task<Result<ApprovalRequestDetails>> Handle(GetApprovalRequestQuery request, CancellationToken cancellationToken) =>
        await reader.ReadAsync(
            await db.ApprovalRequests.AsNoTracking().Include(r => r.Tasks).Include(r => r.History)
                .SingleOrDefaultAsync(r => r.Id == ApprovalRequestId.From(request.Id), cancellationToken),
            cancellationToken);
}

internal sealed class GetApprovalTimelineQueryHandler(IPlatformDbContext db, ApprovalRequestReader reader)
    : IQueryHandler<GetApprovalTimelineQuery, ApprovalRequestDetails>
{
    public async Task<Result<ApprovalRequestDetails>> Handle(GetApprovalTimelineQuery request, CancellationToken cancellationToken) =>
        await reader.ReadAsync(
            await db.ApprovalRequests.AsNoTracking().Include(r => r.Tasks).Include(r => r.History)
                .SingleOrDefaultAsync(r => r.DocumentType == request.DocumentType && r.DocumentId == request.DocumentId, cancellationToken),
            cancellationToken);
}

internal sealed class GetApprovalRequestsQueryHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    ApprovalDocumentTypeRegistry types,
    ITextLocalizer localizer) : IQueryHandler<GetApprovalRequestsQuery, PagedResult<ApprovalRequestListItem>>
{
    public async Task<Result<PagedResult<ApprovalRequestListItem>>> Handle(GetApprovalRequestsQuery request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return ApprovalErrors.TenantScopeRequired;
        }

        var query = db.ApprovalRequests.AsNoTracking();
        if (request.Status is { } status)
        {
            query = query.Where(r => r.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(request.DocumentType))
        {
            query = query.Where(r => r.DocumentType == request.DocumentType);
        }

        var page = await query.OrderByDescending(r => r.SubmittedAt).ThenByDescending(r => r.Id).ToPagedResultAsync(request.Page, cancellationToken);
        var names = await ApprovalNames.UsersAsync(db, page.Items.Select(r => (Guid?)r.RequesterUserId), cancellationToken);
        var items = page.Items.Select(r => new ApprovalRequestListItem(
            r.Id.Value,
            r.DocumentType,
            ApprovalNames.DocumentType(types, localizer, r.DocumentType),
            r.DocumentId,
            ApprovalNames.Title(localizer, r.TitleKey, r.TitleArgs),
            r.Summary,
            names.Name(r.RequesterUserId)!,
            r.Status,
            r.CurrentStep,
            r.SubmittedAt,
            r.CompletedAt)).ToList();
        return new PagedResult<ApprovalRequestListItem>(items, page.Page, page.PageSize, page.TotalCount);
    }
}

internal sealed class GetMyDelegationsQueryHandler(
    IPlatformDbContext db,
    ICurrentUser currentUser,
    ApprovalDocumentTypeRegistry types,
    ITextLocalizer localizer) : IQueryHandler<GetMyDelegationsQuery, IReadOnlyList<DelegationItem>>
{
    public async Task<Result<IReadOnlyList<DelegationItem>>> Handle(GetMyDelegationsQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return CommonErrors.Forbidden;
        }

        var delegations = await db.Delegations.AsNoTracking()
            .Where(d => d.PrincipalUserId == userId || d.DelegateUserId == userId)
            .OrderByDescending(d => d.From)
            .ThenByDescending(d => d.Id)
            .ToListAsync(cancellationToken);
        var names = await ApprovalNames.UsersAsync(db, delegations.SelectMany(d => new Guid?[] { d.PrincipalUserId, d.DelegateUserId }), cancellationToken);

        return delegations.Select(d => new DelegationItem(
            d.Id.Value,
            d.PrincipalUserId,
            names.Name(d.PrincipalUserId)!,
            d.DelegateUserId,
            names.Name(d.DelegateUserId)!,
            d.From,
            d.To,
            d.DocumentTypes,
            [.. d.DocumentTypes.Select(t => ApprovalNames.DocumentType(types, localizer, t))],
            d.IsRevoked,
            Given: d.PrincipalUserId == userId)).ToList();
    }
}

internal sealed class SearchApprovalUsersQueryHandler(IPlatformDbContext db)
    : IQueryHandler<SearchApprovalUsersQuery, IReadOnlyList<ApprovalUserOption>>
{
    private const int Limit = 20;

    public async Task<Result<IReadOnlyList<ApprovalUserOption>>> Handle(SearchApprovalUsersQuery request, CancellationToken cancellationToken)
    {
        var users = db.Users.AsNoTracking().Where(u => u.IsActive);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = TextSearch.ContainsPattern(request.Search);
            users = users.Where(u => TextSearch.Matches(u.UserName, pattern)
                || TextSearch.Matches(u.FirstName, pattern)
                || TextSearch.Matches(u.LastName, pattern));
        }

        var found = await users.OrderBy(u => u.FirstName).ThenBy(u => u.LastName).ThenBy(u => u.UserName).Take(Limit).ToListAsync(cancellationToken);
        return found.Select(u => new ApprovalUserOption(u.Id.Value, u.DisplayName)).ToList();
    }
}

/// <summary><see cref="IApprovalAccess"/>: task-based access of modules to their documents (§1 rule 5).</summary>
public sealed class ApprovalAccess(IPlatformDbContext db, TimeProvider timeProvider) : IApprovalAccess
{
    public async Task<bool> HasTaskAsync(Guid userId, string documentType, string documentId, CancellationToken cancellationToken = default)
    {
        var approval = await db.ApprovalRequests.AsNoTracking().Include(r => r.Tasks)
            .SingleOrDefaultAsync(r => r.DocumentType == documentType && r.DocumentId == documentId, cancellationToken);
        if (approval is null)
        {
            return false;
        }

        if (approval.Tasks.Any(t => t.AssigneeUserId == userId || t.DecidedByUserId == userId))
        {
            return true;
        }

        if (approval.RequesterUserId == userId)
        {
            return false;
        }

        var today = PlatformClock.Today(timeProvider);
        var delegations = await ActingFor.PrincipalsAsync(db, userId, timeProvider, cancellationToken);
        return approval.OpenTasks.Any(t => delegations.Exists(d => d.PrincipalUserId == t.AssigneeUserId && d.Covers(documentType, today)));
    }
}
