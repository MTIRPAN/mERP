using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Files;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Numbering;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Persistence;
using Merp.Application.Persistence.Files;
using Merp.Modules.HR.Application.Documents;
using Merp.Modules.HR.Application.Employees;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Application.SelfService;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Leave;
using Merp.Modules.HR.Domain.Organization;
using Merp.Modules.Platform.Contracts.Approvals;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Leave;

/// <summary>
/// The leave requests of the employees in the caller's scope (D44); drafts stay with their requesters. Statuses,
/// type, person, company and a date range (requests that touch it) filter; Search matches the number or the name.
/// </summary>
public sealed record GetLeaveRequestsQuery(
    PageRequest Paging,
    Guid? CompanyId = null,
    Guid? EmployeeId = null,
    IReadOnlyList<LeaveRequestStatus>? Statuses = null,
    string? LeaveTypeCode = null,
    DateOnly? From = null,
    DateOnly? To = null,
    string? Search = null) : IQuery<PagedResult<LeaveRequestListItem>>, IRequirePermission
{
    public const string DefaultSort = "-startDate";

    internal static readonly SortMap<LeaveRequest> Sorts = SortMap<LeaveRequest>.By(r => r.Id)
        .Add("startDate", r => r.StartDate)
        .Add("createdAt", r => r.CreatedAt)
        .Add("submittedAt", r => r.SubmittedAt)
        .Add("number", r => r.Number);

    public string Permission => HrPermissions.Leave.Requests.Read;
}

/// <summary>A leave request of an employee in the caller's scope, with the caller's actions.</summary>
public sealed record GetLeaveRequestQuery(Guid Id) : IQuery<LeaveRequestDetails>, IRequirePermission
{
    public string Permission => HrPermissions.Leave.Requests.Read;
}

/// <summary>HR asks for leave on an employee's behalf; it is sent at once (D44). Returns the request's id.</summary>
public sealed record CreateLeaveRequestOnBehalfCommand(
    Guid EmploymentId,
    Guid? LeaveTypeId,
    DateOnly StartDate,
    DateOnly? EndDate = null,
    LeaveDayPart StartPart = LeaveDayPart.Full,
    LeaveDayPart EndPart = LeaveDayPart.Full,
    TimeOnly? StartTime = null,
    TimeOnly? EndTime = null,
    DateOnly? EventDate = null,
    int TravelDays = 0,
    string? Note = null,
    string? Address = null,
    string? Phone = null,
    Guid? SubstituteEmployeeId = null) : ICommand<Guid>, IRequirePermission, ILeaveRequestFields
{
    [JsonIgnore]
    public string Permission => HrPermissions.Leave.Requests.Manage;
}

/// <summary>HR records a leave approved, without a flow, e.g. one taken in the past (D44). Returns the request's id.</summary>
public sealed record RecordLeaveCommand(
    Guid EmploymentId,
    Guid? LeaveTypeId,
    DateOnly StartDate,
    DateOnly? EndDate = null,
    LeaveDayPart StartPart = LeaveDayPart.Full,
    LeaveDayPart EndPart = LeaveDayPart.Full,
    TimeOnly? StartTime = null,
    TimeOnly? EndTime = null,
    DateOnly? EventDate = null,
    int TravelDays = 0,
    string? Note = null,
    string? Address = null,
    string? Phone = null,
    Guid? SubstituteEmployeeId = null) : ICommand<Guid>, IRequirePermission, ILeaveRequestFields
{
    [JsonIgnore]
    public string Permission => HrPermissions.Leave.Requests.Record;
}

/// <summary>A request HR would send on an employee's behalf, checked without saving it.</summary>
public sealed record PreviewLeaveRequestQuery(
    Guid EmploymentId,
    Guid? LeaveTypeId,
    DateOnly StartDate,
    DateOnly? EndDate = null,
    LeaveDayPart StartPart = LeaveDayPart.Full,
    LeaveDayPart EndPart = LeaveDayPart.Full,
    TimeOnly? StartTime = null,
    TimeOnly? EndTime = null,
    DateOnly? EventDate = null,
    int TravelDays = 0,
    string? Note = null,
    string? Address = null,
    string? Phone = null,
    Guid? SubstituteEmployeeId = null) : IQuery<LeaveRequestPreview>, IRequirePermission, ILeaveRequestFields
{
    [JsonIgnore]
    public string Permission => HrPermissions.Leave.Requests.Manage;
}

