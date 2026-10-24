using System.Globalization;
using System.Text.Json;
using Merp.Application.Abstractions.Approvals;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Numbering;
using Merp.Application.Abstractions.Security;
using Merp.Application.Persistence.Files;
using Merp.Modules.HR.Application.Documents;
using Merp.Modules.HR.Application.Employees;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Application.SelfService;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Leave;
using Merp.Modules.Platform.Contracts.Approvals;
using Merp.Modules.Platform.Contracts.Notifications;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Leave;

/// <summary>A leave request in a list (D44).</summary>
/// <remarks>
/// EndDate is the last day of leave, the travel days follow it. Quantity is in days, or in hours for an hourly leave;
/// Days is the length in days.
/// </remarks>
public sealed record LeaveRequestListItem(
    Guid Id,
    string Number,
    Guid EmployeeId,
    string EmployeeName,
    Guid EmploymentId,
    Guid CompanyId,
    string? CompanyName,
    string LeaveTypeCode,
    string LeaveTypeName,
    DateOnly StartDate,
    DateOnly EndDate,
    LeaveDayPart StartPart,
    LeaveDayPart EndPart,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    int TravelDays,
    DurationBasis Basis,
    decimal Quantity,
    decimal Days,
    LeaveRequestStatus Status,
    LeaveRequestOrigin Origin,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt);

/// <summary>What the caller may do with a request now.</summary>
/// <param name="Edit">Correct it (a draft or a returned request of the requester).</param>
/// <param name="Submit">Send it.</param>
/// <param name="Cancel">Cancel it: the requester while it is not decided, HR also once approved and not started.</param>
/// <param name="RequestCancellation">Ask to cancel the approved leave before it starts.</param>
/// <param name="ReturnEarly">End the started leave early (HR).</param>
/// <param name="ManageFiles">Attach and delete files.</param>
public sealed record LeaveRequestActions(bool Edit, bool Submit, bool Cancel, bool RequestCancellation, bool ReturnEarly, bool ManageFiles);

/// <param name="Id">The stored file.</param>
/// <param name="FileName">Its name.</param>
/// <param name="ContentType">Its media type.</param>
/// <param name="Length">Its size in bytes.</param>
/// <param name="CreatedAt">When it was attached.</param>
public sealed record LeaveRequestFileItem(Guid Id, string FileName, string ContentType, long Length, DateTimeOffset CreatedAt);

/// <summary>A leave request with its details, files and the caller's actions (D44).</summary>
/// <remarks>
/// The approval timeline is read from Platform with the document type <c>hr.leave-request</c> (and
/// <c>hr.leave-cancellation</c> for the cancellation) and the request's id.
/// </remarks>
public sealed record LeaveRequestDetails(
    LeaveRequestListItem Request,
    Guid LeaveTypeId,
    DateOnly? EventDate,
    string? DeductsFrom,
    string? Note,
    string? Address,
    string? Phone,
    Guid? SubstituteEmployeeId,
    string? SubstituteName,
    string? ReturnComment,
    string? CancellationReason,
    DateTimeOffset? CancellationRequestedAt,
    DateOnly? OriginalEndDate,
    DateTimeOffset? DecidedAt,
    bool RequiresDocument,
    IReadOnlyList<LeaveRequestFileItem> Files,
    IReadOnlyList<string> FileExtensions,
    long MaxFileBytes,
    LeaveRequestActions Actions);

/// <summary>Leave request files (D31): a request's attachments belong to the request.</summary>
public static class LeaveRequestFiles
{
    public const string OwnerType = "hr.leave_request";

    internal static IQueryable<StoredFile> Of(IHrDbContext db, Guid requestId) =>
        db.StoredFiles.Where(f => f.OwnerType == OwnerType && f.OwnerId == requestId && f.Purpose == StoredFilePurposes.Attachment);

    internal static Task<int> CountAsync(IHrDbContext db, Guid requestId, CancellationToken cancellationToken) =>
        Of(db, requestId).CountAsync(cancellationToken);
}

/// <summary>Helpers the leave request handlers share.</summary>
internal static class LeaveRequestRules
{
    private const string Culture = "tr-TR";

