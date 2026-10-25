using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Approvals;
using Merp.Application.Abstractions.Auditing;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Notifications;
using Merp.Application.Abstractions.Numbering;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Persistence;
using Merp.Modules.HR.Contracts.Employees;
using Merp.Modules.HR.Contracts.Organization;
using Merp.Modules.Platform.Contracts.Approvals;
using Merp.Modules.Platform.Contracts.Notifications;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.Modules.Platform.Contracts.Users;
using Merp.Modules.Recruitment.Application.Settings;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.SharedKernel.Notifications;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Recruitment.Application.Requisitions;

/// <summary>What the requisition form gives (D48).</summary>
public interface IRequisitionFields
{
    Guid CompanyId { get; }

    /// <summary>The unit of a new position; taken from the position for an existing one.</summary>
    Guid? OrgUnitId { get; }

    /// <summary>An existing position with vacant seats; null proposes a new position.</summary>
    Guid? PositionId { get; }

    string? NewPositionTitle { get; }

    string? NewPositionLevel { get; }

    int Headcount { get; }

    RequisitionReason Reason { get; }

    /// <summary>The person replaced, for a replacement.</summary>
    Guid? ReplacedEmployeeId { get; }

    RequisitionEmploymentType EmploymentType { get; }

    DateOnly? DesiredStartDate { get; }

    decimal? SalaryMin { get; }

    decimal? SalaryMax { get; }

    string? Currency { get; }

    string? Justification { get; }

    Guid HiringManagerUserId { get; }
}

/// <summary>A requisition in a list; PositionTitle is the position's code and job, or the new position's title.</summary>
public sealed record RequisitionListItem(
    Guid Id,
    string Number,
    Guid CompanyId,
    string? CompanyName,
    string? OrgUnitName,
    Guid? PositionId,
    string PositionTitle,
    int Headcount,
    int FilledCount,
    RequisitionReason Reason,
    RequisitionStatus Status,
    DateOnly? DesiredStartDate,
    string? HiringManagerName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt);

/// <summary>A requisition with its details and the caller's actions (D48).</summary>
/// <remarks>
/// The salary range is shown to those who manage requisitions and to its approvers; others get nulls. CanCreateOpening
/// is true once approved with seats left: the opening (R3) is published from it.
/// </remarks>
public sealed record RequisitionDetails(
    RequisitionListItem Requisition,
    Guid OrgUnitId,
    string? NewPositionTitle,
    string? NewPositionLevel,
    Guid? ReplacedEmployeeId,
    string? ReplacedEmployeeName,
    RequisitionEmploymentType EmploymentType,
    decimal? SalaryMin,
    decimal? SalaryMax,
    string? Currency,
    bool SalaryVisible,
    string Justification,
    Guid HiringManagerUserId,
    Guid RequesterUserId,
    string? RequesterName,
    string? ReturnComment,
    string? CancellationReason,
    DateTimeOffset? DecidedAt,
    bool CanEdit,
    bool CanCancel,
    bool CanCreateOpening,
    uint Version);

/// <summary>The requisitions of the companies in the caller's scope, newest first unless sorted.</summary>
public sealed record GetRequisitionsQuery(PageRequest Paging, Guid? CompanyId = null, IReadOnlyList<RequisitionStatus>? Statuses = null, string? Search = null)
    : IQuery<PagedResult<RequisitionListItem>>, IRequirePermission
{
    public const string DefaultSort = "-createdAt";

    internal static readonly SortMap<JobRequisition> Sorts = SortMap<JobRequisition>.By(r => r.Id)
        .Add("createdAt", r => r.CreatedAt)
        .Add("number", r => r.Number)
        .Add("desiredStartDate", r => r.DesiredStartDate);

    public string Permission => RecruitmentPermissions.Requisitions.Read;
}

/// <summary>A requisition of a company in scope, or one the caller approves.</summary>
public sealed record GetRequisitionQuery(Guid Id) : IQuery<RequisitionDetails>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Requisitions.Read;
}

/// <summary>Opens a requisition: a draft, or sent for approval at once with <see cref="Submit"/>. Returns its id.</summary>
public sealed record CreateRequisitionCommand(
    Guid CompanyId,
    Guid? OrgUnitId,
    Guid? PositionId,
    string? NewPositionTitle,
    string? NewPositionLevel,
    int Headcount,
    RequisitionReason Reason,
    Guid? ReplacedEmployeeId,
    RequisitionEmploymentType EmploymentType,
    DateOnly? DesiredStartDate,
    decimal? SalaryMin,
    decimal? SalaryMax,
    string? Currency,
    string? Justification,
    Guid HiringManagerUserId,
    bool Submit = false) : ICommand<Guid>, IRequirePermission, IRequisitionFields
{
    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Requisitions.Manage;
}