/// <summary>A leave HR would record, checked without saving it.</summary>
public sealed record PreviewLeaveRecordQuery(
    Guid EmploymentId,
    Guid? LeaveTypeId,
    DateOnly StartDate,
    DateOnly? EndDate = null,
    LeaveDayPart StartPart = LeaveDayPart.Full,
    LeaveDayPart EndPart = LeaveDayPart.Full,
    TimeOnly? StartTime = null,
    TimeOnly? EndTime = null,
    DateOnly? EventDate = null,
    int TravelDays = 0,
    string? Note = null,
    string? Address = null,
    string? Phone = null,
    Guid? SubstituteEmployeeId = null) : IQuery<LeaveRequestPreview>, IRequirePermission, ILeaveRequestFields
{
    [JsonIgnore]
    public string Permission => HrPermissions.Leave.Requests.Record;
}

/// <summary>
/// HR cancels a request: one not decided yet (its approval is closed), or an approved one that has not started (its
/// days come back); the reason is required for an approved one.
/// </summary>
public sealed record CancelLeaveRequestCommand(string? Reason) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => HrPermissions.Leave.Requests.Manage;
}

/// <summary>HR ends a started leave early: the new last day, the days no longer taken come back (D44).</summary>
public sealed record ReturnFromLeaveEarlyCommand(DateOnly EndDate, LeaveDayPart EndPart = LeaveDayPart.Full, TimeOnly? EndTime = null) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => HrPermissions.Leave.Requests.Manage;
}

/// <summary>
/// A bulk leave checked for each person (toplu izin, D44): the employments named, or those of a unit (with its sub
/// units) in the company on the first day, in the caller's scope.
/// </summary>
public sealed record PreviewBulkLeaveQuery(
    Guid CompanyId,
    IReadOnlyList<Guid>? EmploymentIds,
    Guid? OrgUnitId,
    bool IncludeSubUnits,
    Guid? LeaveTypeId,
    DateOnly StartDate,
    DateOnly? EndDate = null,
    LeaveDayPart StartPart = LeaveDayPart.Full,
    LeaveDayPart EndPart = LeaveDayPart.Full,
    string? Note = null) : IQuery<IReadOnlyList<BulkLeaveRow>>, IRequirePermission
{
    public const int MaxPeople = 500;

    [JsonIgnore]
    public string Permission => HrPermissions.Leave.Bulk;
}

/// <summary>One person of a bulk leave: the leave counted, the balance after it, or what stops it.</summary>
/// <remarks>RequestId is the recorded leave, once created.</remarks>
public sealed record BulkLeaveRow(
    Guid EmployeeId,
    Guid EmploymentId,
    string EmployeeName,
    string RegistryNumber,
    decimal? Quantity,
    decimal? Days,
    decimal? AvailableAfter,
    LeaveRequestProblem? Problem,
    IReadOnlyList<LeaveRequestWarning> Warnings,
    Guid? RequestId = null);

/// <summary>Records the leave of one person of a bulk leave, approved (D44); each person is a command of its own.</summary>
public sealed record CreateBulkLeaveCommand(
    Guid EmploymentId,
    Guid? LeaveTypeId,
    DateOnly StartDate,
    DateOnly? EndDate,
    LeaveDayPart StartPart,
    LeaveDayPart EndPart,
    string? Note) : ICommand<Guid>, IRequirePermission
{
    public string Permission => HrPermissions.Leave.Bulk;
}

/// <summary>HR attaches a file to a request that was not refused or cancelled; returns the file's id.</summary>
public sealed record UploadLeaveRequestFileCommand(Guid RequestId, FileUpload? File) : ICommand<Guid>, IRequirePermission
{
    public string Permission => HrPermissions.Leave.Requests.Manage;
}

/// <summary>HR deletes a file of a request.</summary>
public sealed record DeleteLeaveRequestFileCommand(Guid RequestId, Guid FileId) : ICommand, IRequirePermission
{
    public string Permission => HrPermissions.Leave.Requests.Manage;
}

/// <summary>A file of a request in the caller's scope, for download.</summary>
public sealed record GetLeaveRequestFileQuery(Guid RequestId, Guid FileId) : IQuery<HrFileDownload>, IRequirePermission
{
    public string Permission => HrPermissions.Leave.Requests.Read;
}