    /// <summary>The person with employments and assignments, untracked; null when missing.</summary>
    public static Task<Employee?> EmployeeAsync(IHrDbContext db, EmployeeId employeeId, CancellationToken cancellationToken) =>
        db.Employees.AsNoTracking()
            .Include(e => e.Employments)
            .ThenInclude(m => m.Assignments)
            .SingleOrDefaultAsync(e => e.Id == employeeId, cancellationToken);

    /// <summary>The person of an employment, untracked; null when missing.</summary>
    public static async Task<(Employee Employee, Employment Employment)?> EmploymentAsync(IHrDbContext db, Guid employmentId, CancellationToken cancellationToken)
    {
        var id = EmploymentId.From(employmentId);
        var employee = await db.Employees.AsNoTracking()
            .Include(e => e.Employments)
            .ThenInclude(m => m.Assignments)
            .SingleOrDefaultAsync(e => e.Employments.Any(m => m.Id == id), cancellationToken);
        return employee is null ? null : (employee, employee.Employments.Single(m => m.Id == id));
    }

    /// <summary>The employment a person asks leave for when they name none: the primary open one, else the latest.</summary>
    public static Employment? MainEmployment(Employee employee) =>
        employee.Employments.Where(m => m.IsOpen).OrderByDescending(m => m.IsPrimary).ThenBy(m => m.HireDate).FirstOrDefault()
            ?? employee.Employments.MaxBy(m => m.HireDate);

    /// <summary>The employments a caller reaches with <paramref name="permission"/> (D38), as a query.</summary>
    public static async Task<IQueryable<EmploymentId>> VisibleEmploymentsAsync(
        IHrDbContext db,
        IAccessScopeProvider scopes,
        IPositionHolders holders,
        ICurrentUser currentUser,
        IOrganizationLookup organization,
        TimeProvider timeProvider,
        string permission,
        CancellationToken cancellationToken)
    {
        var scope = await EmployeeScope.ResolveAsync(scopes, permission, db, holders, currentUser, cancellationToken);
        var days = await CompanyDays.ResolveAsync(db, organization, timeProvider, cancellationToken);
        return scope.Filter(EmploymentRows.Query(db.Employees.AsNoTracking(), days), db).Select(r => r.EmploymentId);
    }

    public static LeaveRequestContact Contact(ILeaveRequestFields fields) =>
        new(fields.Note, fields.Address, fields.Phone, fields.SubstituteEmployeeId is { } substitute ? EmployeeId.From(substitute) : null);

    /// <summary>The approval facts of a leave (D44).</summary>
    public static async Task<JsonElement> FactsAsync(
        IHrDbContext db,
        EmployeeId employeeId,
        Employment employment,
        LeaveRequestPeriod period,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var assignment = employment.AssignmentOn(period.StartDate) ?? employment.LatestAssignment;
        var unitId = assignment is null
            ? null
            : (await db.Positions.AsNoTracking().Where(p => p.Id == assignment.PositionId).Select(p => p.OrgUnitId).ToListAsync(cancellationToken))
                .Select(u => (Guid?)u.Value).SingleOrDefault();
        return JsonSerializer.SerializeToElement(new
        {
            leaveTypeCode = period.LeaveTypeCode,
            days = period.Days,
            requesterEmployeeId = employeeId.Value,
            orgUnitId = unitId,
            branchId = assignment?.BranchId,
            companyId = employment.CompanyId,
            isPastDate = period.StartDate < today,
        });
    }

    /// <summary>"3,5 gün" or "2 saat" in the default culture, for summaries and notifications.</summary>
    public static string Length(ITextLocalizer localizer, DurationBasis basis, decimal quantity) =>
        localizer.Get(
            basis == DurationBasis.Hours ? "hr.leave_requests.length_hours" : "hr.leave_requests.length_days",
            new Dictionary<string, object?> { ["quantity"] = quantity.ToString("0.##", CultureInfo.GetCultureInfo(Culture)) },
            Culture);

