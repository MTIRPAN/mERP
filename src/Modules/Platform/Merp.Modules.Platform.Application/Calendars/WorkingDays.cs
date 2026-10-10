using Merp.Modules.Platform.Contracts.Calendars;
using Merp.Modules.Platform.Domain.Calendars;

namespace Merp.Modules.Platform.Application.Calendars;

/// <summary>A schedule as the working calendar caches it.</summary>
public sealed record ScheduleSnapshot(Guid Id, Guid HolidayCalendarId, IReadOnlyList<WorkDay> Days)
{
    public WorkDay Day(DayOfWeek day) => Days[((int)day + 6) % 7];
}

/// <summary>A holiday as the working calendar caches it; names by culture.</summary>
public sealed record HolidaySnapshot(DateOnly Date, decimal Fraction, TimeOnly? StartsAt, IReadOnlyDictionary<string, string> Name);

/// <summary>
/// Day values of a schedule (izin-kurallari.md §4): a day off counts nothing, a full holiday nothing, a half day
/// holiday half; working hours stop when a half day holiday starts.
/// </summary>
public static class WorkingDays
{
    public const int MaxDays = 400;

    /// <param name="schedule">The weekly pattern.</param>
    /// <param name="holidays">Holidays of the schedule's calendar in the range, by date.</param>
    /// <param name="from">First day.</param>
    /// <param name="to">Last day, inclusive.</param>
    /// <param name="holidayName">Picks a holiday's name in the reader's language.</param>
    public static IReadOnlyList<WorkingDay> Compute(
        ScheduleSnapshot schedule,
        IReadOnlyDictionary<DateOnly, HolidaySnapshot> holidays,
        DateOnly from,
        DateOnly to,
        Func<HolidaySnapshot, string> holidayName)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(holidays);
        ArgumentNullException.ThrowIfNull(holidayName);
        if (to < from || to.DayNumber - from.DayNumber >= MaxDays)
        {
            throw new ArgumentOutOfRangeException(nameof(to), $"A range is 1 to {MaxDays} days.");
        }

        var days = new List<WorkingDay>(to.DayNumber - from.DayNumber + 1);
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var day = schedule.Day(date.DayOfWeek);
            var holiday = holidays.GetValueOrDefault(date);
            var hours = holiday switch
            {
                null => day.Hours(),
                { Fraction: >= 1m } => 0m,
                { StartsAt: { } startsAt } => day.Hours(until: startsAt),
                _ => day.Hours() / 2,
            };

            days.Add(new WorkingDay(
                date,
                day.IsWorkingDay,
                holiday?.Fraction ?? 0m,
                holiday is null ? null : holidayName(holiday),
                holiday?.StartsAt,
                day.Start,
                day.End,
                hours));
        }

        return days;
    }
}

/// <summary>Cached schedules and holidays of the tenant; cleared after any change to them.</summary>
public interface ICalendarCache
{
    Task InvalidateAsync(CancellationToken cancellationToken = default);
}