internal sealed class GetLeaveRequestsQueryValidator : AbstractValidator<GetLeaveRequestsQuery>
{
    public GetLeaveRequestsQueryValidator()
    {
        RuleFor(q => q.Paging.Sort).Must(GetLeaveRequestsQuery.Sorts.IsValid).WithErrorCode(CommonErrors.InvalidSort.Code).OverridePropertyName("sort");
        RuleForEach(q => q.Statuses).IsInEnum().OverridePropertyName("status");
        RuleFor(q => q.To).GreaterThanOrEqualTo(q => q.From).WithErrorCode(CommonErrors.InvalidDateRange.Code).When(q => q.From is not null && q.To is not null);
    }
}

internal sealed class CreateLeaveRequestOnBehalfCommandValidator : AbstractValidator<CreateLeaveRequestOnBehalfCommand>
{
    public CreateLeaveRequestOnBehalfCommandValidator()
    {
        RuleFor(c => c.EmploymentId).NotEmpty();
        LeaveRequestFieldRules.Add(this);
    }
}

internal sealed class RecordLeaveCommandValidator : AbstractValidator<RecordLeaveCommand>
{
    public RecordLeaveCommandValidator()
    {
        RuleFor(c => c.EmploymentId).NotEmpty();
        LeaveRequestFieldRules.Add(this);
    }
}

internal sealed class PreviewLeaveRequestQueryValidator : AbstractValidator<PreviewLeaveRequestQuery>
{
    public PreviewLeaveRequestQueryValidator()
    {
        RuleFor(c => c.EmploymentId).NotEmpty();
        LeaveRequestFieldRules.Add(this);
    }
}

internal sealed class PreviewLeaveRecordQueryValidator : AbstractValidator<PreviewLeaveRecordQuery>
{
    public PreviewLeaveRecordQueryValidator()
    {
        RuleFor(c => c.EmploymentId).NotEmpty();
        LeaveRequestFieldRules.Add(this);
    }
}

internal sealed class CancelLeaveRequestCommandValidator : AbstractValidator<CancelLeaveRequestCommand>
{
    public CancelLeaveRequestCommandValidator() => RuleFor(c => c.Reason).MaximumLength(LeaveRequest.CommentMaxLength);
}

internal sealed class ReturnFromLeaveEarlyCommandValidator : AbstractValidator<ReturnFromLeaveEarlyCommand>
{
    public ReturnFromLeaveEarlyCommandValidator()
    {
        RuleFor(c => c.EndDate).NotEmpty();
        RuleFor(c => c.EndPart).Must(p => p is LeaveDayPart.Full or LeaveDayPart.Morning).WithErrorCode(CommonErrors.ValidationCode);
    }
}

internal sealed class PreviewBulkLeaveQueryValidator : AbstractValidator<PreviewBulkLeaveQuery>
{
    public PreviewBulkLeaveQueryValidator()
    {
        RuleFor(q => q.CompanyId).NotEmpty();
        RuleFor(q => q.LeaveTypeId).NotEmpty();
        RuleFor(q => q.StartDate).NotEmpty();
        RuleFor(q => q.EndDate).GreaterThanOrEqualTo(q => q.StartDate).WithErrorCode(CommonErrors.InvalidDateRange.Code).When(q => q.EndDate is not null);
        RuleFor(q => q.StartPart).Must(p => p is LeaveDayPart.Full or LeaveDayPart.Afternoon).WithErrorCode(CommonErrors.ValidationCode);
        RuleFor(q => q.EndPart).Must(p => p is LeaveDayPart.Full or LeaveDayPart.Morning).WithErrorCode(CommonErrors.ValidationCode);
        RuleFor(q => q.Note).MaximumLength(LeaveRequest.NoteMaxLength);
        RuleFor(q => q.EmploymentIds).NotEmpty().When(q => q.OrgUnitId is null).WithErrorCode("hr.leave.errors.bulk_people_required");
        RuleFor(q => q.EmploymentIds!.Count).LessThanOrEqualTo(PreviewBulkLeaveQuery.MaxPeople).When(q => q.EmploymentIds is not null).OverridePropertyName("employmentIds");
    }
}

internal sealed class UploadLeaveRequestFileCommandValidator : AbstractValidator<UploadLeaveRequestFileCommand>
{
    public UploadLeaveRequestFileCommandValidator() => RuleFor(c => c.File).NotNull().OverridePropertyName(FileErrors.Field);
}

