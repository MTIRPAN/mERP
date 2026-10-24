using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Files;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Persistence;
using Merp.Application.Persistence.Files;
using Merp.Modules.HR.Application.Employees;
using Merp.Modules.HR.Application.SelfService;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Leave;
using Merp.Modules.Platform.Contracts.Approvals;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Leave;

/// <summary>
/// The user asks for leave for themselves (D44): a draft, or sent at once with <see cref="Submit"/>. Without
/// <see cref="EmploymentId"/> the main employment asks. Returns the request's id.
/// </summary>
public sealed record CreateMyLeaveRequestCommand(
    Guid? EmploymentId,
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
    Guid? SubstituteEmployeeId = null,
    bool Submit = false) : ICommand<Guid>, IRequirePermission, ILeaveRequestFields
{
    [JsonIgnore]
    public string Permission => HrPermissions.Self.LeaveRequest;
}

/// <summary>Corrects the user's draft or returned request, and sends it with <see cref="Submit"/>.</summary>
public sealed record UpdateMyLeaveRequestCommand(
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
    Guid? SubstituteEmployeeId = null,
    bool Submit = false) : ICommand, IRequirePermission, ILeaveRequestFields
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => HrPermissions.Self.LeaveRequest;
}

/// <summary>Sends the user's draft or returned request as it is.</summary>
public sealed record SubmitMyLeaveRequestCommand(Guid Id) : ICommand, IRequirePermission
{
    public string Permission => HrPermissions.Self.LeaveRequest;
}

/// <summary>The user cancels their draft or a request not decided yet; Platform closes its approval.</summary>
public sealed record CancelMyLeaveRequestCommand(string? Reason) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => HrPermissions.Self.LeaveRequest;
}

/// <summary>The user asks to cancel their approved leave before it starts; the reason is required (D44).</summary>
public sealed record RequestMyLeaveCancellationCommand(string? Reason) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => HrPermissions.Self.LeaveRequest;
}

/// <summary>
/// The user's request checked before it is saved: length, balance, problems, warnings and approvers (D44). With
/// <see cref="RequestId"/> the request being corrected.
/// </summary>
public sealed record PreviewMyLeaveRequestQuery(
    Guid? EmploymentId,
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
    Guid? SubstituteEmployeeId = null,
    Guid? RequestId = null) : IQuery<LeaveRequestPreview>, IRequirePermission, ILeaveRequestFields
{
    [JsonIgnore]
    public string Permission => HrPermissions.Self.LeaveRequest;
}

/// <summary>The user's leave requests, of all their employments, newest first unless sorted.</summary>
public sealed record GetMyLeaveRequestsQuery(PageRequest Paging, IReadOnlyList<LeaveRequestStatus>? Statuses = null)
    : IQuery<PagedResult<LeaveRequestListItem>>, IRequirePermission
{
    public const string DefaultSort = "-startDate";

    internal static readonly SortMap<LeaveRequest> Sorts = SortMap<LeaveRequest>.By(r => r.Id)
        .Add("startDate", r => r.StartDate)
        .Add("createdAt", r => r.CreatedAt)
        .Add("number", r => r.Number);

    public string Permission => HrPermissions.Self.LeaveRead;
}

/// <summary>One of the user's leave requests with its details, files and actions.</summary>
public sealed record GetMyLeaveRequestQuery(Guid Id) : IQuery<LeaveRequestDetails>, IRequirePermission
{
    public string Permission => HrPermissions.Self.LeaveRead;
}

/// <summary>The user's leave balances; without <see cref="EmploymentId"/> those of the main employment.</summary>
public sealed record GetMyLeaveBalancesQuery(Guid? EmploymentId = null) : IQuery<IReadOnlyList<LeaveBalanceItem>>, IRequirePermission
{
    public string Permission => HrPermissions.Self.LeaveRead;
}

/// <summary>Attaches a file to the user's request while it is open (D31, D44); returns the file's id.</summary>
public sealed record UploadMyLeaveRequestFileCommand(Guid RequestId, FileUpload? File) : ICommand<Guid>, IRequirePermission
{
    public string Permission => HrPermissions.Self.LeaveRequest;
}

