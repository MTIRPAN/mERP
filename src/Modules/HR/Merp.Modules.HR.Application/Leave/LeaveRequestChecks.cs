using FluentValidation;
using Merp.Application.Abstractions.Localization;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Leave;
using Merp.Modules.Platform.Contracts.Approvals;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Leave;

/// <summary>What a leave request form gives (D44).</summary>
public interface ILeaveRequestFields
{
    Guid? LeaveTypeId { get; }

    DateOnly StartDate { get; }

    /// <summary>Last day; null for a type with a fixed length, which then gives it.</summary>
    DateOnly? EndDate { get; }

    /// <summary>Full, or Afternoon for a leave starting at noon.</summary>
    LeaveDayPart StartPart { get; }

    /// <summary>Full, or Morning for a leave ending at noon.</summary>
    LeaveDayPart EndPart { get; }

    /// <summary>Start of an hourly leave.</summary>
    TimeOnly? StartTime { get; }

    /// <summary>End of an hourly leave.</summary>
    TimeOnly? EndTime { get; }

    /// <summary>The event of an event based leave (the birth); it chooses the type's version.</summary>
    DateOnly? EventDate { get; }

    /// <summary>Calendar days of travel after an annual leave (yol izni).</summary>
    int TravelDays { get; }

    string? Note { get; }

    /// <summary>Address during the leave.</summary>
    string? Address { get; }

    /// <summary>Phone during the leave.</summary>
    string? Phone { get; }

    /// <summary>The colleague who stands in (vekil).</summary>
    Guid? SubstituteEmployeeId { get; }
}

/// <summary>Something the request may go on with, e.g. a short notice entered by HR.</summary>
/// <param name="Code">The warning's key.</param>
/// <param name="Message">Its text.</param>
public sealed record LeaveRequestWarning(string Code, string Message);

/// <summary>What stops the request.</summary>
/// <param name="Code">The error's key.</param>
/// <param name="Message">Its text.</param>
/// <param name="Field">The form field it belongs to.</param>
public sealed record LeaveRequestProblem(string Code, string Message, string? Field);

/// <summary>
/// A leave request checked before it is saved (D44): its length and days, the balance before and after it, the
/// first problem that stops it, the warnings and who would approve it.
/// </summary>
/// <remarks>
/// Quantity is in days, or in hours for an hourly leave; Days is the length in days. Available and AvailableAfter
/// are those of the balance the type uses (DeductsFrom), null for a type without one. Approval is null for a
/// recorded leave and when the request stops before the flow can be chosen.
/// </remarks>
public sealed record LeaveRequestPreview(
    LeaveRequestProblem? Problem,
    IReadOnlyList<LeaveRequestWarning> Warnings,
    DateOnly? StartDate,
    DateOnly? EndDate,
    DurationBasis? Basis,
    decimal? Quantity,
    decimal? Days,
    IReadOnlyList<LeaveDurationDay> DurationDays,
    string? DeductsFrom,
    decimal? Available,
    decimal? AvailableAfter,
    bool RequiresApproval,
    bool RequiresDocument,
    ApprovalPreview? Approval);

/// <summary>Form values of a stored request, to check it again when it is sent.</summary>
internal sealed record LeaveRequestFields(
    Guid? LeaveTypeId,
    DateOnly StartDate,
    DateOnly? EndDate,
    LeaveDayPart StartPart,
    LeaveDayPart EndPart,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    DateOnly? EventDate,
    int TravelDays,
    string? Note,
    string? Address,
    string? Phone,
    Guid? SubstituteEmployeeId) : ILeaveRequestFields
{
    public static LeaveRequestFields Of(LeaveRequest request) =>
        new(
            request.LeaveTypeId.Value,
            request.StartDate,
            request.EndDate,
            request.StartPart,
            request.EndPart,
            request.StartTime,
            request.EndTime,
            request.EventDate,
            request.TravelDays,
            request.Note,
            request.Address,
            request.Phone,
            request.SubstituteEmployeeId?.Value);

    public static LeaveRequestFields Of(ILeaveRequestFields fields) =>
        new(
            fields.LeaveTypeId,
            fields.StartDate,
            fields.EndDate,
            fields.StartPart,
            fields.EndPart,
            fields.StartTime,
            fields.EndTime,
            fields.EventDate,
            fields.TravelDays,
            fields.Note,
            fields.Address,
            fields.Phone,
            fields.SubstituteEmployeeId);
}