/// <summary>HR's access to requests through the scope of a permission.</summary>
public sealed class LeaveRequestAccess(
    IHrDbContext db,
    IAccessScopeProvider scopes,
    IPositionHolders holders,
    ICurrentUser currentUser,
    IOrganizationLookup organization,
    TimeProvider timeProvider)
{
    public Task<IQueryable<EmploymentId>> EmploymentsAsync(string permission, CancellationToken cancellationToken) =>
        LeaveRequestRules.VisibleEmploymentsAsync(db, scopes, holders, currentUser, organization, timeProvider, permission, cancellationToken);

    public async Task<bool> ReachesAsync(string permission, EmploymentId employmentId, CancellationToken cancellationToken) =>
        await (await EmploymentsAsync(permission, cancellationToken)).AnyAsync(id => id == employmentId, cancellationToken);

    /// <summary>A request (not a draft of someone else) whose employment the permission reaches; tracked when asked.</summary>
    public async Task<LeaveRequest?> FindAsync(string permission, Guid id, bool tracked, CancellationToken cancellationToken)
    {
        var requests = tracked ? db.LeaveRequests : db.LeaveRequests.AsNoTracking();
        var request = await requests.SingleOrDefaultAsync(r => r.Id == LeaveRequestId.From(id), cancellationToken);
        if (request is null || (request.Status == LeaveRequestStatus.Draft && request.RequesterUserId != currentUser.UserId))
        {
            return null;
        }

        return await ReachesAsync(permission, request.EmploymentId, cancellationToken) ? request : null;
    }

    /// <summary>The person of an employment the permission reaches.</summary>
    public async Task<(Employee Employee, Employment Employment)?> EmploymentAsync(string permission, Guid employmentId, CancellationToken cancellationToken) =>
        await ReachesAsync(permission, EmploymentId.From(employmentId), cancellationToken)
            ? await LeaveRequestRules.EmploymentAsync(db, employmentId, cancellationToken)
            : null;
}

internal sealed class GetLeaveRequestsQueryHandler(IHrDbContext db, LeaveRequestAccess access, IOrganizationLookup organization, ICurrentUser currentUser)
    : IQueryHandler<GetLeaveRequestsQuery, PagedResult<LeaveRequestListItem>>
{
    public async Task<Result<PagedResult<LeaveRequestListItem>>> Handle(GetLeaveRequestsQuery request, CancellationToken cancellationToken)
    {
        var employments = await access.EmploymentsAsync(request.Permission, cancellationToken);
        var requests = db.LeaveRequests.AsNoTracking().Where(r => r.Status != LeaveRequestStatus.Draft && employments.Contains(r.EmploymentId));
        if (request.CompanyId is { } companyId)
        {
            requests = requests.Where(r => r.CompanyId == companyId);
        }

        if (request.EmployeeId is { } employeeId)
        {
            requests = requests.Where(r => r.EmployeeId == EmployeeId.From(employeeId));
        }

        if (request.Statuses is { Count: > 0 } statuses)
        {
            requests = requests.Where(r => statuses.Contains(r.Status));
        }

        if (!string.IsNullOrWhiteSpace(request.LeaveTypeCode))
        {
            var code = request.LeaveTypeCode.Trim().ToUpperInvariant();
            requests = requests.Where(r => r.LeaveTypeCode == code);
        }

        if (request.From is { } from)
        {
            requests = requests.Where(r => r.EndDate >= from);
        }

        if (request.To is { } to)
        {
            requests = requests.Where(r => r.StartDate <= to);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = TextSearch.ContainsPattern(request.Search);
            var people = db.Employees.Where(e => TextSearch.Matches(e.FirstName + " " + e.LastName, pattern)).Select(e => e.Id);
            requests = requests.Where(r => TextSearch.Matches(r.Number, pattern) || people.Contains(r.EmployeeId));
        }

        var page = await GetLeaveRequestsQuery.Sorts.Apply(requests, request.Paging.Sort, GetLeaveRequestsQuery.DefaultSort)
            .ToPagedResultAsync(request.Paging, cancellationToken);
        return new PagedResult<LeaveRequestListItem>(
            await LeaveRequestRules.ItemsAsync(db, organization, currentUser, page.Items, cancellationToken),
            page.Page,
            page.PageSize,
            page.TotalCount);
    }
}