/// <summary>Corrects a draft or a returned requisition; with <see cref="Submit"/>, sends it.</summary>
public sealed record UpdateRequisitionCommand(
    uint Version,
    Guid CompanyId,
    Guid? OrgUnitId,
    Guid? PositionId,
    string? NewPositionTitle,
    string? NewPositionLevel,
    int Headcount,
    RequisitionReason Reason,
    Guid? ReplacedEmployeeId,
    RequisitionEmploymentType EmploymentType,
    DateOnly? DesiredStartDate,
    decimal? SalaryMin,
    decimal? SalaryMax,
    string? Currency,
    string? Justification,
    Guid HiringManagerUserId,
    bool Submit = false) : ICommand, IRequirePermission, IRequisitionFields
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Requisitions.Manage;
}

/// <summary>Sends a draft or a returned requisition for approval.</summary>
public sealed record SubmitRequisitionCommand(Guid Id) : ICommand, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Requisitions.Manage;
}

/// <summary>Cancels a requisition not decided yet, or approved and not filled; an open approval is closed.</summary>
public sealed record CancelRequisitionCommand(string? Reason) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Requisitions.Manage;
}

/// <summary>The units of a company, for the requisition form.</summary>
public sealed record GetRequisitionUnitsQuery(Guid CompanyId) : IQuery<IReadOnlyList<OrgUnitInfo>>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Requisitions.Manage;
}

/// <summary>A position with its seats free for a new requisition: vacant less those open requisitions hold.</summary>
public sealed record RequisitionPositionItem(Guid PositionId, string Code, Guid OrgUnitId, string OrgUnitName, string JobName, int Norm, int Filled, int Available);

/// <summary>The active positions of a company (of a unit and below it when given), with the seats a requisition may ask for.</summary>
public sealed record GetRequisitionPositionsQuery(Guid CompanyId, Guid? OrgUnitId = null) : IQuery<IReadOnlyList<RequisitionPositionItem>>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Requisitions.Manage;
}

/// <summary>People of a company by name or registry number, for the person a requisition replaces.</summary>
public sealed record SearchRequisitionEmployeesQuery(Guid CompanyId, string? Search = null) : IQuery<IReadOnlyList<EmployeeSummary>>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Requisitions.Manage;
}

/// <summary>The approval ended (inbox).</summary>
public sealed record CompleteRequisitionCommand(Guid RequisitionId, ApprovalOutcome Outcome, DateTimeOffset DecidedAt) : ICommand;

/// <summary>An approver returned the requisition for correction (inbox).</summary>
public sealed record ReturnRequisitionCommand(Guid RequisitionId, string Comment) : ICommand;

public static class RequisitionErrors
{
    public const string UnknownPositionCode = "recruitment.requisitions.errors.unknown_position";
    public const string NoVacancyCode = "recruitment.requisitions.errors.no_vacancy";
    public const string UnknownUnitCode = "recruitment.requisitions.errors.unknown_unit";
    public const string UnknownEmployeeCode = "recruitment.requisitions.errors.unknown_employee";
    public const string UnknownUserCode = "recruitment.requisitions.errors.unknown_user";
    public const string SalaryRangeCode = "recruitment.requisitions.errors.salary_range";
}

/// <summary>The approvable document type of requisitions (docs/domain/yetki-ve-onay.md §2.8, D48).</summary>
public sealed class RecruitmentApprovalDocumentTypes : IApprovalDocumentTypeProvider
{
    public IEnumerable<ApprovalDocumentType> GetDocumentTypes() =>
    [
        new(
            RecruitmentDocumentTypes.Requisition,
            "recruitment.approvals.document_types.requisition",
            [
                new ApprovalFact("companyId", ApprovalFactType.Identifier, "recruitment.approvals.facts.company"),
                new ApprovalFact("orgUnitId", ApprovalFactType.Identifier, "recruitment.approvals.facts.org_unit"),
                new ApprovalFact("headcount", ApprovalFactType.Number, "recruitment.approvals.facts.headcount"),
                new ApprovalFact("reason", ApprovalFactType.Text, "recruitment.approvals.facts.reason"),
                new ApprovalFact("isNewPosition", ApprovalFactType.Boolean, "recruitment.approvals.facts.is_new_position"),
            ],
            AutoApproveWhenNoDefinition: false,
            DefaultFlow:
            [
                new ApprovalDefaultStep("recruitment.approvals.steps.line_manager", "line-manager", new Dictionary<string, string> { ["level"] = "1" }, AllowReturn: true),
                new ApprovalDefaultStep(
                    "recruitment.approvals.steps.hr_manager",
                    "role",
                    new Dictionary<string, string> { [ApprovalDefaultStep.RoleCodeParameter] = "hr-manager" },
                    AllowReturn: true),
            ]),
    ];
}

/// <summary>Notification types of requisitions for their requesters (D48).</summary>
public sealed class RequisitionNotificationTypes : INotificationTypeProvider
{
    public const string Completed = "recruitment.requisition_completed";
    public const string Returned = "recruitment.requisition_returned";

