using FluentValidation;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Leave;
using Merp.Modules.Platform.Contracts.Calendars;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Leave;

/// <summary>What a leave of an employment covers, as the duration calculator needs it.</summary>
/// <param name="Employment">The employment taking the leave.</param>
/// <param name="Type">The leave type.</param>
/// <param name="StartDate">First day.</param>
/// <param name="EndDate">Last day; null for a type with a fixed length, which then gives the last day.</param>
/// <param name="StartPart">Full, or Afternoon for a leave starting at noon.</param>
/// <param name="EndPart">Full, or Morning for a leave ending at noon.</param>
/// <param name="StartTime">Start of an hourly leave on its first day.</param>
/// <param name="EndTime">End of an hourly leave on its last day.</param>
/// <param name="EventDate">The event of an event based leave (the birth); it chooses the type's version.</param>
public sealed record LeaveDurationRequest(
    Employment Employment,
    LeaveType Type,
    DateOnly StartDate,
    DateOnly? EndDate,
    LeaveDayPart StartPart = LeaveDayPart.Full,
    LeaveDayPart EndPart = LeaveDayPart.Full,
    TimeOnly? StartTime = null,
    TimeOnly? EndTime = null,
    DateOnly? EventDate = null);

/// <summary>A counted leave with its range and the schedule it was counted on.</summary>
public sealed record LeaveDurationResult(DateOnly StartDate, DateOnly EndDate, Guid ScheduleId, LeaveDuration Duration);

/// <summary>
/// The length of a leave on the employee's work schedule (docs/domain/izin-kurallari.md §4, D42): the employment's own
/// schedule, else its branch's, else its company's, else the tenant's default.
/// </summary>
public interface ILeaveDurationCalculator
{
    Task<Result<LeaveDurationResult>> CalculateAsync(LeaveDurationRequest request, CancellationToken cancellationToken = default);
}

public static class LeaveDurationRequestErrors
{
    public const string NoScheduleCode = "hr.leave.errors.no_work_schedule";
    public const string EndDateRequiredCode = "hr.leave.errors.end_date_required";
    public const string RangeTooLongCode = "hr.leave.errors.range_too_long";
}

public sealed class LeaveDurationCalculator(IWorkingCalendar calendar) : ILeaveDurationCalculator
{
    public async Task<Result<LeaveDurationResult>> CalculateAsync(LeaveDurationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var employment = request.Employment;
        var branchId = (employment.AssignmentOn(request.StartDate) ?? employment.LatestAssignment)?.BranchId;
        var scheduleId = employment.WorkScheduleId ?? await calendar.ResolveScheduleIdAsync(employment.CompanyId, branchId, cancellationToken);
        if (scheduleId is not { } schedule)
        {
            return Error.Validation([new FieldError("employmentId", LeaveDurationRequestErrors.NoScheduleCode)]);
        }

        var end = request.EndDate;
        if (end is null)
        {
            var length = request.Type.VersionOn(request.EventDate ?? request.StartDate)?.FixedDays;
            if (length is not > 0)
            {
                return Error.Validation([new FieldError("endDate", LeaveDurationRequestErrors.EndDateRequiredCode)]);
            }

            var ahead = await calendar.GetDaysAsync(schedule, request.StartDate, request.StartDate.AddDays(LeaveDurationErrors.MaxDays - 1), cancellationToken);
            end = LeaveDurations.EndDate(request.Type.DurationBasis, ahead, length.Value);
            if (end is null)
            {
                return Error.Validation([new FieldError("startDate", LeaveDurationRequestErrors.RangeTooLongCode)]);
            }
        }

        if (end.Value.DayNumber - request.StartDate.DayNumber >= LeaveDurationErrors.MaxDays)
        {
            return Error.Validation([new FieldError("endDate", LeaveDurationRequestErrors.RangeTooLongCode)]);
        }

        var days = await calendar.GetDaysAsync(schedule, request.StartDate, end.Value, cancellationToken);
        var counted = LeaveDurations.Count(request.Type.DurationBasis, days, request.StartPart, request.EndPart, request.StartTime, request.EndTime);
        return counted.IsFailure ? counted.Error : new LeaveDurationResult(request.StartDate, end.Value, schedule, counted.Value);
    }
}