internal sealed class GetLeaveRequestQueryHandler(
    IHrDbContext db,
    LeaveRequestAccess access,
    IOrganizationLookup organization,
    ICurrentUser currentUser,
    IFileStore fileStore,
    TimeProvider timeProvider) : IQueryHandler<GetLeaveRequestQuery, LeaveRequestDetails>
{
    public async Task<Result<LeaveRequestDetails>> Handle(GetLeaveRequestQuery request, CancellationToken cancellationToken)
    {
        var leave = await access.FindAsync(request.Permission, request.Id, tracked: false, cancellationToken);
        if (leave is null)
        {
            return CommonErrors.NotFound;
        }

        var manages = await access.ReachesAsync(HrPermissions.Leave.Requests.Manage, leave.EmploymentId, cancellationToken);
        var today = await LeaveTypeRules.TodayAsync(organization, timeProvider, leave.CompanyId, cancellationToken);
        var own = leave.RequesterUserId == currentUser.UserId;
        var editable = own && leave.Status is LeaveRequestStatus.Draft or LeaveRequestStatus.Returned;
        var actions = new LeaveRequestActions(
            editable,
            editable,
            manages && (leave.IsOpen || (leave.IsApproved && leave.StartDate > today)),
            false,
            manages && leave.Status == LeaveRequestStatus.Approved && leave.StartDate <= today && leave.EndDate > leave.StartDate,
            manages && leave.Status is not (LeaveRequestStatus.Rejected or LeaveRequestStatus.Cancelled));
        return await LeaveRequestRules.DetailsAsync(db, organization, currentUser, fileStore, leave, actions, cancellationToken);
    }
}

internal sealed class CreateLeaveRequestOnBehalfCommandHandler(LeaveRequestAccess access, ICurrentUser currentUser, LeaveRequestSubmitter submitter)
    : ICommandHandler<CreateLeaveRequestOnBehalfCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateLeaveRequestOnBehalfCommand request, CancellationToken cancellationToken)
    {
        var person = await access.EmploymentAsync(request.Permission, request.EmploymentId, cancellationToken);
        if (person is not { } found || currentUser.UserId is not { } userId)
        {
            return CommonErrors.NotFound;
        }

        return await submitter.CreateAsync(found.Employee, found.Employment, request, LeaveRequestOrigin.OnBehalf, userId, submit: true, cancellationToken);
    }
}

internal sealed class RecordLeaveCommandHandler(LeaveRequestAccess access, ICurrentUser currentUser, LeaveRecorder recorder)
    : ICommandHandler<RecordLeaveCommand, Guid>
{
    public async Task<Result<Guid>> Handle(RecordLeaveCommand request, CancellationToken cancellationToken)
    {
        var person = await access.EmploymentAsync(request.Permission, request.EmploymentId, cancellationToken);
        if (person is not { } found || currentUser.UserId is not { } userId)
        {
            return CommonErrors.NotFound;
        }

        return await recorder.RecordAsync(found.Employee, found.Employment, request, LeaveRequestOrigin.Record, userId, cancellationToken);
    }
}

internal sealed class CreateBulkLeaveCommandHandler(LeaveRequestAccess access, ICurrentUser currentUser, LeaveRecorder recorder)
    : ICommandHandler<CreateBulkLeaveCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateBulkLeaveCommand request, CancellationToken cancellationToken)
    {
        var person = await access.EmploymentAsync(request.Permission, request.EmploymentId, cancellationToken);
        if (person is not { } found || currentUser.UserId is not { } userId)
        {
            return CommonErrors.NotFound;
        }

        return await recorder.RecordAsync(found.Employee, found.Employment, BulkLeave.Fields(request), LeaveRequestOrigin.Bulk, userId, cancellationToken);
    }
}