    public static string Date(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    /// <summary>List rows of requests with the names of their people, types and companies.</summary>
    public static async Task<List<LeaveRequestListItem>> ItemsAsync(
        IHrDbContext db,
        IOrganizationLookup organization,
        ICurrentUser currentUser,
        IReadOnlyList<LeaveRequest> requests,
        CancellationToken cancellationToken)
    {
        var employeeIds = requests.Select(r => r.EmployeeId).Distinct().ToList();
        var names = await db.Employees.AsNoTracking()
            .Where(e => employeeIds.Contains(e.Id))
            .Select(e => new { e.Id, e.FirstName, e.LastName })
            .ToDictionaryAsync(e => e.Id, e => $"{e.FirstName} {e.LastName}", cancellationToken);
        var codes = requests.Select(r => r.LeaveTypeCode).Distinct().ToList();
        var types = (await db.LeaveTypes.AsNoTracking().Where(t => codes.Contains(t.Code)).ToListAsync(cancellationToken))
            .ToDictionary(t => t.Code, t => t.Name.Get(currentUser.Culture, Culture), StringComparer.Ordinal);
        var companies = (await organization.GetCompaniesAsync([.. requests.Select(r => r.CompanyId).Distinct()], cancellationToken))
            .ToDictionary(c => c.Id, c => c.ShortName);
        return
        [
            .. requests.Select(r => new LeaveRequestListItem(
                r.Id.Value,
                r.Number,
                r.EmployeeId.Value,
                names.GetValueOrDefault(r.EmployeeId, string.Empty),
                r.EmploymentId.Value,
                r.CompanyId,
                companies.GetValueOrDefault(r.CompanyId),
                r.LeaveTypeCode,
                types.GetValueOrDefault(r.LeaveTypeCode, r.LeaveTypeCode),
                r.StartDate,
                r.EndDate,
                r.StartPart,
                r.EndPart,
                r.StartTime,
                r.EndTime,
                r.TravelDays,
                r.Basis,
                r.Quantity,
                r.Days,
                r.Status,
                r.Origin,
                r.CreatedAt,
                r.SubmittedAt)),
        ];
    }

    /// <summary>A request with its details and files; <paramref name="actions"/> are the caller's.</summary>
    public static async Task<LeaveRequestDetails> DetailsAsync(
        IHrDbContext db,
        IOrganizationLookup organization,
        ICurrentUser currentUser,
        IFileStore fileStore,
        LeaveRequest request,
        LeaveRequestActions actions,
        CancellationToken cancellationToken)
    {
        var item = (await ItemsAsync(db, organization, currentUser, [request], cancellationToken)).Single();
        var substitute = request.SubstituteEmployeeId is { } substituteId
            ? await db.Employees.AsNoTracking().Where(e => e.Id == substituteId).Select(e => e.FirstName + " " + e.LastName).SingleOrDefaultAsync(cancellationToken)
            : null;
        var requiresDocument = await db.LeaveTypes.AsNoTracking().Where(t => t.Id == request.LeaveTypeId).Select(t => t.RequiresDocument).SingleOrDefaultAsync(cancellationToken);
        var files = await LeaveRequestFiles.Of(db, request.Id.Value).AsNoTracking()
            .OrderBy(f => f.CreatedAt)
            .Select(f => new LeaveRequestFileItem(f.Id, f.FileName, f.ContentType, f.Length, f.CreatedAt))
            .ToListAsync(cancellationToken);
        var rules = fileStore.AttachmentRules;
        return new LeaveRequestDetails(
            item,
            request.LeaveTypeId.Value,
            request.EventDate,
            request.DeductsFrom,
            request.Note,
            request.Address,
            request.Phone,
            request.SubstituteEmployeeId?.Value,
            substitute,
            request.ReturnComment,
            request.CancellationReason,
            request.CancellationRequestedAt,
            request.OriginalEndDate,
            request.DecidedAt,
            requiresDocument,
            files,
            [.. rules.Extensions.Order(StringComparer.Ordinal)],
            rules.MaxBytes,
            actions);
    }
}

/// <summary>Who hears of a leave request.</summary>
[Flags]
public enum LeaveRecipients
{
    None = 0,
    Requester = 1,
    Employee = 2,
    Substitute = 4,
}

/// <summary>Sends requests for approval and tells the people concerned what happened to them (D44).</summary>
public sealed class LeaveRequestNotifier(IHrDbContext db, IIntegrationEventPublisher publisher, ITextLocalizer localizer)
{
    private const string Culture = "tr-TR";