    public IEnumerable<NotificationTypeDefinition> GetNotificationTypes() =>
    [
        new(Completed, [NotificationChannel.InApp, NotificationChannel.Email]),
        new(Returned, [NotificationChannel.InApp, NotificationChannel.Email]),
    ];
}

/// <summary>Who sees a requisition's approval timeline besides its approvers: the requester and readers of its company.</summary>
public sealed class RequisitionAccessPolicy(IRecruitmentDbContext db, ICurrentUser currentUser, IAccessScopeProvider scopes) : IApprovalDocumentAccessPolicy
{
    public IReadOnlyCollection<string> DocumentTypes { get; } = [RecruitmentDocumentTypes.Requisition];

    public async Task<bool> CanReadAsync(string documentType, string documentId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(documentId, out var id))
        {
            return false;
        }

        var requisition = await db.JobRequisitions.AsNoTracking()
            .Where(r => r.Id == JobRequisitionId.From(id))
            .Select(r => new { r.CompanyId, r.RequesterUserId })
            .SingleOrDefaultAsync(cancellationToken);
        if (requisition is null)
        {
            return false;
        }

        var companies = await RecruitmentSettingRules.CompaniesAsync(scopes, RecruitmentPermissions.Requisitions.Read, cancellationToken);
        return requisition.RequesterUserId == currentUser.UserId
            || (await scopes.GetCurrentUserScopesAsync(RecruitmentPermissions.Requisitions.Read, cancellationToken)).Count > 0
                && (companies is null || companies.Contains(requisition.CompanyId));
    }
}

/// <summary>Checks and builds what a requisition asks for (D48).</summary>
public sealed class RequisitionRules(IRecruitmentDbContext db, IOrgQuery org, IEmployeeLookup employees, IUserDirectory users)
{
    /// <summary>Seats open requisitions of <paramref name="positionId"/> hold, but <paramref name="except"/>.</summary>
    public async Task<int> ReservedAsync(Guid positionId, JobRequisitionId? except, CancellationToken cancellationToken)
    {
        var excluded = except ?? default;
        return await db.JobRequisitions.AsNoTracking()
            .Where(r => r.PositionId == positionId && r.Id != excluded
                && (r.Status == RequisitionStatus.Pending || r.Status == RequisitionStatus.Returned || r.Status == RequisitionStatus.Approved))
            .SumAsync(r => r.Headcount - r.FilledCount, cancellationToken);
    }

    /// <summary>Seats of a position a requisition may ask for: vacant less what other open requisitions hold.</summary>
    public async Task<int> AvailableAsync(Guid positionId, JobRequisitionId? except, CancellationToken cancellationToken) =>
        await org.GetPositionAsync(positionId, cancellationToken) is { } position
            ? position.Vacant - await ReservedAsync(positionId, except, cancellationToken)
            : 0;

    public static Error NoVacancy(int available) =>
        Error.Validation([new FieldError("headcount", RequisitionErrors.NoVacancyCode, new Dictionary<string, object?> { ["available"] = Math.Max(available, 0) })]);

    /// <summary>
    /// The details of the form, checked: the position of the company with enough seats free (vacant less what open
    /// requisitions hold, user decision), or a unit of the company for a new position; the person replaced; the
    /// hiring manager.
    /// </summary>
    public async Task<Result<RequisitionTerms>> CheckAsync(IRequisitionFields fields, JobRequisitionId? except, CancellationToken cancellationToken)
    {
        Guid unitId;
        if (fields.PositionId is { } positionId)
        {
            var position = await org.GetPositionAsync(positionId, cancellationToken);
            if (position is null || position.CompanyId != fields.CompanyId)
            {
                return Field("positionId", RequisitionErrors.UnknownPositionCode);
            }

            var available = position.Vacant - await ReservedAsync(positionId, except, cancellationToken);
            if (fields.Headcount > available)
            {
                return NoVacancy(available);
            }

            unitId = position.OrgUnitId;
        }
        else
        {
            if (fields.OrgUnitId is not { } unit || !(await org.GetUnitsAsync(fields.CompanyId, cancellationToken)).Any(u => u.OrgUnitId == unit))
            {
                return Field("orgUnitId", RequisitionErrors.UnknownUnitCode);
            }

            unitId = unit;
        }

        if (fields.Reason == RequisitionReason.Replacement
            && (fields.ReplacedEmployeeId is not { } replaced || await employees.FindAsync(replaced, fields.CompanyId, cancellationToken) is null))
        {
            return Field("replacedEmployeeId", RequisitionErrors.UnknownEmployeeCode);
        }

        if ((await users.GetAsync([fields.HiringManagerUserId], cancellationToken)).SingleOrDefault() is not { IsActive: true })
        {
            return Field("hiringManagerUserId", RequisitionErrors.UnknownUserCode);
        }

        return new RequisitionTerms(
            fields.CompanyId,
            unitId,
            fields.PositionId,
            fields.NewPositionTitle,
            fields.NewPositionLevel,
            fields.Headcount,
            fields.Reason,
            fields.ReplacedEmployeeId,
            fields.EmploymentType,
            fields.DesiredStartDate,
            fields.SalaryMin,
            fields.SalaryMax,
            fields.Currency,
            fields.Justification!,
            fields.HiringManagerUserId);
    }

