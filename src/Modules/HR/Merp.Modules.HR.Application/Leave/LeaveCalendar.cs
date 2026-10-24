using FluentValidation;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Settings;
using Merp.Modules.HR.Application.Employees;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Domain.Leave;
using Merp.Modules.HR.Domain.Organization;
using Merp.Modules.Platform.Contracts.Calendars;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Leave;

/// <summary>
/// The leave calendar of the employees in the caller's scope (PLAN L5, D45): approved leaves and, unless left out,
/// those waiting for approval, between two days; narrowed to a company, a unit (with its sub units) or the caller's
/// team. Managers hold the permission for their team, so they see only their people.
/// </summary>
public sealed record GetLeaveCalendarQuery(
    DateOnly From,
    DateOnly To,
    Guid? CompanyId = null,
    Guid? OrgUnitId = null,
    bool IncludeSubUnits = true,
    bool MyTeam = false,
    bool IncludePending = true) : IQuery<LeaveCalendar>, IRequirePermission
{
    /// <summary>The longest range asked at once: a month view with its leading and trailing weeks, or a quarter.</summary>
    public const int MaxDays = 100;

    public string Permission => HrPermissions.Leave.Calendar.Read;
}

/// <summary>A leave on the calendar; EndDate is its last day, the travel days follow it.</summary>
public sealed record LeaveCalendarEvent(
    Guid RequestId,
    string Number,
    Guid EmployeeId,
    string EmployeeName,
    Guid CompanyId,
    Guid? OrgUnitId,
    string? OrgUnitName,
    string LeaveTypeCode,
    string LeaveTypeName,
    LeaveCategory Category,
    DateOnly StartDate,
    DateOnly EndDate,
    LeaveDayPart StartPart,
    LeaveDayPart EndPart,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    int TravelDays,
    LeaveRequestStatus Status);

/// <param name="Date">The day.</param>
/// <param name="Name">The holiday's name.</param>
/// <param name="HalfDay">Only its afternoon is off (arife, 28 Ekim).</param>
public sealed record LeaveCalendarHoliday(DateOnly Date, string Name, bool HalfDay);

/// <summary>More people of a unit are away on a day than the company's threshold allows.</summary>
public sealed record LeaveCalendarAlert(DateOnly Date, Guid OrgUnitId, string OrgUnitName, int Count, int Threshold);

/// <summary>The leaves, the public holidays and the crowded days of a range.</summary>
public sealed record LeaveCalendar(
    IReadOnlyList<LeaveCalendarEvent> Events,
    IReadOnlyList<LeaveCalendarHoliday> Holidays,
    IReadOnlyList<LeaveCalendarAlert> Alerts);

internal sealed class GetLeaveCalendarQueryValidator : AbstractValidator<GetLeaveCalendarQuery>
{
    public GetLeaveCalendarQueryValidator()
    {
        RuleFor(q => q.From).NotEmpty();
        RuleFor(q => q.To).GreaterThanOrEqualTo(q => q.From).WithErrorCode(CommonErrors.InvalidDateRange.Code);
        RuleFor(q => q.To)
            .Must((q, to) => to.DayNumber - q.From.DayNumber < GetLeaveCalendarQuery.MaxDays)
            .WithErrorCode("hr.leave.errors.calendar_range_too_long")
            .When(q => q.To >= q.From);
    }
}