/// <summary>Records leaves approved without a flow: a past leave or one person of a bulk leave (D44).</summary>
public sealed class LeaveRecorder(
    IHrDbContext db,
    LeaveRequestChecker checker,
    LeaveRequestNotifier notifier,
    INumberGenerator numbers,
    TimeProvider timeProvider)
{
    public async Task<Result<Guid>> RecordAsync(
        Employee employee,
        Employment employment,
        ILeaveRequestFields fields,
        LeaveRequestOrigin origin,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var check = await checker.CheckAsync(new LeaveCheckRequest(employee, employment, fields, origin, true, null, 0), cancellationToken);
        if (!check.IsValid)
        {
            return check.Error!;
        }

        var number = await numbers.NextAsync(employment.CompanyId, HrNumberSequences.LeaveRequest, check.Today, cancellationToken);
        var leave = LeaveRequest.Record(
            number,
            employee.Id,
            employment.Id,
            employment.CompanyId,
            userId,
            origin,
            check.Period!,
            LeaveRequestRules.Contact(fields),
            timeProvider.GetUtcNow());
        db.LeaveRequests.Add(leave);
        await notifier.NotifyAsync(
            HrNotificationTypes.LeaveRequestCompleted,
            leave,
            LeaveRecipients.Employee,
            new Dictionary<string, string> { ["result"] = notifier.Status(leave.Status) },
            null,
            cancellationToken);
        return leave.Id.Value;
    }
}

internal sealed class PreviewLeaveRequestQueryHandler(LeaveRequestAccess access, ICurrentUser currentUser, LeaveRequestPreviewer previewer)
    : IQueryHandler<PreviewLeaveRequestQuery, LeaveRequestPreview>
{
    public async Task<Result<LeaveRequestPreview>> Handle(PreviewLeaveRequestQuery request, CancellationToken cancellationToken)
    {
        var person = await access.EmploymentAsync(request.Permission, request.EmploymentId, cancellationToken);
        return person is { } found && currentUser.UserId is { } userId
            ? await previewer.PreviewAsync(found.Employee, found.Employment, request, LeaveRequestOrigin.OnBehalf, null, userId, cancellationToken)
            : CommonErrors.NotFound;
    }
}

internal sealed class PreviewLeaveRecordQueryHandler(LeaveRequestAccess access, ICurrentUser currentUser, LeaveRequestPreviewer previewer)
    : IQueryHandler<PreviewLeaveRecordQuery, LeaveRequestPreview>
{
    public async Task<Result<LeaveRequestPreview>> Handle(PreviewLeaveRecordQuery request, CancellationToken cancellationToken)
    {
        var person = await access.EmploymentAsync(request.Permission, request.EmploymentId, cancellationToken);
        return person is { } found && currentUser.UserId is { } userId
            ? await previewer.PreviewAsync(found.Employee, found.Employment, request, LeaveRequestOrigin.Record, null, userId, cancellationToken)
            : CommonErrors.NotFound;
    }
}

internal sealed class CancelLeaveRequestCommandHandler(
    LeaveRequestAccess access,
    IOrganizationLookup organization,
    IIntegrationEventPublisher publisher,
    LeaveRequestNotifier notifier,
    TimeProvider timeProvider) : ICommandHandler<CancelLeaveRequestCommand>
{
    public async Task<Result> Handle(CancelLeaveRequestCommand request, CancellationToken cancellationToken)
    {
        var leave = await access.FindAsync(request.Permission, request.Id, tracked: true, cancellationToken);
        if (leave is null)
        {
            return CommonErrors.NotFound;
        }

        var now = timeProvider.GetUtcNow();
        string documentType;
        bool flowOpen;
        if (leave.IsApproved)
        {
            var today = await LeaveTypeRules.TodayAsync(organization, timeProvider, leave.CompanyId, cancellationToken);
            var cancelled = leave.CancelApproved(request.Reason, today, now);
            if (cancelled.IsFailure)
            {
                return cancelled.Error;
            }

            (documentType, flowOpen) = (HrApprovalDocumentTypes.LeaveCancellation, cancelled.Value);
        }
        else
        {
            var cancelled = leave.Cancel(request.Reason, now);
            if (cancelled.IsFailure)
            {
                return cancelled.Error;
            }

            (documentType, flowOpen) = (HrApprovalDocumentTypes.LeaveRequest, cancelled.Value);
        }

        if (flowOpen)
        {
            await publisher.PublishAsync(new ApprovalCancellationRequested(documentType, leave.Id.Value.ToString()), cancellationToken);
        }

        await notifier.NotifyAsync(
            HrNotificationTypes.LeaveCancellationCompleted,
            leave,
            LeaveRecipients.Requester | LeaveRecipients.Employee,
            new Dictionary<string, string> { ["result"] = notifier.Status(leave.Status) },
            null,
            cancellationToken);
        return Result.Success();
    }
}