    public async Task RequestApprovalAsync(
        string documentType,
        LeaveRequest request,
        Employment employment,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var employee = await db.Employees.AsNoTracking().Where(e => e.Id == request.EmployeeId).Select(e => e.FirstName + " " + e.LastName).SingleAsync(cancellationToken);
        var typeName = await TypeNameAsync(request, cancellationToken);
        var titleKey = documentType == HrApprovalDocumentTypes.LeaveCancellation ? "hr.leave_requests.cancellation_title" : "hr.leave_requests.approval_title";
        await publisher.PublishAsync(
            new ApprovalRequested(
                documentType,
                request.Id.Value.ToString(),
                request.RequesterUserId,
                request.EmployeeId.Value,
                await LeaveRequestRules.FactsAsync(db, request.EmployeeId, employment, request.Period, today, cancellationToken),
                titleKey,
                new Dictionary<string, string> { ["number"] = request.Number, ["employee"] = employee },
                localizer.Get(
                    "hr.leave_requests.approval_summary",
                    new Dictionary<string, object?>
                    {
                        ["length"] = LeaveRequestRules.Length(localizer, request.Basis, request.Quantity),
                        ["type"] = typeName,
                        ["start"] = LeaveRequestRules.Date(request.StartDate),
                        ["end"] = LeaveRequestRules.Date(request.EndDate),
                    },
                    Culture),
                $"/hr/leave-requests/{request.Id.Value}")
            {
                CompanyId = request.CompanyId,
            },
            cancellationToken);
    }

    /// <summary>Notifies the audience of <paramref name="request"/>: the person's own user opens it in "İzinlerim", the others in HR.</summary>
    public async Task NotifyAsync(
        string type,
        LeaveRequest request,
        LeaveRecipients audience,
        IReadOnlyDictionary<string, string>? extraArgs,
        IReadOnlyCollection<Guid>? others,
        CancellationToken cancellationToken)
    {
        var employeeUser = await db.Employees.AsNoTracking().Where(e => e.Id == request.EmployeeId).Select(e => new { e.UserId, Name = e.FirstName + " " + e.LastName }).SingleAsync(cancellationToken);
        var recipients = new HashSet<Guid>(others ?? []);
        if (audience.HasFlag(LeaveRecipients.Requester))
        {
            recipients.Add(request.RequesterUserId);
        }

        if (audience.HasFlag(LeaveRecipients.Employee) && employeeUser.UserId is { } employeeUserId)
        {
            recipients.Add(employeeUserId);
        }

        if (audience.HasFlag(LeaveRecipients.Substitute) && request.SubstituteEmployeeId is { } substituteId
            && await db.Employees.AsNoTracking().Where(e => e.Id == substituteId).Select(e => e.UserId).SingleOrDefaultAsync(cancellationToken) is { } substitute)
        {
            recipients.Add(substitute);
        }

        if (recipients.Count == 0)
        {
            return;
        }

        var args = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["number"] = request.Number,
            ["employee"] = employeeUser.Name,
            ["type"] = await TypeNameAsync(request, cancellationToken),
            ["start"] = LeaveRequestRules.Date(request.StartDate),
            ["end"] = LeaveRequestRules.Date(request.EndDate),
            ["length"] = LeaveRequestRules.Length(localizer, request.Basis, request.Quantity),
        };
        foreach (var (key, value) in extraArgs ?? new Dictionary<string, string>())
        {
            args[key] = value;
        }

        var own = employeeUser.UserId is { } userId && recipients.Remove(userId) ? userId : (Guid?)null;
        if (own is { } self)
        {
            await PublishAsync(type, [self], args, $"/hr/me/leave?request={request.Id.Value}", request.CompanyId, cancellationToken);
        }

        if (recipients.Count > 0)
        {
            await PublishAsync(type, [.. recipients], args, $"/hr/leave-requests/{request.Id.Value}", request.CompanyId, cancellationToken);
        }
    }