/// <summary>Deletes a file of the user's draft or returned request.</summary>
public sealed record DeleteMyLeaveRequestFileCommand(Guid RequestId, Guid FileId) : ICommand, IRequirePermission
{
    public string Permission => HrPermissions.Self.LeaveRequest;
}

/// <summary>A file of one of the user's requests, for download.</summary>
public sealed record GetMyLeaveRequestFileQuery(Guid RequestId, Guid FileId) : IQuery<HrFileDownload>, IRequirePermission
{
    public string Permission => HrPermissions.Self.LeaveRead;
}

internal sealed class CreateMyLeaveRequestCommandValidator : AbstractValidator<CreateMyLeaveRequestCommand>
{
    public CreateMyLeaveRequestCommandValidator() => LeaveRequestFieldRules.Add(this);
}

internal sealed class UpdateMyLeaveRequestCommandValidator : AbstractValidator<UpdateMyLeaveRequestCommand>
{
    public UpdateMyLeaveRequestCommandValidator() => LeaveRequestFieldRules.Add(this);
}

internal sealed class PreviewMyLeaveRequestQueryValidator : AbstractValidator<PreviewMyLeaveRequestQuery>
{
    public PreviewMyLeaveRequestQueryValidator() => LeaveRequestFieldRules.Add(this);
}

internal sealed class RequestMyLeaveCancellationCommandValidator : AbstractValidator<RequestMyLeaveCancellationCommand>
{
    public RequestMyLeaveCancellationCommandValidator() =>
        RuleFor(c => c.Reason).NotEmpty().WithErrorCode("hr.leave.errors.reason_required").MaximumLength(LeaveRequest.CommentMaxLength);
}

internal sealed class CancelMyLeaveRequestCommandValidator : AbstractValidator<CancelMyLeaveRequestCommand>
{
    public CancelMyLeaveRequestCommandValidator() => RuleFor(c => c.Reason).MaximumLength(LeaveRequest.CommentMaxLength);
}

internal sealed class GetMyLeaveRequestsQueryValidator : AbstractValidator<GetMyLeaveRequestsQuery>
{
    public GetMyLeaveRequestsQueryValidator()
    {
        RuleFor(q => q.Paging.Sort).Must(GetMyLeaveRequestsQuery.Sorts.IsValid).WithErrorCode(CommonErrors.InvalidSort.Code).OverridePropertyName("sort");
        RuleForEach(q => q.Statuses).IsInEnum().OverridePropertyName("status");
    }
}

internal sealed class UploadMyLeaveRequestFileCommandValidator : AbstractValidator<UploadMyLeaveRequestFileCommand>
{
    public UploadMyLeaveRequestFileCommandValidator() => RuleFor(c => c.File).NotNull().OverridePropertyName(FileErrors.Field);
}

/// <summary>The user's own person and requests.</summary>
internal static class MyLeave
{
    /// <summary>The person linked to the user and the employment asking: the one named, or the main one.</summary>
    public static async Task<(Employee Employee, Employment Employment)?> EmploymentAsync(
        IHrDbContext db,
        ICurrentUser currentUser,
        Guid? employmentId,
        CancellationToken cancellationToken)
    {
        var employee = await SelfServiceRules.MeAsync(db, currentUser, tracked: false, cancellationToken);
        var employment = employee is null
            ? null
            : employmentId is { } id ? employee.Employments.SingleOrDefault(m => m.Id == EmploymentId.From(id)) : LeaveRequestRules.MainEmployment(employee);
        return employee is null || employment is null ? null : (employee, employment);
    }

    /// <summary>A request the user asked for themselves, tracked.</summary>
    public static Task<LeaveRequest?> OwnAsync(IHrDbContext db, ICurrentUser currentUser, Guid id, CancellationToken cancellationToken) =>
        currentUser.UserId is { } userId
            ? db.LeaveRequests.SingleOrDefaultAsync(r => r.Id == LeaveRequestId.From(id) && r.RequesterUserId == userId, cancellationToken)
            : Task.FromResult<LeaveRequest?>(null);