internal sealed class ReturnFromLeaveEarlyCommandHandler(
    IHrDbContext db,
    LeaveRequestAccess access,
    ILeaveDurationCalculator calculator,
    IOrganizationLookup organization,
    TimeProvider timeProvider) : ICommandHandler<ReturnFromLeaveEarlyCommand>
{
    public async Task<Result> Handle(ReturnFromLeaveEarlyCommand request, CancellationToken cancellationToken)
    {
        var leave = await access.FindAsync(request.Permission, request.Id, tracked: true, cancellationToken);
        var person = leave is null ? null : await LeaveRequestRules.EmploymentAsync(db, leave.EmploymentId.Value, cancellationToken);
        var type = leave is null ? null : await db.LeaveTypes.AsNoTracking().SingleOrDefaultAsync(t => t.Id == leave.LeaveTypeId, cancellationToken);
        if (leave is null || person is not { } found || type is null)
        {
            return CommonErrors.NotFound;
        }

        if (request.EndDate < leave.StartDate || request.EndDate > leave.EndDate)
        {
            return LeaveRequestErrors.InvalidEarlyReturn;
        }

        var hourly = leave.Basis == DurationBasis.Hours;
        var counted = await calculator.CalculateAsync(
            new LeaveDurationRequest(
                found.Employment,
                type,
                leave.StartDate,
                request.EndDate,
                leave.StartPart,
                hourly ? LeaveDayPart.Full : request.EndPart,
                leave.StartTime,
                hourly ? request.EndTime ?? leave.EndTime : null,
                leave.EventDate),
            cancellationToken);
        if (counted.IsFailure)
        {
            return counted.Error;
        }

        var duration = counted.Value.Duration;
        var today = await LeaveTypeRules.TodayAsync(organization, timeProvider, leave.CompanyId, cancellationToken);
        return leave.ReturnEarly(
            leave.Period with
            {
                EndDate = request.EndDate,
                EndPart = hourly ? LeaveDayPart.Full : request.EndPart,
                EndTime = hourly ? request.EndTime ?? leave.EndTime : null,
                Quantity = duration.Quantity,
                Days = hourly ? duration.DayEquivalent ?? 0 : duration.Quantity,
            },
            today);
    }
}

internal static class BulkLeave
{
    public static LeaveRequestFields Fields(CreateBulkLeaveCommand command) =>
        new(command.LeaveTypeId, command.StartDate, command.EndDate, command.StartPart, command.EndPart, null, null, null, 0, command.Note, null, null, null);

    public static LeaveRequestFields Fields(PreviewBulkLeaveQuery query) =>
        new(query.LeaveTypeId, query.StartDate, query.EndDate, query.StartPart, query.EndPart, null, null, null, 0, query.Note, null, null, null);
}