    public string Status(LeaveRequestStatus status) => localizer.Get($"hr.leave_requests.status.{status}", culture: Culture);

    private Task PublishAsync(string type, IReadOnlyList<Guid> recipients, IReadOnlyDictionary<string, string> args, string link, Guid companyId, CancellationToken cancellationToken) =>
        publisher.PublishAsync(new NotificationRequested(type, recipients, args, link) { CompanyId = companyId }, cancellationToken);

    private async Task<string> TypeNameAsync(LeaveRequest request, CancellationToken cancellationToken) =>
        (await db.LeaveTypes.AsNoTracking().Where(t => t.Id == request.LeaveTypeId).Select(t => t.Name).SingleOrDefaultAsync(cancellationToken))?.Get(Culture, Culture)
            ?? request.LeaveTypeCode;
}

/// <summary>Creates, corrects and sends leave requests after the §6 checks (D44).</summary>
public sealed class LeaveRequestSubmitter(
    IHrDbContext db,
    LeaveRequestChecker checker,
    LeaveRequestNotifier notifier,
    INumberGenerator numbers,
    TimeProvider timeProvider)
{
    /// <summary>A new request of <paramref name="origin"/> Self or OnBehalf, sent at once when <paramref name="submit"/>.</summary>
    public async Task<Result<Guid>> CreateAsync(
        Employee employee,
        Employment employment,
        ILeaveRequestFields fields,
        LeaveRequestOrigin origin,
        Guid requesterUserId,
        bool submit,
        CancellationToken cancellationToken)
    {
        var check = await checker.CheckAsync(new LeaveCheckRequest(employee, employment, fields, origin, submit, null, 0), cancellationToken);
        if (!check.IsValid)
        {
            return check.Error!;
        }

        var number = await numbers.NextAsync(employment.CompanyId, HrNumberSequences.LeaveRequest, check.Today, cancellationToken);
        var request = LeaveRequest.Draft(number, employee.Id, employment.Id, employment.CompanyId, requesterUserId, origin, check.Period!, LeaveRequestRules.Contact(fields));
        db.LeaveRequests.Add(request);
        if (submit)
        {
            var sent = await SendAsync(request, employment, check, cancellationToken);
            if (sent.IsFailure)
            {
                return sent.Error;
            }
        }

        return request.Id.Value;
    }

    /// <summary>Corrects a draft or a returned request, and sends it when <paramref name="submit"/>.</summary>
    public async Task<Result> UpdateAsync(LeaveRequest request, ILeaveRequestFields fields, bool submit, CancellationToken cancellationToken)
    {
        var person = await LeaveRequestRules.EmploymentAsync(db, request.EmploymentId.Value, cancellationToken);
        if (person is not { } found)
        {
            return CommonErrors.NotFound;
        }

        var attachments = await LeaveRequestFiles.CountAsync(db, request.Id.Value, cancellationToken);
        var check = await checker.CheckAsync(new LeaveCheckRequest(found.Employee, found.Employment, fields, request.Origin, submit, request.Id, attachments), cancellationToken);
        if (!check.IsValid)
        {
            return check.Error!;
        }

        var updated = request.Update(check.Period!, LeaveRequestRules.Contact(fields));
        if (updated.IsFailure || !submit)
        {
            return updated;
        }

        return await SendAsync(request, found.Employment, check, cancellationToken);
    }

    /// <summary>Sends a checked request: to the approval engine, or approved at once for a type that needs none.</summary>
    private async Task<Result> SendAsync(LeaveRequest request, Employment employment, LeaveCheck check, CancellationToken cancellationToken)
    {
        var sent = request.Submit(check.Type!.RequiresApproval, timeProvider.GetUtcNow());
        if (sent.IsFailure)
        {
            return sent.Error;
        }

        if (sent.Value)
        {
            await notifier.RequestApprovalAsync(HrApprovalDocumentTypes.LeaveRequest, request, employment, check.Today, cancellationToken);
            await notifier.NotifyAsync(
                HrNotificationTypes.LeaveRequestSubmitted,
                request,
                LeaveRecipients.Requester | LeaveRecipients.Employee,
                null,
                null,
                cancellationToken);
        }
        else
        {
            await notifier.NotifyAsync(
                HrNotificationTypes.LeaveRequestCompleted,
                request,
                LeaveRecipients.Requester | LeaveRecipients.Employee | LeaveRecipients.Substitute,
                new Dictionary<string, string> { ["result"] = notifier.Status(request.Status) },
                null,
                cancellationToken);
        }

        return Result.Success();
    }
}