    private static Error Field(string field, string code) => Error.Validation([new FieldError(field, code)]);

    public static void AddFieldRules<T>(AbstractValidator<T> validator)
        where T : IRequisitionFields
    {
        validator.RuleFor(r => r.CompanyId).NotEmpty();
        validator.RuleFor(r => r.Headcount).InclusiveBetween(1, JobRequisition.MaxHeadcount);
        validator.RuleFor(r => r.Reason).IsInEnum();
        validator.RuleFor(r => r.EmploymentType).IsInEnum();
        validator.RuleFor(r => r.NewPositionTitle).NotEmpty().MaximumLength(JobRequisition.TitleMaxLength).When(r => r.PositionId is null);
        validator.RuleFor(r => r.NewPositionLevel).MaximumLength(JobRequisition.LevelMaxLength);
        validator.RuleFor(r => r.OrgUnitId).NotEmpty().When(r => r.PositionId is null);
        validator.RuleFor(r => r.ReplacedEmployeeId).NotEmpty().When(r => r.Reason == RequisitionReason.Replacement);
        validator.RuleFor(r => r.SalaryMin).GreaterThanOrEqualTo(0).When(r => r.SalaryMin is not null);
        validator.RuleFor(r => r.SalaryMax).GreaterThanOrEqualTo(r => r.SalaryMin ?? 0).WithErrorCode(RequisitionErrors.SalaryRangeCode).When(r => r.SalaryMax is not null);
        validator.RuleFor(r => r.Currency).NotEmpty().Length(JobRequisition.CurrencyLength).When(r => r.SalaryMin is not null || r.SalaryMax is not null);
        validator.RuleFor(r => r.Justification).NotEmpty().MaximumLength(JobRequisition.JustificationMaxLength);
        validator.RuleFor(r => r.HiringManagerUserId).NotEmpty();
    }
}

/// <summary>Sends requisitions for approval and builds their list rows and details.</summary>
public sealed class RequisitionService(
    IOrgQuery org,
    IOrganizationLookup organization,
    IUserDirectory users,
    IIntegrationEventPublisher publisher,
    TimeProvider timeProvider)
{
    public async Task<Result> SendAsync(JobRequisition requisition, CancellationToken cancellationToken)
    {
        var sent = requisition.Submit(timeProvider.GetUtcNow());
        if (sent.IsFailure)
        {
            return sent;
        }

        var requester = await org.GetUserOrgInfoAsync(requisition.RequesterUserId, cancellationToken);
        var title = (await RowsAsync([requisition], cancellationToken)).Single().PositionTitle;
        await publisher.PublishAsync(
            new ApprovalRequested(
                RecruitmentDocumentTypes.Requisition,
                requisition.Id.Value.ToString(),
                requisition.RequesterUserId,
                requester?.EmployeeId,
                JsonSerializer.SerializeToElement(new
                {
                    companyId = requisition.CompanyId,
                    orgUnitId = requisition.OrgUnitId,
                    headcount = requisition.Headcount,
                    reason = requisition.Reason.ToString(),
                    isNewPosition = requisition.IsNewPosition,
                }),
                "recruitment.requisitions.approval_title",
                new Dictionary<string, string> { ["number"] = requisition.Number, ["position"] = title },
                string.Create(CultureInfo.InvariantCulture, $"{requisition.Headcount} × {title}"),
                $"/recruitment/requisitions/{requisition.Id.Value}")
            {
                CompanyId = requisition.CompanyId,
            },
            cancellationToken);
        return Result.Success();
    }

    public async Task<List<RequisitionListItem>> RowsAsync(IReadOnlyList<JobRequisition> requisitions, CancellationToken cancellationToken)
    {
        var companies = (await organization.GetCompaniesAsync([.. requisitions.Select(r => r.CompanyId).Distinct()], cancellationToken))
            .ToDictionary(c => c.Id, c => c.ShortName);
        var units = new Dictionary<Guid, string>();
        var positions = new Dictionary<Guid, OrgPositionInfo>();
        foreach (var companyId in requisitions.Select(r => r.CompanyId).Distinct())
        {
            foreach (var unit in await org.GetUnitsAsync(companyId, cancellationToken))
            {
                units[unit.OrgUnitId] = unit.Name;
            }
        }

        foreach (var positionId in requisitions.Where(r => r.PositionId is not null).Select(r => r.PositionId!.Value).Distinct())
        {
            if (await org.GetPositionAsync(positionId, cancellationToken) is { } position)
            {
                positions[positionId] = position;
            }
        }

        var managers = (await users.GetAsync([.. requisitions.Select(r => r.HiringManagerUserId).Distinct()], cancellationToken))
            .ToDictionary(u => u.Id, u => u.DisplayName);
        return
        [
            .. requisitions.Select(r => new RequisitionListItem(
                r.Id.Value,
                r.Number,
                r.CompanyId,
                companies.GetValueOrDefault(r.CompanyId),
                units.GetValueOrDefault(r.OrgUnitId),
                r.PositionId,
                r.PositionId is { } id && positions.TryGetValue(id, out var position)
                    ? $"{position.Code} · {position.JobName}"
                    : r.NewPositionTitle ?? string.Empty,
                r.Headcount,
                r.FilledCount,
                r.Reason,
                r.Status,
                r.DesiredStartDate,
                managers.GetValueOrDefault(r.HiringManagerUserId),
                r.CreatedAt,
                r.SubmittedAt)),
        ];
    }
}