    /// <summary>A leave of the user's person, whoever asked for it.</summary>
    public static IQueryable<LeaveRequest> Mine(IHrDbContext db, ICurrentUser currentUser) =>
        currentUser.EmployeeId is { } employeeId
            ? db.LeaveRequests.Where(r => r.EmployeeId == EmployeeId.From(employeeId))
            : db.LeaveRequests.Where(_ => false);
}

internal sealed class CreateMyLeaveRequestCommandHandler(IHrDbContext db, ICurrentUser currentUser, LeaveRequestSubmitter submitter)
    : ICommandHandler<CreateMyLeaveRequestCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateMyLeaveRequestCommand request, CancellationToken cancellationToken)
    {
        if (await MyLeave.EmploymentAsync(db, currentUser, request.EmploymentId, cancellationToken) is not { } me || currentUser.UserId is not { } userId)
        {
            return CommonErrors.NotFound;
        }

        return await submitter.CreateAsync(me.Employee, me.Employment, request, LeaveRequestOrigin.Self, userId, request.Submit, cancellationToken);
    }
}

internal sealed class UpdateMyLeaveRequestCommandHandler(IHrDbContext db, ICurrentUser currentUser, LeaveRequestSubmitter submitter)
    : ICommandHandler<UpdateMyLeaveRequestCommand>
{
    public async Task<Result> Handle(UpdateMyLeaveRequestCommand request, CancellationToken cancellationToken)
    {
        var leave = await MyLeave.OwnAsync(db, currentUser, request.Id, cancellationToken);
        return leave is null ? CommonErrors.NotFound : await submitter.UpdateAsync(leave, request, request.Submit, cancellationToken);
    }
}

internal sealed class SubmitMyLeaveRequestCommandHandler(IHrDbContext db, ICurrentUser currentUser, LeaveRequestSubmitter submitter)
    : ICommandHandler<SubmitMyLeaveRequestCommand>
{
    public async Task<Result> Handle(SubmitMyLeaveRequestCommand request, CancellationToken cancellationToken)
    {
        var leave = await MyLeave.OwnAsync(db, currentUser, request.Id, cancellationToken);
        return leave is null ? CommonErrors.NotFound : await submitter.UpdateAsync(leave, LeaveRequestFields.Of(leave), true, cancellationToken);
    }
}

internal sealed class CancelMyLeaveRequestCommandHandler(
    IHrDbContext db,
    ICurrentUser currentUser,
    IIntegrationEventPublisher publisher,
    TimeProvider timeProvider) : ICommandHandler<CancelMyLeaveRequestCommand>
{
    public async Task<Result> Handle(CancelMyLeaveRequestCommand request, CancellationToken cancellationToken)
    {
        var leave = await MyLeave.OwnAsync(db, currentUser, request.Id, cancellationToken);
        if (leave is null)
        {
            return CommonErrors.NotFound;
        }

        var cancelled = leave.Cancel(request.Reason, timeProvider.GetUtcNow());
        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        if (cancelled.Value)
        {
            await publisher.PublishAsync(new ApprovalCancellationRequested(HrApprovalDocumentTypes.LeaveRequest, leave.Id.Value.ToString()), cancellationToken);
        }

        return Result.Success();
    }
}

internal sealed class RequestMyLeaveCancellationCommandHandler(
    IHrDbContext db,
    ICurrentUser currentUser,
    IOrganizationLookup organization,
    LeaveRequestNotifier notifier,
    TimeProvider timeProvider) : ICommandHandler<RequestMyLeaveCancellationCommand>
{
    public async Task<Result> Handle(RequestMyLeaveCancellationCommand request, CancellationToken cancellationToken)
    {
        var leave = await MyLeave.Mine(db, currentUser).SingleOrDefaultAsync(r => r.Id == LeaveRequestId.From(request.Id), cancellationToken);
        var person = leave is null ? null : await LeaveRequestRules.EmploymentAsync(db, leave.EmploymentId.Value, cancellationToken);
        if (leave is null || person is not { } found || currentUser.UserId is null)
        {
            return CommonErrors.NotFound;
        }

        var today = await LeaveTypeRules.TodayAsync(organization, timeProvider, leave.CompanyId, cancellationToken);
        var asked = leave.RequestCancellation(request.Reason, today, timeProvider.GetUtcNow());
        if (asked.IsFailure)
        {
            return asked;
        }

        await notifier.RequestApprovalAsync(HrApprovalDocumentTypes.LeaveCancellation, leave, found.Employment, today, cancellationToken);
        return Result.Success();
    }
}