public static class LeaveRequestCheckErrors
{
    public const string NoEmploymentCode = "hr.leave.errors.no_active_employment";
    public const string NotRequestableCode = "hr.leave.errors.type_not_requestable";
    public const string NotAllowedCode = "hr.leave.errors.type_not_allowed";
    public const string HalfDayNotAllowedCode = "hr.leave.errors.half_day_not_allowed";
    public const string TravelDaysNotAllowedCode = "hr.leave.errors.travel_days_not_allowed";
    public const string TooManyTravelDaysCode = "hr.leave.errors.too_many_travel_days";
    public const string PastDateNotAllowedCode = "hr.leave.errors.past_date_not_allowed";
    public const string MaxPerRequestCode = "hr.leave.errors.max_per_request";
    public const string MaxPerYearCode = "hr.leave.errors.max_per_year";
    public const string OverlappingCode = "hr.leave.errors.overlapping_request";
    public const string MinNoticeCode = "hr.leave.errors.min_notice";
    public const string DocumentRequiredCode = "hr.leave.errors.document_required";
    public const string TenDayBlockCode = "hr.leave.errors.ten_day_block";
    public const string UnknownSubstituteCode = "hr.leave.errors.unknown_substitute";

    public const int MaxTravelDays = 30;
}

/// <summary>The checks of a request in progress.</summary>
/// <param name="Employee">The person on leave, with employments and assignments.</param>
/// <param name="Employment">The employment taking the leave.</param>
/// <param name="Fields">The form.</param>
/// <param name="Origin">Who asks: the employee is held to the rules HR is only warned of.</param>
/// <param name="Submitting">The request is being sent: a missing document stops the employee.</param>
/// <param name="RequestId">The request being corrected, left out of overlaps and reservations.</param>
/// <param name="Attachments">Files attached to it.</param>
public sealed record LeaveCheckRequest(
    Employee Employee,
    Employment Employment,
    ILeaveRequestFields Fields,
    LeaveRequestOrigin Origin,
    bool Submitting,
    LeaveRequestId? RequestId,
    int Attachments);

/// <summary>The outcome of the checks: the counted leave, or the first error, and the warnings.</summary>
public sealed record LeaveCheck(
    DateOnly Today,
    LeaveType? Type,
    LeaveRequestPeriod? Period,
    LeaveDurationResult? Duration,
    Error? Error,
    IReadOnlyList<LeaveRequestWarning> Warnings,
    decimal? Available,
    decimal? AvailableAfter)
{
    public bool IsValid => Error is null;

    public bool IsPast => Period is not null && Period.StartDate < Today;
}

internal static class LeaveRequestStatuses
{
    /// <summary>Requests that hold their days: asked for and not refused, or approved.</summary>
    public static readonly LeaveRequestStatus[] Blocking =
        [LeaveRequestStatus.Pending, LeaveRequestStatus.Returned, LeaveRequestStatus.Approved, LeaveRequestStatus.CancellationPending];

    /// <summary>Requests whose days are reserved on the balance (§5).</summary>
    public static readonly LeaveRequestStatus[] Reserving = [LeaveRequestStatus.Pending, LeaveRequestStatus.Returned];
}

