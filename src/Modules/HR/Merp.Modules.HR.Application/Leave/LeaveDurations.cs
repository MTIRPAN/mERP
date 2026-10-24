using Merp.Modules.HR.Domain.Leave;
using Merp.Modules.Platform.Contracts.Calendars;
using Merp.SharedKernel.Results;

namespace Merp.Modules.HR.Application.Leave;

/// <param name="Date">The day.</param>
/// <param name="Value">What it counts: days, or hours for an hourly leave.</param>
/// <param name="IsWorkingDay">Worked according to the schedule.</param>
/// <param name="HolidayName">The public holiday on that day.</param>
public sealed record LeaveDurationDay(DateOnly Date, decimal Value, bool IsWorkingDay, string? HolidayName);

/// <summary>A counted leave: its days or hours, and each day's share.</summary>
/// <param name="Quantity">Days for working and calendar days, hours for an hourly leave.</param>
/// <param name="DayEquivalent">For an hourly leave, the hours as days of the schedule's longest working day.</param>
/// <param name="Days">Each day of the range with its share.</param>
public sealed record LeaveDuration(decimal Quantity, decimal? DayEquivalent, IReadOnlyList<LeaveDurationDay> Days);

/// <summary>Error codes of leave durations; texts in the module's <c>tr-TR.json</c>.</summary>
public static class LeaveDurationErrors
{
    public const string ZeroDurationCode = "hr.leave.errors.zero_duration";
    public const string TimesRequiredCode = "hr.leave.errors.times_required";

    /// <summary>The longest range counted at once, the calendar's limit.</summary>
    public const int MaxDays = 400;
}

/// <summary>
/// Counts the length of a leave from the days of the employee's work schedule (docs/domain/izin-kurallari.md §4, D42).
/// A half day holiday (arife, 28 Ekim) is the afternoon: its morning still counts.
/// </summary>
public static class LeaveDurations
{
    private const decimal Half = 0.5m;

    /// <summary>
    /// The length of a leave over <paramref name="days"/> (the range, first to last day, in order). Working days count
    /// <see cref="WorkingDay.LeaveDayValue"/>, the first day only its afternoon with <paramref name="startPart"/>
    /// Afternoon, the last only its morning with <paramref name="endPart"/> Morning. Calendar days count every day.
    /// Hours count the working hours between <paramref name="startTime"/> on the first day and
    /// <paramref name="endTime"/> on the last. A length of zero is an error.
    /// </summary>
    public static Result<LeaveDuration> Count(
        DurationBasis basis,
        IReadOnlyList<WorkingDay> days,
        LeaveDayPart startPart = LeaveDayPart.Full,
        LeaveDayPart endPart = LeaveDayPart.Full,
        TimeOnly? startTime = null,
        TimeOnly? endTime = null)
    {
        ArgumentNullException.ThrowIfNull(days);
        if (days.Count == 0)
        {
            return Zero;
        }

        if (basis == DurationBasis.Hours && (startTime is null || endTime is null))
        {
            return Error.Validation([new FieldError("startTime", LeaveDurationErrors.TimesRequiredCode)]);
        }

        var first = days[0].Date;
        var last = days[^1].Date;
        var counted = days.Select(day => new LeaveDurationDay(
            day.Date,
            basis switch
            {
                DurationBasis.CalendarDays => 1,
                DurationBasis.Hours => Hours(day, day.Date == first ? startTime!.Value : TimeOnly.MinValue, day.Date == last ? endTime!.Value : TimeOnly.MaxValue),
                _ => WorkingDayValue(day, day.Date == first && startPart == LeaveDayPart.Afternoon, day.Date == last && endPart == LeaveDayPart.Morning),
            },
            day.IsWorkingDay,
            day.HolidayName)).ToList();

        var quantity = counted.Sum(d => d.Value);
        if (quantity <= 0)
        {
            return Zero;
        }

        decimal? equivalent = null;
        if (basis == DurationBasis.Hours)
        {
            var dayHours = days.Where(d => d.IsWorkingDay && d.HolidayFraction == 0).Select(d => d.WorkingHours).DefaultIfEmpty(0).Max();
            equivalent = dayHours > 0 ? Math.Round(quantity / dayHours, 2) : null;
        }

        return new LeaveDuration(quantity, equivalent, counted);
    }

    /// <summary>
    /// The last day of a leave of <paramref name="length"/> days starting with the first of <paramref name="days"/>
    /// (calendar days: the length's last day; working days: the day the counted length is reached), or null when the
    /// days given do not reach it.
    /// </summary>
    public static DateOnly? EndDate(DurationBasis basis, IReadOnlyList<WorkingDay> days, decimal length)
    {
        ArgumentNullException.ThrowIfNull(days);
        if (days.Count == 0 || length <= 0)
        {
            return null;
        }

        if (basis == DurationBasis.CalendarDays)
        {
            var end = days[0].Date.AddDays((int)Math.Ceiling(length) - 1);
            return end <= days[^1].Date ? end : null;
        }

        var sum = 0m;
        foreach (var day in days)
        {
            sum += day.LeaveDayValue;
            if (sum >= length)
            {
                return day.Date;
            }
        }

        return null;
    }

    private static Error Zero => Error.Validation([new FieldError("endDate", LeaveDurationErrors.ZeroDurationCode)]);

    /// <summary>
    /// A working day as halves: the morning counts unless the day is off or wholly a holiday, the afternoon unless it is
    /// also a half day holiday. A request for one half meeting a holiday on the same half counts nothing.
    /// </summary>
    private static decimal WorkingDayValue(WorkingDay day, bool afternoonOnly, bool morningOnly)
    {
        if (!day.IsWorkingDay || day.HolidayFraction >= 1)
        {
            return 0;
        }

        var morning = afternoonOnly ? 0 : Half;
        var afternoon = morningOnly || day.HolidayFraction > 0 ? 0 : Half;
        return morning + afternoon;
    }

    /// <summary>Working hours of a day between <paramref name="from"/> and <paramref name="to"/>, at most the day's working hours.</summary>
    private static decimal Hours(WorkingDay day, TimeOnly from, TimeOnly to)
    {
        if (!day.IsWorkingDay || day.HolidayFraction >= 1 || day.Start is not { } start || day.End is not { } end)
        {
            return 0;
        }

        // A half day holiday ends the working day when it starts
        if (day.HolidayFraction > 0 && day.HolidayStartsAt is { } holiday && holiday < end)
        {
            end = holiday;
        }

        var begin = from > start ? from : start;
        var finish = to < end ? to : end;
        if (finish <= begin)
        {
            return 0;
        }

        var hours = (decimal)(finish - begin).TotalHours;
        return Math.Round(Math.Min(hours, day.WorkingHours), 2);
    }
}