internal sealed class GetLeaveCalendarQueryHandler(
    IHrDbContext db,
    IAccessScopeProvider scopes,
    IPositionHolders holders,
    ICurrentUser currentUser,
    IOrganizationLookup organization,
    IWorkingCalendar calendar,
    ISettings settings,
    TimeProvider timeProvider) : IQueryHandler<GetLeaveCalendarQuery, LeaveCalendar>
{
    private static readonly LeaveRequestStatus[] Approved = [LeaveRequestStatus.Approved, LeaveRequestStatus.CancellationPending];

    public async Task<Result<LeaveCalendar>> Handle(GetLeaveCalendarQuery request, CancellationToken cancellationToken)
    {
        var scope = await EmployeeScope.ResolveAsync(scopes, request.Permission, db, holders, currentUser, cancellationToken);
        var days = await CompanyDays.ResolveAsync(db, organization, timeProvider, cancellationToken);
        var rows = scope.Filter(EmploymentRows.Query(db.Employees.AsNoTracking(), days), db);
        if (request.CompanyId is { } companyId)
        {
            rows = rows.Where(r => r.CompanyId == companyId);
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

        if (request.MyTeam)
        {
            var own = currentUser.UserId is { } userId
                ? (await holders.GetPositionsOfUserAsync(userId, cancellationToken)).Select(h => h.PositionId).Distinct().ToList()
                : [];
            var team = await db.ReportingLines.AsNoTracking()
                .Where(r => own.Contains(r.ManagerPositionId))
                .Select(r => r.SubordinatePositionId)
                .Distinct()
                .ToListAsync(cancellationToken);
            rows = rows.Where(r => (r.PositionId != null && team.Contains(r.PositionId.Value)) || (r.ManagerPositionId != null && own.Contains(r.ManagerPositionId.Value)));
        }

        var people = await rows.Select(r => new { r.EmploymentId, r.EmployeeId, r.FirstName, r.LastName, r.PositionId, r.CompanyId }).ToListAsync(cancellationToken);
        var employmentIds = people.Select(p => p.EmploymentId).ToList();
        var statuses = request.IncludePending ? LeaveRequestStatuses.Blocking : Approved;

        // Travel days follow the last day: a leave ending up to a month before the range may still reach into it
        var earliestEnd = request.From.AddDays(-LeaveRequestCheckErrors.MaxTravelDays);
        var leaves = (await db.LeaveRequests.AsNoTracking()
                .Where(r => employmentIds.Contains(r.EmploymentId) && statuses.Contains(r.Status) && r.StartDate <= request.To && r.EndDate >= earliestEnd)
                .ToListAsync(cancellationToken))
            .Where(r => r.Period.AwayUntil >= request.From)
            .OrderBy(r => r.StartDate)
            .ToList();

        var positionIds = people.Where(p => p.PositionId is not null).Select(p => p.PositionId!.Value).Distinct().ToList();
        var unitOf = await db.Positions.AsNoTracking()
            .Where(p => positionIds.Contains(p.Id))
            .Join(db.OrgUnits.AsNoTracking(), p => p.OrgUnitId, u => u.Id, (p, u) => new { p.Id, UnitId = u.Id, u.Name })
            .ToDictionaryAsync(x => x.Id, x => (x.UnitId, x.Name), cancellationToken);
        var codes = leaves.Select(r => r.LeaveTypeCode).Distinct().ToList();
        var types = (await db.LeaveTypes.AsNoTracking().Where(t => codes.Contains(t.Code)).ToListAsync(cancellationToken))
            .ToDictionary(t => t.Code, StringComparer.Ordinal);
        var byEmployment = people.ToDictionary(p => p.EmploymentId);

        var events = leaves.Select(r =>
        {
            var person = byEmployment[r.EmploymentId];
            (OrgUnitId UnitId, string Name)? unit = person.PositionId is { } position && unitOf.TryGetValue(position, out var found) ? found : null;
            var type = types.GetValueOrDefault(r.LeaveTypeCode);
            return new LeaveCalendarEvent(
                r.Id.Value,
                r.Number,
                r.EmployeeId.Value,
                $"{person.FirstName} {person.LastName}",
                r.CompanyId,
                unit?.UnitId.Value,
                unit?.Name,
                r.LeaveTypeCode,
                type?.Name.Get(currentUser.Culture, "tr-TR") ?? r.LeaveTypeCode,
                type?.Category ?? LeaveCategory.Annual,
                r.StartDate,
                r.EndDate,
                r.StartPart,
                r.EndPart,
                r.StartTime,
                r.EndTime,
                r.TravelDays,
                r.Status);
        }).ToList();

        return new LeaveCalendar(
            events,
            await HolidaysAsync(request, people.Select(p => p.CompanyId).FirstOrDefault(), cancellationToken),
            await AlertsAsync(request, events, cancellationToken));
    }

    /// <summary>The public holidays of the company asked for, else of the first company shown.</summary>
    private async Task<IReadOnlyList<LeaveCalendarHoliday>> HolidaysAsync(GetLeaveCalendarQuery request, Guid firstCompany, CancellationToken cancellationToken)
    {
        var companyId = request.CompanyId ?? firstCompany;
        if (companyId == Guid.Empty || await calendar.ResolveScheduleIdAsync(companyId, null, cancellationToken) is not { } schedule)
        {
            return [];
        }

        return
        [
            .. (await calendar.GetDaysAsync(schedule, request.From, request.To, cancellationToken))
                .Where(d => d.HolidayName is not null)
                .Select(d => new LeaveCalendarHoliday(d.Date, d.HolidayName!, d.HolidayFraction < 1)),
        ];
    }

    /// <summary>Days on which more people of a unit are away than its company's threshold; a threshold of 0 is off.</summary>
    private async Task<IReadOnlyList<LeaveCalendarAlert>> AlertsAsync(GetLeaveCalendarQuery request, List<LeaveCalendarEvent> events, CancellationToken cancellationToken)
    {
        var thresholds = new Dictionary<Guid, int>();
        foreach (var companyId in events.Select(e => e.CompanyId).Distinct())
        {
            thresholds[companyId] = (int)await settings.GetAsync<decimal>(HrSettingDefinitions.LeaveCalendarThreshold, companyId, cancellationToken);
        }

        var alerts = new List<LeaveCalendarAlert>();
        foreach (var unit in events.Where(e => e.OrgUnitId is not null && thresholds[e.CompanyId] > 0).GroupBy(e => (e.OrgUnitId!.Value, e.OrgUnitName!, e.CompanyId)))
        {
            var threshold = thresholds[unit.Key.CompanyId];
            for (var day = request.From; day <= request.To; day = day.AddDays(1))
            {
                var current = day;
                var away = unit.Where(e => e.StartDate <= current && current <= e.EndDate.AddDays(e.TravelDays)).Select(e => e.EmployeeId).Distinct().Count();
                if (away > threshold)
                {
                    alerts.Add(new LeaveCalendarAlert(day, unit.Key.Item1, unit.Key.Item2, away, threshold));
                }
            }
        }

        return [.. alerts.OrderBy(a => a.Date).ThenBy(a => a.OrgUnitName, StringComparer.Ordinal)];
    }
}