internal sealed class PreviewBulkLeaveQueryHandler(
    IHrDbContext db,
    IAccessScopeProvider scopes,
    IPositionHolders holders,
    ICurrentUser currentUser,
    IOrganizationLookup organization,
    TimeProvider timeProvider,
    LeaveRequestChecker checker,
    ITextLocalizer localizer) : IQueryHandler<PreviewBulkLeaveQuery, IReadOnlyList<BulkLeaveRow>>
{
    public async Task<Result<IReadOnlyList<BulkLeaveRow>>> Handle(PreviewBulkLeaveQuery request, CancellationToken cancellationToken)
    {
        var scope = await EmployeeScope.ResolveAsync(scopes, request.Permission, db, holders, currentUser, cancellationToken);
        var days = await CompanyDays.ResolveAsync(db, organization, timeProvider, cancellationToken);
        var rows = scope.Filter(EmploymentRows.Query(db.Employees.AsNoTracking(), days), db)
            .Where(r => r.CompanyId == request.CompanyId && r.HireDate <= request.StartDate && (r.TerminationDate == null || r.TerminationDate >= request.StartDate));
        if (request.EmploymentIds is { Count: > 0 } named)
        {
            var ids = named.Select(EmploymentId.From).ToList();
            rows = rows.Where(r => ids.Contains(r.EmploymentId));
        }

        if (request.OrgUnitId is { } unitId)
        {
            var root = OrgUnitId.From(unitId);
            var units = db.OrgUnits.AsNoTracking();
            var unitIds = request.IncludeSubUnits
                ? units.Where(u => units.Any(r => r.Id == root && OrgPaths.IsWithin(u.Path, r.Path))).Select(u => u.Id)
                : units.Where(u => u.Id == root).Select(u => u.Id);
            var positions = db.Positions.AsNoTracking().Where(p => unitIds.Contains(p.OrgUnitId)).Select(p => p.Id);
            rows = rows.Where(r => r.PositionId != null && positions.Contains(r.PositionId.Value));
        }

        var targets = await rows
            .OrderBy(r => r.FirstName).ThenBy(r => r.LastName)
            .Select(r => new { r.EmployeeId, r.EmploymentId, r.FirstName, r.LastName, r.RegistryNumber })
            .Take(PreviewBulkLeaveQuery.MaxPeople)
            .ToListAsync(cancellationToken);
        var fields = BulkLeave.Fields(request);
        var result = new List<BulkLeaveRow>();
        foreach (var target in targets)
        {
            var person = await LeaveRequestRules.EmploymentAsync(db, target.EmploymentId.Value, cancellationToken);
            var check = await checker.CheckAsync(new LeaveCheckRequest(person!.Value.Employee, person.Value.Employment, fields, LeaveRequestOrigin.Bulk, true, null, 0), cancellationToken);
            LeaveRequestProblem? problem = null;
            if (check.Error is { } error)
            {
                var field = error.Fields.Count > 0 ? error.Fields[0] : null;
                var code = field?.Code ?? error.Code;
                problem = new LeaveRequestProblem(code, localizer.Get(code, field?.Args ?? error.Args), field?.Field);
            }

            result.Add(new BulkLeaveRow(
                target.EmployeeId.Value,
                target.EmploymentId.Value,
                $"{target.FirstName} {target.LastName}",
                target.RegistryNumber,
                check.Period?.Quantity,
                check.Period?.Days,
                check.AvailableAfter,
                problem,
                check.Warnings));
        }

        return result;
    }
}

internal sealed class UploadLeaveRequestFileCommandHandler(IHrDbContext db, LeaveRequestAccess access, IFileStore files)
    : ICommandHandler<UploadLeaveRequestFileCommand, Guid>
{
    public async Task<Result<Guid>> Handle(UploadLeaveRequestFileCommand request, CancellationToken cancellationToken)
    {
        var leave = await access.FindAsync(request.Permission, request.RequestId, tracked: false, cancellationToken);
        if (leave is null)
        {
            return CommonErrors.NotFound;
        }

        if (leave.Status is LeaveRequestStatus.Rejected or LeaveRequestStatus.Cancelled)
        {
            return LeaveRequestErrors.NotEditable;
        }

        var stored = await files.SaveAsync(LeaveRequestFiles.OwnerType, leave.Id.Value, StoredFilePurposes.Attachment, request.File!, files.AttachmentRules, cancellationToken);
        if (stored.IsFailure)
        {
            return stored.Error;
        }

        db.StoredFiles.Add(stored.Value);
        return stored.Value.Id;
    }
}

internal sealed class DeleteLeaveRequestFileCommandHandler(IHrDbContext db, LeaveRequestAccess access, IFileStore files)
    : ICommandHandler<DeleteLeaveRequestFileCommand>
{
    public async Task<Result> Handle(DeleteLeaveRequestFileCommand request, CancellationToken cancellationToken)
    {
        var leave = await access.FindAsync(request.Permission, request.RequestId, tracked: false, cancellationToken);
        var file = leave is null ? null : await LeaveRequestFiles.Of(db, leave.Id.Value).SingleOrDefaultAsync(f => f.Id == request.FileId, cancellationToken);
        if (file is null)
        {
            return CommonErrors.NotFound;
        }

        db.StoredFiles.Remove(file);
        files.PurgeAfterCommit(file);
        return Result.Success();
    }
}

internal sealed class GetLeaveRequestFileQueryHandler(IHrDbContext db, LeaveRequestAccess access, IFileStore files)
    : IQueryHandler<GetLeaveRequestFileQuery, HrFileDownload>
{
    public async Task<Result<HrFileDownload>> Handle(GetLeaveRequestFileQuery request, CancellationToken cancellationToken) =>
        await access.FindAsync(request.Permission, request.RequestId, tracked: false, cancellationToken) is null
            ? CommonErrors.NotFound
            : await LeaveRequestDownloads.OpenAsync(db, files, request.RequestId, request.FileId, cancellationToken);
}