internal static class LeaveRequestFieldRules
{
    public static void Add<T>(AbstractValidator<T> validator)
        where T : ILeaveRequestFields
    {
        validator.RuleFor(f => f.LeaveTypeId).NotEmpty();
        validator.RuleFor(f => f.StartDate).NotEmpty();
        validator.RuleFor(f => f.EndDate).GreaterThanOrEqualTo(f => f.StartDate).WithErrorCode(CommonErrors.InvalidDateRange.Code).When(f => f.EndDate is not null);
        validator.RuleFor(f => f.StartPart).Must(p => p is LeaveDayPart.Full or LeaveDayPart.Afternoon).WithErrorCode(CommonErrors.ValidationCode);
        validator.RuleFor(f => f.EndPart).Must(p => p is LeaveDayPart.Full or LeaveDayPart.Morning).WithErrorCode(CommonErrors.ValidationCode);
        validator.RuleFor(f => f.EndTime).GreaterThan(f => f.StartTime).WithErrorCode(CommonErrors.InvalidDateRange.Code)
            .When(f => f.StartTime is not null && f.EndTime is not null && (f.EndDate is null || f.EndDate == f.StartDate));
        validator.RuleFor(f => f.TravelDays).InclusiveBetween(0, LeaveRequestCheckErrors.MaxTravelDays);
        validator.RuleFor(f => f.Note).MaximumLength(LeaveRequest.NoteMaxLength);
        validator.RuleFor(f => f.Address).MaximumLength(LeaveRequest.AddressMaxLength);
        validator.RuleFor(f => f.Phone).MaximumLength(LeaveRequest.PhoneMaxLength);
    }
}