internal sealed class PreviewMyLeaveRequestQueryHandler(IHrDbContext db, ICurrentUser currentUser, LeaveRequestPreviewer previewer)
    : IQueryHandler<PreviewMyLeaveRequestQuery, LeaveRequestPreview>
{
    public async Task<Result<LeaveRequestPreview>> Handle(PreviewMyLeaveRequestQuery request, CancellationToken cancellationToken)
    {
        var me = await MyLeave.EmploymentAsync(db, currentUser, request.EmploymentId, cancellationToken);
        if (me is null || currentUser.UserId is not { } userId)
        {
            return CommonErrors.NotFound;
        }

        LeaveRequestId? requestId = null;
        if (request.RequestId is { } id)
        {
            if (!await MyLeave.Mine(db, currentUser).AnyAsync(r => r.Id == LeaveRequestId.From(id), cancellationToken))
            {
                return CommonErrors.NotFound;
            }

            requestId = LeaveRequestId.From(id);
        }

        return await previewer.PreviewAsync(me.Value.Employee, me.Value.Employment, request, LeaveRequestOrigin.Self, requestId, userId, cancellationToken);
    }
}

internal sealed class GetMyLeaveRequestsQueryHandler(IHrDbContext db, ICurrentUser currentUser, IOrganizationLookup organization)
    : IQueryHandler<GetMyLeaveRequestsQuery, PagedResult<LeaveRequestListItem>>
{
    public async Task<Result<PagedResult<LeaveRequestListItem>>> Handle(GetMyLeaveRequestsQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is null)
        {
            return CommonErrors.NotFound;
        }

        var requests = MyLeave.Mine(db, currentUser).AsNoTracking();
        if (request.Statuses is { Count: > 0 } statuses)
        {
            requests = requests.Where(r => statuses.Contains(r.Status));
        }

        var page = await GetMyLeaveRequestsQuery.Sorts.Apply(requests, request.Paging.Sort, GetMyLeaveRequestsQuery.DefaultSort)
            .ToPagedResultAsync(request.Paging, cancellationToken);
        return new PagedResult<LeaveRequestListItem>(
            await LeaveRequestRules.ItemsAsync(db, organization, currentUser, page.Items, cancellationToken),
            page.Page,
            page.PageSize,
            page.TotalCount);
    }
}

internal sealed class GetMyLeaveRequestQueryHandler(
    IHrDbContext db,
    ICurrentUser currentUser,
    IOrganizationLookup organization,
    IFileStore fileStore,
    TimeProvider timeProvider) : IQueryHandler<GetMyLeaveRequestQuery, LeaveRequestDetails>
{
    public async Task<Result<LeaveRequestDetails>> Handle(GetMyLeaveRequestQuery request, CancellationToken cancellationToken)
    {
        var leave = await MyLeave.Mine(db, currentUser).AsNoTracking().SingleOrDefaultAsync(r => r.Id == LeaveRequestId.From(request.Id), cancellationToken);
        if (leave is null)
        {
            return CommonErrors.NotFound;
        }

        var today = await LeaveTypeRules.TodayAsync(organization, timeProvider, leave.CompanyId, cancellationToken);
        var own = leave.RequesterUserId == currentUser.UserId;
        var editable = own && leave.Status is LeaveRequestStatus.Draft or LeaveRequestStatus.Returned;
        var actions = new LeaveRequestActions(
            editable,
            editable,
            own && leave.Status is LeaveRequestStatus.Draft or LeaveRequestStatus.Pending or LeaveRequestStatus.Returned,
            leave.Status == LeaveRequestStatus.Approved && leave.StartDate > today && leave.CancellationRequestedAt is null,
            false,
            own && leave.Status is LeaveRequestStatus.Draft or LeaveRequestStatus.Pending or LeaveRequestStatus.Returned);
        return await LeaveRequestRules.DetailsAsync(db, organization, currentUser, fileStore, leave, actions, cancellationToken);
    }
}