/// <summary>Checks a request without saving it, for the live form (D44).</summary>
public sealed class LeaveRequestPreviewer(IHrDbContext db, LeaveRequestChecker checker, IApprovalPreview approvals, ITextLocalizer localizer)
{
    public async Task<LeaveRequestPreview> PreviewAsync(
        Employee employee,
        Employment employment,
        ILeaveRequestFields fields,
        LeaveRequestOrigin origin,
        LeaveRequestId? requestId,
        Guid requesterUserId,
        CancellationToken cancellationToken)
    {
        var attachments = requestId is { } id ? await LeaveRequestFiles.CountAsync(db, id.Value, cancellationToken) : 0;
        var check = await checker.CheckAsync(new LeaveCheckRequest(employee, employment, fields, origin, false, requestId, attachments), cancellationToken);
        ApprovalPreview? approval = null;
        if (check.Period is { } period && check.Type is { RequiresApproval: true } && origin is LeaveRequestOrigin.Self or LeaveRequestOrigin.OnBehalf)
        {
            approval = await approvals.PreviewAsync(
                HrApprovalDocumentTypes.LeaveRequest,
                employment.CompanyId,
                await LeaveRequestRules.FactsAsync(db, employee.Id, employment, period, check.Today, cancellationToken),
                requesterUserId,
                employee.Id.Value,
                cancellationToken);
        }

        var duration = check.Duration;
        return new LeaveRequestPreview(
            check.Error is { } error ? Problem(error) : null,
            check.Warnings,
            duration?.StartDate,
            duration?.EndDate,
            check.Type?.DurationBasis,
            duration?.Duration.Quantity,
            check.Period?.Days ?? (check.Type?.DurationBasis == DurationBasis.Hours ? duration?.Duration.DayEquivalent : duration?.Duration.Quantity),
            duration?.Duration.Days ?? [],
            check.Type?.DeductsFrom,
            check.Available,
            check.AvailableAfter,
            check.Type?.RequiresApproval ?? false,
            check.Type?.RequiresDocument ?? false,
            approval);
    }

    private LeaveRequestProblem Problem(Error error) =>
        (error.Fields.Count > 0 ? error.Fields[0] : null) is { } field
            ? new LeaveRequestProblem(field.Code, localizer.Get(field.Code, field.Args), field.Field)
            : new LeaveRequestProblem(error.Code, localizer.Get(error.Code, error.Args), null);
}

/// <summary>
/// Who may see the approval timeline of a leave request or of its cancellation besides its approvers: the requester,
/// the person on leave and those who see the request with <c>hr.leave.requests.read</c> (D34, D44).
/// </summary>
public sealed class LeaveRequestAccessPolicy(IHrDbContext db, ICurrentUser currentUser, LeaveRequestAccess access) : IApprovalDocumentAccessPolicy
{
    public IReadOnlyCollection<string> DocumentTypes { get; } = [HrApprovalDocumentTypes.LeaveRequest, HrApprovalDocumentTypes.LeaveCancellation];

    public async Task<bool> CanReadAsync(string documentType, string documentId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(documentId, out var id))
        {
            return false;
        }

        var request = await db.LeaveRequests.AsNoTracking()
            .Where(r => r.Id == LeaveRequestId.From(id))
            .Select(r => new { r.EmployeeId, r.EmploymentId, r.RequesterUserId })
            .SingleOrDefaultAsync(cancellationToken);
        if (request is null)
        {
            return false;
        }

        if (request.RequesterUserId == currentUser.UserId || (currentUser.EmployeeId is { } own && request.EmployeeId == EmployeeId.From(own)))
        {
            return true;
        }

        return await access.ReachesAsync(HrPermissions.Leave.Requests.Read, request.EmploymentId, cancellationToken);
    }
}
