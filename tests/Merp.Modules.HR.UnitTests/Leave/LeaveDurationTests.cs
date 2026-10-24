using Merp.Modules.HR.Application.Leave;
using Merp.Modules.HR.Domain.Leave;
using Merp.Modules.Platform.Contracts.Calendars;

namespace Merp.Modules.HR.UnitTests.Leave;

/// <summary>PLAN L2 acceptance: the duration scenarios of docs/domain/izin-kurallari.md §8.1.</summary>
public sealed class LeaveDurationTests
{
    private static readonly TimeOnly WorkStart = new(9, 0);
    private static readonly TimeOnly WorkEnd = new(18, 0);
    private static readonly TimeOnly HolidayStart = new(13, 0);

    // The public holidays of §7 around the scenarios: full days and half days from 13:00
    private static readonly Dictionary<DateOnly, (string Name, bool Half)> Holidays = new()
    {
        [new(2026, 5, 26)] = ("Kurban Bayramı arifesi", true),
        [new(2026, 5, 27)] = ("Kurban Bayramı", false),
        [new(2026, 5, 28)] = ("Kurban Bayramı", false),
        [new(2026, 5, 29)] = ("Kurban Bayramı", false),
        [new(2026, 5, 30)] = ("Kurban Bayramı", false),
        [new(2026, 10, 28)] = ("Cumhuriyet Bayramı arifesi", true),
        [new(2026, 10, 29)] = ("Cumhuriyet Bayramı", false),
        [new(2027, 1, 1)] = ("Yılbaşı", false),
        [new(2027, 3, 8)] = ("Ramazan Bayramı arifesi", true),
        [new(2027, 3, 9)] = ("Ramazan Bayramı", false),
        [new(2027, 3, 10)] = ("Ramazan Bayramı", false),
        [new(2027, 3, 11)] = ("Ramazan Bayramı", false),
        [new(2027, 5, 15)] = ("Kurban Bayramı arifesi", true),
        [new(2027, 5, 16)] = ("Kurban Bayramı", false),
        [new(2027, 5, 17)] = ("Kurban Bayramı", false),
        [new(2027, 5, 18)] = ("Kurban Bayramı", false),
        [new(2027, 5, 19)] = ("Kurban Bayramı", false),
    };

    /// <summary>The days of a schedule working Monday to Friday, or to Saturday, 09:00–18:00 with an hour's break.</summary>
    private static List<WorkingDay> Days(DateOnly from, DateOnly to, bool saturday = false)
    {
        var days = new List<WorkingDay>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var working = date.DayOfWeek is not DayOfWeek.Sunday && (saturday || date.DayOfWeek is not DayOfWeek.Saturday);
            var holiday = Holidays.TryGetValue(date, out var found) ? found : ((string Name, bool Half)?)null;
            var fraction = holiday is null ? 0 : holiday.Value.Half ? 0.5m : 1m;
            days.Add(new WorkingDay(
                date,
                working,
                fraction,
                holiday?.Name,
                holiday is { Half: true } ? HolidayStart : null,
                working ? WorkStart : null,
                working ? WorkEnd : null,
                !working ? 0 : fraction switch { 0 => 8, 0.5m => 4, _ => 0 }));
        }

        return days;
    }

    private static decimal WorkingDays(DateOnly from, DateOnly to, bool saturday = false, LeaveDayPart start = LeaveDayPart.Full, LeaveDayPart end = LeaveDayPart.Full) =>
        LeaveDurations.Count(DurationBasis.WorkingDays, Days(from, to, saturday), start, end).Value.Quantity;

    [Fact]
    public void S1_TheEveOfRepublicDay_CountsHalf_AndTheHolidayNothing() =>
        WorkingDays(new(2026, 10, 26), new(2026, 10, 30)).ShouldBe(3.5m);

    [Fact]
    public void S2_TheEveOfTheFeastOfSacrifice_CountsHalf_AndTheFeastNothing() =>
        WorkingDays(new(2026, 5, 25), new(2026, 5, 29)).ShouldBe(1.5m);

    [Fact]
    public void S3_ASixDayWeek_CountsTheSaturday() =>
        WorkingDays(new(2027, 3, 8), new(2027, 3, 13), saturday: true).ShouldBe(2.5m);

    [Fact]
    public void S4_TheFeastOfSacrifice2027_CountsNothing() =>
        WorkingDays(new(2027, 5, 17), new(2027, 5, 21)).ShouldBe(2);

    [Fact]
    public void S5_NewYearsDay_CountsNothing() =>
        WorkingDays(new(2026, 12, 31), new(2027, 1, 1)).ShouldBe(1);

    [Fact]
    public void S6_TheAfternoonOfAHalfDayHoliday_IsAZeroDuration()
    {
        var result = LeaveDurations.Count(DurationBasis.WorkingDays, Days(new(2026, 10, 28), new(2026, 10, 28)), LeaveDayPart.Afternoon);

        result.IsFailure.ShouldBeTrue();
        result.Error.Fields.ShouldHaveSingleItem().Code.ShouldBe(LeaveDurationErrors.ZeroDurationCode);
    }

    [Fact]
    public void S7_TheWeekend_CountsNothing() =>
        WorkingDays(new(2026, 10, 30), new(2026, 11, 2)).ShouldBe(2);

    [Fact]
    public void S8_ACalendarDayLeaveOfThreeDays_EndsOnTheThirdDay()
    {
        var ahead = Days(new(2026, 10, 30), new(2026, 11, 30));

        var end = LeaveDurations.EndDate(DurationBasis.CalendarDays, ahead, 3);

        end.ShouldBe(new DateOnly(2026, 11, 1));
        LeaveDurations.Count(DurationBasis.CalendarDays, Days(new(2026, 10, 30), end!.Value)).Value.Quantity.ShouldBe(3);
    }

    [Fact]
    public void HalfDays_AtTheEnds_CountHalf_AndTheMorningOfAHalfDayHolidayCountsHalf()
    {
        WorkingDays(new(2026, 10, 26), new(2026, 10, 27), start: LeaveDayPart.Afternoon, end: LeaveDayPart.Morning).ShouldBe(1);
        WorkingDays(new(2026, 10, 28), new(2026, 10, 28), end: LeaveDayPart.Morning).ShouldBe(0.5m);
    }

    [Fact]
    public void AWorkingDayLeave_EndsWhenItsLengthIsReached()
    {
        var ahead = Days(new(2026, 10, 26), new(2026, 11, 30));

        LeaveDurations.EndDate(DurationBasis.WorkingDays, ahead, 4).ShouldBe(new DateOnly(2026, 11, 2));
    }

    [Fact]
    public void HourlyLeave_CountsTheWorkingHours_AndStopsAtAHalfDayHoliday()
    {
        var normal = LeaveDurations.Count(DurationBasis.Hours, Days(new(2026, 10, 27), new(2026, 10, 27)), startTime: new(10, 0), endTime: new(12, 30)).Value;
        var eve = LeaveDurations.Count(DurationBasis.Hours, Days(new(2026, 10, 28), new(2026, 10, 28)), startTime: new(10, 0), endTime: new(17, 0)).Value;

        normal.Quantity.ShouldBe(2.5m);
        normal.DayEquivalent.ShouldBe(0.31m);
        eve.Quantity.ShouldBe(3);
        LeaveDurations.Count(DurationBasis.Hours, Days(new(2026, 10, 27), new(2026, 10, 27))).Error.Fields.ShouldHaveSingleItem().Code
            .ShouldBe(LeaveDurationErrors.TimesRequiredCode);
    }
}