internal sealed class GetMyLeaveBalancesQueryHandler(IHrDbContext db, ICurrentUser currentUser, LeaveBalanceReader reader)
    : IQueryHandler<GetMyLeaveBalancesQuery, IReadOnlyList<LeaveBalanceItem>>
{
    public async Task<Result<IReadOnlyList<LeaveBalanceItem>>> Handle(GetMyLeaveBalancesQuery request, CancellationToken cancellationToken)
    {
        var me = await MyLeave.EmploymentAsync(db, currentUser, request.EmploymentId, cancellationToken);
        return me is { } found ? Result.Success(await reader.ReadAsync(found.Employee, found.Employment, cancellationToken)) : CommonErrors.NotFound;
    }
}

internal sealed class UploadMyLeaveRequestFileCommandHandler(IHrDbContext db, ICurrentUser currentUser, IFileStore files)
    : ICommandHandler<UploadMyLeaveRequestFileCommand, Guid>
{
    public async Task<Result<Guid>> Handle(UploadMyLeaveRequestFileCommand request, CancellationToken cancellationToken)
    {
        var leave = await MyLeave.OwnAsync(db, currentUser, request.RequestId, cancellationToken);
        if (leave is null)
        {
            return CommonErrors.NotFound;
        }

        if (leave.Status is not (LeaveRequestStatus.Draft or LeaveRequestStatus.Pending or LeaveRequestStatus.Returned))
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

internal sealed class DeleteMyLeaveRequestFileCommandHandler(IHrDbContext db, ICurrentUser currentUser, IFileStore files)
    : ICommandHandler<DeleteMyLeaveRequestFileCommand>
{
    public async Task<Result> Handle(DeleteMyLeaveRequestFileCommand request, CancellationToken cancellationToken)
    {
        var leave = await MyLeave.OwnAsync(db, currentUser, request.RequestId, cancellationToken);
        var file = leave is null ? null : await LeaveRequestFiles.Of(db, leave.Id.Value).SingleOrDefaultAsync(f => f.Id == request.FileId, cancellationToken);
        if (leave is null || file is null)
        {
            return CommonErrors.NotFound;
        }

        if (leave.Status is not (LeaveRequestStatus.Draft or LeaveRequestStatus.Returned))
        {
            return LeaveRequestErrors.NotEditable;
        }

        db.StoredFiles.Remove(file);
        files.PurgeAfterCommit(file);
        return Result.Success();
    }
}

internal sealed class GetMyLeaveRequestFileQueryHandler(IHrDbContext db, ICurrentUser currentUser, IFileStore files)
    : IQueryHandler<GetMyLeaveRequestFileQuery, HrFileDownload>
{
    public async Task<Result<HrFileDownload>> Handle(GetMyLeaveRequestFileQuery request, CancellationToken cancellationToken)
    {
        if (!await MyLeave.Mine(db, currentUser).AnyAsync(r => r.Id == LeaveRequestId.From(request.RequestId), cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        return await LeaveRequestDownloads.OpenAsync(db, files, request.RequestId, request.FileId, cancellationToken);
    }
}

internal static class LeaveRequestDownloads
{
    public static async Task<Result<HrFileDownload>> OpenAsync(IHrDbContext db, IFileStore files, Guid requestId, Guid fileId, CancellationToken cancellationToken)
    {
        var file = await LeaveRequestFiles.Of(db, requestId).AsNoTracking().SingleOrDefaultAsync(f => f.Id == fileId, cancellationToken);
        if (file is null)
        {
            return CommonErrors.NotFound;
        }

        var content = await files.OpenReadAsync(file, cancellationToken);
        return new HrFileDownload(file.FileName, file.ContentType, file.Length, content);
    }
}