internal sealed class CreateRequisitionCommandValidator : AbstractValidator<CreateRequisitionCommand>
{
    public CreateRequisitionCommandValidator() => RequisitionRules.AddFieldRules(this);
}

internal sealed class UpdateRequisitionCommandValidator : AbstractValidator<UpdateRequisitionCommand>
{
    public UpdateRequisitionCommandValidator() => RequisitionRules.AddFieldRules(this);
}

internal sealed class GetRequisitionsQueryValidator : AbstractValidator<GetRequisitionsQuery>
{
    public GetRequisitionsQueryValidator()
    {
        RuleFor(q => q.Paging.Sort).Must(GetRequisitionsQuery.Sorts.IsValid).WithErrorCode(CommonErrors.InvalidSort.Code).OverridePropertyName("sort");
        RuleForEach(q => q.Statuses).IsInEnum().OverridePropertyName("status");
    }
}

/// <summary>Whether the caller reaches a company with a permission (Tenant or Company scope).</summary>
internal static class RequisitionScope
{
    public static async Task<bool> CoversAsync(IAccessScopeProvider scopes, string permission, Guid companyId, CancellationToken cancellationToken)
    {
        var list = await scopes.GetCurrentUserScopesAsync(permission, cancellationToken);
        return list.Any(s => s.Type == AccessScopeTypes.Tenant || (s.Type == AccessScopeTypes.Company && s.ScopeId == companyId));
    }
}

internal sealed class CreateRequisitionCommandHandler(
    IRecruitmentDbContext db,
    IAccessScopeProvider scopes,
    ICurrentUser currentUser,
    INumberGenerator numbers,
    RequisitionRules rules,
    RequisitionService service,
    IOrganizationLookup organization,
    TimeProvider timeProvider) : ICommandHandler<CreateRequisitionCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateRequisitionCommand request, CancellationToken cancellationToken)
    {
        if (!await RequisitionScope.CoversAsync(scopes, request.Permission, request.CompanyId, cancellationToken))
        {
            return CommonErrors.Forbidden;
        }

        if (currentUser.UserId is not { } userId)
        {
            return CommonErrors.Forbidden;
        }

        var details = await rules.CheckAsync(request, null, cancellationToken);
        if (details.IsFailure)
        {
            return details.Error;
        }

        var zone = (await organization.GetCompaniesAsync([request.CompanyId], cancellationToken)).SingleOrDefault()?.TimeZoneId;
        var number = await numbers.NextAsync(request.CompanyId, RecruitmentDocumentTypes.Requisition, RecruitmentClock.Today(timeProvider, zone), cancellationToken);
        var requisition = JobRequisition.Draft(number, userId, details.Value);
        db.JobRequisitions.Add(requisition);
        if (request.Submit)
        {
            var sent = await service.SendAsync(requisition, cancellationToken);
            if (sent.IsFailure)
            {
                return sent.Error;
            }
        }

        return requisition.Id.Value;
    }
}