/// <summary>
/// The checks of a leave request in the order of docs/domain/izin-kurallari.md §6 (D44): the first error stops,
/// warnings are collected. The employee is held to the notice, the document and the ten day rule HR is only warned
/// of; recorded leaves may be in the past.
/// </summary>
public sealed class LeaveRequestChecker(
    IHrDbContext db,
    ILeaveDurationCalculator calculator,
    LeavePolicyResolver resolver,
    IOrganizationLookup organization,
    ITextLocalizer localizer,
    TimeProvider timeProvider)
{
    public async Task<LeaveCheck> CheckAsync(LeaveCheckRequest request, CancellationToken cancellationToken)
    {
        var fields = request.Fields;
        var employee = request.Employee;
        var employment = request.Employment;
        var byHr = request.Origin != LeaveRequestOrigin.Self;
        var recorded = request.Origin is LeaveRequestOrigin.Record or LeaveRequestOrigin.Bulk;
        var today = await LeaveTypeRules.TodayAsync(organization, timeProvider, employment.CompanyId, cancellationToken);
        var warnings = new List<LeaveRequestWarning>();
        LeaveType? type = null;
        LeaveDurationResult? duration = null;
        LeaveCheck Stop(Error error) => new(today, type, null, duration, error, warnings, null, null);
        void Warn(string code, IReadOnlyDictionary<string, object?>? args = null) => warnings.Add(new LeaveRequestWarning(code, localizer.Get(code, args)));

        type = fields.LeaveTypeId is { } typeId
            ? await db.LeaveTypes.AsNoTracking().SingleOrDefaultAsync(t => t.Id == LeaveTypeId.From(typeId), cancellationToken)
            : null;
        if (type is null || !type.IsActive || (type.CompanyId is not null && type.CompanyId != employment.CompanyId))
        {
            return Stop(Field("leaveTypeId", LeaveSettingsErrors.UnknownLeaveTypeCode));
        }

        // 1. An employment open on the first day
        if (!Covers(employment, fields.StartDate))
        {
            return Stop(Field("employmentId", LeaveRequestCheckErrors.NoEmploymentCode));
        }

        // 2. A type the person may ask for, in the parts it allows
        if (type.Category is LeaveCategory.Arrangement or LeaveCategory.Travel)
        {
            return Stop(Field("leaveTypeId", LeaveRequestCheckErrors.NotRequestableCode));
        }

        if (type.GenderRestriction is { } gender && employee.Gender != gender)
        {
            return Stop(Field("leaveTypeId", LeaveRequestCheckErrors.NotAllowedCode));
        }

        var annual = type.Category == LeaveCategory.Annual;
        var rules = (await resolver.ChooseAsync(employee, employment, fields.StartDate, cancellationToken)).Policy?.VersionOn(fields.StartDate)?.Rules;
        var halfDay = fields.StartPart != LeaveDayPart.Full || fields.EndPart != LeaveDayPart.Full;
        if (halfDay && (type.Unit != LeaveUnit.HalfDay || (annual && rules is { AllowHalfDay: false })))
        {
            return Stop(Field("startPart", LeaveRequestCheckErrors.HalfDayNotAllowedCode));
        }

        if (fields.TravelDays > 0)
        {
            if (!annual || rules is not { AllowTravelDays: true })
            {
                return Stop(Field("travelDays", LeaveRequestCheckErrors.TravelDaysNotAllowedCode));
            }

            if (fields.TravelDays > rules.MaxTravelDays)
            {
                return Stop(Field("travelDays", LeaveRequestCheckErrors.TooManyTravelDaysCode, ("max", rules.MaxTravelDays)));
            }
        }

        // 3. Past days only for HR, when the type and the policy allow them; recorded leaves are past by nature
        if (fields.StartDate < today && !recorded && (!byHr || !type.AllowPastDates || (annual && rules is { AllowPastDatesForHr: false })))
        {
            return Stop(Field("startDate", LeaveRequestCheckErrors.PastDateNotAllowedCode));
        }

        // 4. A length above zero, within the limits of a request and of a year
        var hourly = type.DurationBasis == DurationBasis.Hours;
        var counted = await calculator.CalculateAsync(
            new LeaveDurationRequest(
                employment,
                type,
                fields.StartDate,
                fields.EndDate,
                hourly ? LeaveDayPart.Full : fields.StartPart,
                hourly ? LeaveDayPart.Full : fields.EndPart,
                hourly ? fields.StartTime : null,
                hourly ? fields.EndTime : null,
                fields.EventDate),
            cancellationToken);
        if (counted.IsFailure)
        {
            return Stop(counted.Error);
        }

        duration = counted.Value;
        if (!Covers(employment, duration.EndDate.AddDays(fields.TravelDays)))
        {
            return Stop(Field("employmentId", LeaveRequestCheckErrors.NoEmploymentCode));
        }

        var days = hourly ? duration.Duration.DayEquivalent ?? 0 : duration.Duration.Quantity;
        var version = type.VersionOn(fields.EventDate ?? fields.StartDate);
        var perRequest = new[] { version?.MaxDaysPerRequest, annual ? rules?.MaxDaysPerRequest : null }.Where(m => m is not null).Min();
        if (perRequest is { } maxPerRequest && days > maxPerRequest)
        {
            return Stop(Field("endDate", LeaveRequestCheckErrors.MaxPerRequestCode, ("max", maxPerRequest)));
        }

        // No request has the empty id
        var excluded = request.RequestId ?? default;
        var others = await db.LeaveRequests.AsNoTracking()
            .Where(r => r.EmployeeId == employee.Id && LeaveRequestStatuses.Blocking.Contains(r.Status) && r.Id != excluded)
            .ToListAsync(cancellationToken);
        var sameYear = others.Where(r => r.EmploymentId == employment.Id && r.LeaveTypeCode == type.Code && r.StartDate.Year == fields.StartDate.Year).ToList();
        if (version?.MaxDaysPerYear is { } maxPerYear && sameYear.Sum(r => r.Days) + days > maxPerYear)
        {
            return Stop(Field("endDate", LeaveRequestCheckErrors.MaxPerYearCode, ("max", maxPerYear), ("used", sameYear.Sum(r => r.Days))));
        }

        var period = new LeaveRequestPeriod(
            type.Id,
            type.Code,
            type.DeductsFrom,
            duration.StartDate,
            duration.EndDate,
            hourly ? LeaveDayPart.Full : fields.StartPart,
            hourly ? LeaveDayPart.Full : fields.EndPart,
            hourly ? fields.StartTime : null,
            hourly ? fields.EndTime : null,
            fields.EventDate,
            fields.TravelDays,
            type.DurationBasis,
            duration.Duration.Quantity,
            days);

        // 5. No other leave of the person at the same time (E13)
        if (others.FirstOrDefault(r => r.Period.Overlaps(period)) is { } clash)
        {
            return Stop(Field("startDate", LeaveRequestCheckErrors.OverlappingCode, ("number", clash.Number)));
        }

        // 6. The notice period: an error for the employee, a warning for HR
        var notice = version?.MinNoticeDays ?? (annual ? rules?.MinNoticeDays : null);
        if (notice is > 0 && fields.StartDate.DayNumber - today.DayNumber < notice)
        {
            if (!byHr)
            {
                return Stop(Field("startDate", LeaveRequestCheckErrors.MinNoticeCode, ("days", notice)));
            }

            Warn(LeaveRequestCheckErrors.MinNoticeCode, Args(("days", notice)));
        }

        // 7. The balance, less the requests waiting, at most the advance below zero (E1, E10)
        decimal? available = null;
        decimal? after = null;
        if (type.DeductsFrom is { } code)
        {
            var account = await db.LeaveAccounts.AsNoTracking().Include(a => a.Entries)
                .SingleOrDefaultAsync(a => a.EmploymentId == employment.Id && a.LeaveTypeCode == code, cancellationToken);
            if (account?.ClosedOn is not null)
            {
                return Stop(LeaveAccountErrors.Closed);
            }

            var pending = others.Where(r => r.EmploymentId == employment.Id && r.DeductsFrom == code && r.IsOpen).Sum(r => r.Days);
            available = (account?.Balance ?? 0) - pending;
            after = available - days;
            var advance = rules?.AdvanceLimitDays ?? 0;
            if (rules is { AllowBeforeFirstEntitlement: false } && (account?.Earned ?? 0) <= 0)
            {
                advance = 0;
            }

            if (after < -advance)
            {
                return Stop(LeaveAccountErrors.InsufficientBalance);
            }
        }

        // 8. The document a type asks for (user decision): required when the employee sends the request
        if (type.RequiresDocument && request.Attachments == 0)
        {
            if (request.Submitting && !byHr)
            {
                return Stop(Field("attachments", LeaveRequestCheckErrors.DocumentRequiredCode));
            }

            Warn(LeaveRequestCheckErrors.DocumentRequiredCode);
        }

        // 9. The ten day rule: a short annual leave must not use up the days of the ten day part (4857 md. 56)
        if (annual && rules is { TenDayBlockRule: not TenDayBlockRule.Off } && days < LeaveRequest.TenDayBlock
            && available >= LeaveRequest.TenDayBlock && after < LeaveRequest.TenDayBlock
            && !sameYear.Any(r => r.Days >= LeaveRequest.TenDayBlock))
        {
            if (rules.TenDayBlockRule == TenDayBlockRule.Enforce && !byHr)
            {
                return Stop(Field("endDate", LeaveRequestCheckErrors.TenDayBlockCode));
            }

            Warn(LeaveRequestCheckErrors.TenDayBlockCode);
        }

        if (fields.SubstituteEmployeeId is { } substitute
            && (substitute == employee.Id.Value || !await db.Employees.AnyAsync(e => e.Id == EmployeeId.From(substitute), cancellationToken)))
        {
            return Stop(Field("substituteEmployeeId", LeaveRequestCheckErrors.UnknownSubstituteCode));
        }

        return new LeaveCheck(today, type, period, duration, null, warnings, available, after);
    }

    /// <summary>The employment is open on the day: started and not ended before it.</summary>
    private static bool Covers(Employment employment, DateOnly day) =>
        employment.HireDate <= day && (employment.TerminationDate is not { } end || day <= end);

    private static Error Field(string field, string code, params (string Name, object? Value)[] args) =>
        Error.Validation([new FieldError(field, code, Args(args))]);

    private static Dictionary<string, object?> Args(params (string Name, object? Value)[] args) =>
        args.ToDictionary(a => a.Name, a => a.Value, StringComparer.Ordinal);
}