/// <summary>
/// The length of a leave before it is asked for (PLAN L2): an employment in the scope of <c>hr.employees.read</c> (D42).
/// </summary>
public sealed record PreviewLeaveDurationQuery(
    Guid EmploymentId,
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly? EndDate = null,
    LeaveDayPart StartPart = LeaveDayPart.Full,
    LeaveDayPart EndPart = LeaveDayPart.Full,
    TimeOnly? StartTime = null,
    TimeOnly? EndTime = null,
    DateOnly? EventDate = null) : IQuery<LeaveDurationPreview>, IRequirePermission
{
    public string Permission => HrPermissions.Employees.Read;
}

/// <remarks>
/// Quantity is in days, or in hours for an hourly leave (DayEquivalent then gives the days); Days holds each day's
/// share. EndDate is computed for a type with a fixed length when none was given.
/// </remarks>
public sealed record LeaveDurationPreview(
    DateOnly StartDate,
    DateOnly EndDate,
    DurationBasis Basis,
    decimal Quantity,
    decimal? DayEquivalent,
    IReadOnlyList<LeaveDurationDay> Days);

internal sealed class PreviewLeaveDurationQueryValidator : AbstractValidator<PreviewLeaveDurationQuery>
{
    public PreviewLeaveDurationQueryValidator()
    {
        RuleFor(q => q.EmploymentId).NotEmpty();
        RuleFor(q => q.LeaveTypeId).NotEmpty();
        RuleFor(q => q.EndDate).GreaterThanOrEqualTo(q => q.StartDate).WithErrorCode(CommonErrors.InvalidDateRange.Code).When(q => q.EndDate is not null);
        RuleFor(q => q.StartPart).Must(p => p is LeaveDayPart.Full or LeaveDayPart.Afternoon).WithErrorCode(CommonErrors.ValidationCode);
        RuleFor(q => q.EndPart).Must(p => p is LeaveDayPart.Full or LeaveDayPart.Morning).WithErrorCode(CommonErrors.ValidationCode);
        RuleFor(q => q.EndTime).GreaterThan(q => q.StartTime).WithErrorCode(CommonErrors.InvalidDateRange.Code)
            .When(q => q.StartTime is not null && q.EndTime is not null && (q.EndDate is null || q.EndDate == q.StartDate));
    }
}

internal sealed class PreviewLeaveDurationQueryHandler(
    IHrDbContext db,
    IAccessScopeProvider scopes,
    ILeaveDurationCalculator calculator) : IQueryHandler<PreviewLeaveDurationQuery, LeaveDurationPreview>
{
    public async Task<Result<LeaveDurationPreview>> Handle(PreviewLeaveDurationQuery request, CancellationToken cancellationToken)
    {
        var employmentId = EmploymentId.From(request.EmploymentId);
        var employee = await db.Employees.AsNoTracking()
            .Include(e => e.Employments)
            .ThenInclude(m => m.Assignments)
            .SingleOrDefaultAsync(e => e.Employments.Any(m => m.Id == employmentId), cancellationToken);
        var employment = employee?.Employments.Single(m => m.Id == employmentId);

        // Outside the caller's scope looks the same as missing
        if (employment is null || !(await OrgScope.ForCompaniesAsync(scopes, request.Permission, cancellationToken)).CoversCompany(employment.CompanyId))
        {
            return CommonErrors.NotFound;
        }

        var type = await db.LeaveTypes.AsNoTracking().SingleOrDefaultAsync(t => t.Id == LeaveTypeId.From(request.LeaveTypeId), cancellationToken);
        if (type is null || !type.IsActive || (type.CompanyId is not null && type.CompanyId != employment.CompanyId))
        {
            return Error.Validation([new FieldError("leaveTypeId", LeaveSettingsErrors.UnknownLeaveTypeCode)]);
        }

        var result = await calculator.CalculateAsync(
            new LeaveDurationRequest(employment, type, request.StartDate, request.EndDate, request.StartPart, request.EndPart, request.StartTime, request.EndTime, request.EventDate),
            cancellationToken);
        if (result.IsFailure)
        {
            return result.Error;
        }

        var duration = result.Value.Duration;
        return new LeaveDurationPreview(result.Value.StartDate, result.Value.EndDate, type.DurationBasis, duration.Quantity, duration.DayEquivalent, duration.Days);
    }
}