internal sealed class UpdateRequisitionCommandHandler(
    IRecruitmentDbContext db,
    IAccessScopeProvider scopes,
    RequisitionRules rules,
    RequisitionService service) : ICommandHandler<UpdateRequisitionCommand>
{
    public async Task<Result> Handle(UpdateRequisitionCommand request, CancellationToken cancellationToken)
    {
        var requisition = await db.JobRequisitions.SingleOrDefaultAsync(r => r.Id == JobRequisitionId.From(request.Id), cancellationToken);
        if (requisition is null || !await RequisitionScope.CoversAsync(scopes, request.Permission, requisition.CompanyId, cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        if (request.CompanyId != requisition.CompanyId)
        {
            return Error.Validation([new FieldError("companyId", CommonErrors.ValidationCode)]);
        }

        db.Entry(requisition).ExpectVersion(request.Version);
        var details = await rules.CheckAsync(request, requisition.Id, cancellationToken);
        if (details.IsFailure)
        {
            return details.Error;
        }

        var updated = requisition.Update(details.Value);
        return updated.IsFailure || !request.Submit ? updated : await service.SendAsync(requisition, cancellationToken);
    }
}

internal sealed class SubmitRequisitionCommandHandler(
    IRecruitmentDbContext db,
    IAccessScopeProvider scopes,
    RequisitionRules rules,
    RequisitionService service) : ICommandHandler<SubmitRequisitionCommand>
{
    public async Task<Result> Handle(SubmitRequisitionCommand request, CancellationToken cancellationToken)
    {
        var requisition = await db.JobRequisitions.SingleOrDefaultAsync(r => r.Id == JobRequisitionId.From(request.Id), cancellationToken);
        if (requisition is null || !await RequisitionScope.CoversAsync(scopes, request.Permission, requisition.CompanyId, cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        // The seats are checked again: other requisitions may have taken them since the draft was saved
        if (requisition.PositionId is { } positionId)
        {
            var available = await rules.AvailableAsync(positionId, requisition.Id, cancellationToken);
            if (requisition.Headcount > available)
            {
                return RequisitionRules.NoVacancy(available);
            }
        }

        return await service.SendAsync(requisition, cancellationToken);
    }
}

internal sealed class CancelRequisitionCommandHandler(
    IRecruitmentDbContext db,
    IAccessScopeProvider scopes,
    IIntegrationEventPublisher publisher,
    TimeProvider timeProvider) : ICommandHandler<CancelRequisitionCommand>
{
    public async Task<Result> Handle(CancelRequisitionCommand request, CancellationToken cancellationToken)
    {
        var requisition = await db.JobRequisitions.SingleOrDefaultAsync(r => r.Id == JobRequisitionId.From(request.Id), cancellationToken);
        if (requisition is null || !await RequisitionScope.CoversAsync(scopes, request.Permission, requisition.CompanyId, cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        var cancelled = requisition.Cancel(request.Reason, timeProvider.GetUtcNow());
        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        if (cancelled.Value)
        {
            await publisher.PublishAsync(new ApprovalCancellationRequested(RecruitmentDocumentTypes.Requisition, requisition.Id.Value.ToString()), cancellationToken);
        }

        return Result.Success();
    }
}

internal sealed class GetRequisitionsQueryHandler(IRecruitmentDbContext db, IAccessScopeProvider scopes, RequisitionService service)
    : IQueryHandler<GetRequisitionsQuery, PagedResult<RequisitionListItem>>
{
    public async Task<Result<PagedResult<RequisitionListItem>>> Handle(GetRequisitionsQuery request, CancellationToken cancellationToken)
    {
        var companies = await RecruitmentSettingRules.CompaniesAsync(scopes, request.Permission, cancellationToken);
        var requisitions = db.JobRequisitions.AsNoTracking();
        if (companies is not null)
        {
            var ids = companies.ToList();
            requisitions = requisitions.Where(r => ids.Contains(r.CompanyId));
        }

        if (request.CompanyId is { } companyId)
        {
            requisitions = requisitions.Where(r => r.CompanyId == companyId);
        }

        if (request.Statuses is { Count: > 0 } statuses)
        {
            requisitions = requisitions.Where(r => statuses.Contains(r.Status));
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = TextSearch.ContainsPattern(request.Search);
            requisitions = requisitions.Where(r => TextSearch.Matches(r.Number, pattern) || TextSearch.Matches(r.NewPositionTitle, pattern));
        }

        var page = await GetRequisitionsQuery.Sorts.Apply(requisitions, request.Paging.Sort, GetRequisitionsQuery.DefaultSort)
            .ToPagedResultAsync(request.Paging, cancellationToken);
        return new PagedResult<RequisitionListItem>(await service.RowsAsync(page.Items, cancellationToken), page.Page, page.PageSize, page.TotalCount);
    }
}

internal sealed class GetRequisitionQueryHandler(
    IRecruitmentDbContext db,
    IAccessScopeProvider scopes,
    ICurrentUser currentUser,
    IApprovalAccess approvals,
    IEmployeeLookup employees,
    IUserDirectory users,
    ISensitiveReadLog readLog,
    RequisitionService service) : IQueryHandler<GetRequisitionQuery, RequisitionDetails>
{
    public async Task<Result<RequisitionDetails>> Handle(GetRequisitionQuery request, CancellationToken cancellationToken)
    {
        var found = await db.JobRequisitions.AsNoTracking()
            .Where(r => r.Id == JobRequisitionId.From(request.Id))
            .Select(r => new { Requisition = r, Version = EF.Property<uint>(r, ConcurrencyVersion.PropertyName) })
            .SingleOrDefaultAsync(cancellationToken);
        if (found is null)
        {
            return CommonErrors.NotFound;
        }

        var requisition = found.Requisition;
        var reads = await RequisitionScope.CoversAsync(scopes, request.Permission, requisition.CompanyId, cancellationToken);
        var approves = currentUser.UserId is { } userId
            && await approvals.HasTaskAsync(userId, RecruitmentDocumentTypes.Requisition, requisition.Id.Value.ToString(), cancellationToken);
        if (!reads && !approves)
        {
            return CommonErrors.NotFound;
        }

        var manages = await RequisitionScope.CoversAsync(scopes, RecruitmentPermissions.Requisitions.Manage, requisition.CompanyId, cancellationToken);
        var salaryVisible = manages || approves;
        if (salaryVisible && (requisition.SalaryMin is not null || requisition.SalaryMax is not null))
        {
            await readLog.RecordAsync(typeof(JobRequisition), requisition.Id.Value.ToString(), [nameof(JobRequisition.SalaryMin), nameof(JobRequisition.SalaryMax)], cancellationToken);
        }

        var replaced = requisition.ReplacedEmployeeId is { } replacedId ? await employees.FindAsync(replacedId, requisition.CompanyId, cancellationToken) : null;
        var requester = (await users.GetAsync([requisition.RequesterUserId], cancellationToken)).SingleOrDefault();
        return new RequisitionDetails(
            (await service.RowsAsync([requisition], cancellationToken)).Single(),
            requisition.OrgUnitId,
            requisition.NewPositionTitle,
            requisition.NewPositionLevel,
            requisition.ReplacedEmployeeId,
            replaced?.FullName,
            requisition.EmploymentType,
            salaryVisible ? requisition.SalaryMin : null,
            salaryVisible ? requisition.SalaryMax : null,
            salaryVisible ? requisition.Currency : null,
            salaryVisible,
            requisition.Justification,
            requisition.HiringManagerUserId,
            requisition.RequesterUserId,
            requester?.DisplayName,
            requisition.ReturnComment,
            requisition.CancellationReason,
            requisition.DecidedAt,
            manages && requisition.Status is RequisitionStatus.Draft or RequisitionStatus.Returned,
            manages && requisition.Status is RequisitionStatus.Draft or RequisitionStatus.Pending or RequisitionStatus.Returned or RequisitionStatus.Approved,
            manages && requisition.Status == RequisitionStatus.Approved && requisition.FilledCount < requisition.Headcount,
            found.Version);
    }
}

internal sealed class GetRequisitionUnitsQueryHandler(IAccessScopeProvider scopes, IOrgQuery org) : IQueryHandler<GetRequisitionUnitsQuery, IReadOnlyList<OrgUnitInfo>>
{
    public async Task<Result<IReadOnlyList<OrgUnitInfo>>> Handle(GetRequisitionUnitsQuery request, CancellationToken cancellationToken) =>
        await RequisitionScope.CoversAsync(scopes, request.Permission, request.CompanyId, cancellationToken)
            ? Result.Success(await org.GetUnitsAsync(request.CompanyId, cancellationToken))
            : CommonErrors.Forbidden;
}

internal sealed class GetRequisitionPositionsQueryHandler(IAccessScopeProvider scopes, IOrgQuery org, RequisitionRules rules)
    : IQueryHandler<GetRequisitionPositionsQuery, IReadOnlyList<RequisitionPositionItem>>
{
    public async Task<Result<IReadOnlyList<RequisitionPositionItem>>> Handle(GetRequisitionPositionsQuery request, CancellationToken cancellationToken)
    {
        if (!await RequisitionScope.CoversAsync(scopes, request.Permission, request.CompanyId, cancellationToken))
        {
            return CommonErrors.Forbidden;
        }

        var items = new List<RequisitionPositionItem>();
        foreach (var position in (await org.GetPositionsAsync(request.CompanyId, request.OrgUnitId, cancellationToken)).Where(p => p.IsActive))
        {
            var available = Math.Max(position.Vacant - await rules.ReservedAsync(position.PositionId, null, cancellationToken), 0);
            items.Add(new RequisitionPositionItem(position.PositionId, position.Code, position.OrgUnitId, position.OrgUnitName, position.JobName, position.Norm, position.Filled, available));
        }

        return items;
    }
}

internal sealed class SearchRequisitionEmployeesQueryHandler(IAccessScopeProvider scopes, IEmployeeLookup employees)
    : IQueryHandler<SearchRequisitionEmployeesQuery, IReadOnlyList<EmployeeSummary>>
{
    private const int MaxResults = 20;

    public async Task<Result<IReadOnlyList<EmployeeSummary>>> Handle(SearchRequisitionEmployeesQuery request, CancellationToken cancellationToken) =>
        await RequisitionScope.CoversAsync(scopes, request.Permission, request.CompanyId, cancellationToken)
            ? Result.Success(await employees.SearchAsync(request.CompanyId, request.Search, MaxResults, cancellationToken))
            : CommonErrors.Forbidden;
}

internal sealed class CompleteRequisitionCommandHandler(IRecruitmentDbContext db, IIntegrationEventPublisher publisher, ITextLocalizer localizer) : ICommandHandler<CompleteRequisitionCommand>
{
    public async Task<Result> Handle(CompleteRequisitionCommand request, CancellationToken cancellationToken)
    {
        var requisition = await db.JobRequisitions.SingleOrDefaultAsync(r => r.Id == JobRequisitionId.From(request.RequisitionId), cancellationToken);
        if (requisition is not { Status: RequisitionStatus.Pending })
        {
            return Result.Success();
        }

        var approved = request.Outcome == ApprovalOutcome.Approved;
        requisition.Complete(approved, request.DecidedAt);
        await publisher.PublishAsync(
            new NotificationRequested(
                RequisitionNotificationTypes.Completed,
                [requisition.RequesterUserId],
                new Dictionary<string, string> { ["number"] = requisition.Number, ["result"] = localizer.Get($"recruitment.requisitions.status.{requisition.Status}", culture: "tr-TR") },
                $"/recruitment/requisitions/{requisition.Id.Value}")
            {
                CompanyId = requisition.CompanyId,
            },
            cancellationToken);
        return Result.Success();
    }
}

internal sealed class ReturnRequisitionCommandHandler(IRecruitmentDbContext db, IIntegrationEventPublisher publisher) : ICommandHandler<ReturnRequisitionCommand>
{
    public async Task<Result> Handle(ReturnRequisitionCommand request, CancellationToken cancellationToken)
    {
        var requisition = await db.JobRequisitions.SingleOrDefaultAsync(r => r.Id == JobRequisitionId.From(request.RequisitionId), cancellationToken);
        if (requisition is not { Status: RequisitionStatus.Pending })
        {
            return Result.Success();
        }

        requisition.Return(request.Comment);
        await publisher.PublishAsync(
            new NotificationRequested(
                RequisitionNotificationTypes.Returned,
                [requisition.RequesterUserId],
                new Dictionary<string, string> { ["number"] = requisition.Number, ["comment"] = requisition.ReturnComment ?? string.Empty },
                $"/recruitment/requisitions/{requisition.Id.Value}")
            {
                CompanyId = requisition.CompanyId,
            },
            cancellationToken);
        return Result.Success();
    }
}

/// <summary>Inbox: the approval of a requisition ended.</summary>
internal sealed class RequisitionApprovalCompletedHandler(ISender sender) : IIntegrationEventHandler<ApprovalRequestCompleted>
{
    public async Task Handle(ApprovalRequestCompleted integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.DocumentType == RecruitmentDocumentTypes.Requisition && Guid.TryParse(integrationEvent.DocumentId, out var id))
        {
            await RequisitionInbox.SendAsync(sender, new CompleteRequisitionCommand(id, integrationEvent.Outcome, integrationEvent.DecidedAt), cancellationToken);
        }
    }
}

/// <summary>Inbox: an approver returned a requisition.</summary>
internal sealed class RequisitionApprovalReturnedHandler(ISender sender) : IIntegrationEventHandler<ApprovalRequestReturned>
{
    public async Task Handle(ApprovalRequestReturned integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.DocumentType == RecruitmentDocumentTypes.Requisition && Guid.TryParse(integrationEvent.DocumentId, out var id))
        {
            await RequisitionInbox.SendAsync(sender, new ReturnRequisitionCommand(id, integrationEvent.Comment), cancellationToken);
        }
    }
}

/// <summary>A user who may be the hiring manager of a requisition.</summary>
public sealed record RequisitionUserOption(Guid Id, string DisplayName, string UserName);

/// <summary>Active users by name, for the hiring manager of a requisition (at most 20).</summary>
public sealed record SearchRequisitionUsersQuery(string? Search = null) : IQuery<IReadOnlyList<RequisitionUserOption>>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Requisitions.Manage;
}

internal sealed class SearchRequisitionUsersQueryHandler(IUserDirectory users) : IQueryHandler<SearchRequisitionUsersQuery, IReadOnlyList<RequisitionUserOption>>
{
    private const int MaxResults = 20;

    public async Task<Result<IReadOnlyList<RequisitionUserOption>>> Handle(SearchRequisitionUsersQuery request, CancellationToken cancellationToken) =>
        (await users.SearchAsync(request.Search, MaxResults, cancellationToken))
            .Where(u => u.IsActive)
            .Select(u => new RequisitionUserOption(u.Id, u.DisplayName, u.UserName))
            .ToList();
}

internal static class RequisitionInbox
{
    public static async Task SendAsync(ISender sender, ICommand command, CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{command.GetType().Name} failed: {result.Error.Code}");
        }
    }
}
