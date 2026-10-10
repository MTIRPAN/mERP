namespace Merp.Modules.Platform.Contracts.Calendars;

/// <summary>
/// One day of a work schedule (PLAN P9). Leave durations (HR, PLAN L2) count <see cref="LeaveDayValue"/>: 1 for a
/// working day, 0,5 when half of it is a holiday, 0 for days off and full holidays.
/// </summary>
/// <param name="Date">The day.</param>
/// <param name="IsWorkingDay">Worked according to the weekly pattern, whatever the holidays.</param>
/// <param name="HolidayFraction">0, 0,5 (from <paramref name="HolidayStartsAt"/>) or 1.</param>
/// <param name="HolidayName">The holiday's name in the caller's language.</param>
/// <param name="HolidayStartsAt">Start of a half day holiday, e.g. 13:00.</param>
/// <param name="Start">Start of work on a working day.</param>
/// <param name="End">End of work on a working day.</param>
/// <param name="WorkingHours">Hours actually worked: less the break, less the holiday part.</param>
public sealed record WorkingDay(
    DateOnly Date,
    bool IsWorkingDay,
    decimal HolidayFraction,
    string? HolidayName,
    TimeOnly? HolidayStartsAt,
    TimeOnly? Start,
    TimeOnly? End,
    decimal WorkingHours)
{
    /// <summary>The day's weight in a leave counted in working days (izin-kurallari.md §4).</summary>
    public decimal LeaveDayValue => IsWorkingDay ? 1m - HolidayFraction : 0m;
}

/// <summary>
/// Working days and public holidays of the tenant's work schedules (PLAN P9, D33). Read only; answers are cached and
/// refreshed when a schedule or a holiday changes.
/// </summary>
public interface IWorkingCalendar
{
    /// <summary>
    /// The schedule that applies: the branch's own, otherwise the company's default, otherwise the tenant's default.
    /// Null when the tenant has none.
    /// </summary>
    Task<Guid?> ResolveScheduleIdAsync(Guid companyId, Guid? branchId, CancellationToken cancellationToken = default);

    /// <summary>One day; null when the schedule does not exist.</summary>
    Task<WorkingDay?> GetDayAsync(Guid scheduleId, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>Every day from <paramref name="from"/> to <paramref name="to"/> inclusive (at most 400 days); empty when the schedule does not exist.</summary>
    Task<IReadOnlyList<WorkingDay>> GetDaysAsync(Guid scheduleId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
