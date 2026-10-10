using Merp.Modules.Platform.Application.Calendars;
using Merp.Modules.Platform.Contracts.Calendars;
using Merp.Modules.Platform.Domain.Calendars;

namespace Merp.Modules.Platform.UnitTests.Calendars;

/// <summary>izin-kurallari.md §8.1 S1–S7: the day values a leave counts in working days (PLAN P9 acceptance).</summary>
public sealed class WorkingDaysTests
{
    private static readonly TimeOnly HalfDay = new(13, 0);

    /// <summary>Holidays of §7 the scenarios touch.</summary>
    private static readonly Dictionary<DateOnly, HolidaySnapshot> Holidays = new[]
    {
        Holiday(2026, 5, 26, half: true),
        Holiday(2026, 5, 27), Holiday(2026, 5, 28), Holiday(2026, 5, 29), Holiday(2026, 5, 30),
        Holiday(2026, 10, 28, half: true),
        Holiday(2026, 10, 29),
        Holiday(2027, 1, 1),
        Holiday(2027, 3, 8, half: true),
        Holiday(2027, 3, 9), Holiday(2027, 3, 10), Holiday(2027, 3, 11),
        Holiday(2027, 5, 15, half: true),
        Holiday(2027, 5, 16), Holiday(2027, 5, 17), Holiday(2027, 5, 18), Holiday(2027, 5, 19),
    }.ToDictionary(h => h.Date);

    public static TheoryData<string, string, string, bool, decimal> Scenarios => new()
    {
        // S1: 28.10 eve 0,5; 29.10 holiday 0
        { "S1", "2026-10-26", "2026-10-30", false, 3.5m },
        // S2: 26.05 eve 0,5; 27–29.05 bayram 0
        { "S2", "2026-05-25", "2026-05-29", false, 1.5m },
        // S3: six day week; 08.03 eve 0,5; 09–11.03 bayram 0; Friday 1; Saturday 1
        { "S3", "2027-03-08", "2027-03-13", true, 2.5m },
        // S4: 17–19.05 bayram 0
        { "S4", "2027-05-17", "2027-05-21", false, 2m },
        // S5: new year's day 0
        { "S5", "2026-12-31", "2027-01-01", false, 1m },
        // S7: the weekend does not count
        { "S7", "2026-10-30", "2026-11-02", false, 2m },
    };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void LeaveDays_MatchTheRules(string scenario, string from, string to, bool sixDays, decimal expected)
    {
        var days = Compute(sixDays ? SixDays() : FiveDays(), DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture));

        days.Sum(d => d.LeaveDayValue).ShouldBe(expected, scenario);
    }

    [Fact]
    public void S6_TheEveOfRepublicDay_IsAHolidayFrom13()
    {
        var day = Compute(FiveDays(), new DateOnly(2026, 10, 28), new DateOnly(2026, 10, 28)).Single();

        day.IsWorkingDay.ShouldBeTrue();
        day.HolidayFraction.ShouldBe(0.5m);
        day.HolidayStartsAt.ShouldBe(HalfDay);
        day.HolidayName.ShouldBe("tatil");
        day.LeaveDayValue.ShouldBe(0.5m);
        // 08:30–13:00, less the half hour of the 12:30–13:30 break before the holiday starts
        day.WorkingHours.ShouldBe(4m);
    }

    [Fact]
    public void SixDayWeek_CountsSaturday_ButNotSunday()
    {
        var days = Compute(SixDays(), new DateOnly(2026, 11, 7), new DateOnly(2026, 11, 8));

        days[0].Date.DayOfWeek.ShouldBe(DayOfWeek.Saturday);
        days[0].IsWorkingDay.ShouldBeTrue();
        days[0].WorkingHours.ShouldBe(5m);
        days[1].IsWorkingDay.ShouldBeFalse();
        days[1].LeaveDayValue.ShouldBe(0m);
    }

    [Fact]
    public void WorkingHours_LeaveOutTheBreak_AndFullHolidays()
    {
        var days = Compute(FiveDays(), new DateOnly(2026, 10, 27), new DateOnly(2026, 10, 29));

        days[0].WorkingHours.ShouldBe(8.5m);
        days[2].WorkingHours.ShouldBe(0m);
        days[2].HolidayFraction.ShouldBe(1m);
    }

    [Fact]
    public void TooLongARange_IsRefused() =>
        Should.Throw<ArgumentOutOfRangeException>(() => Compute(FiveDays(), new DateOnly(2026, 1, 1), new DateOnly(2027, 3, 1)));

    private static IReadOnlyList<WorkingDay> Compute(ScheduleSnapshot schedule, DateOnly from, DateOnly to) =>
        WorkingDays.Compute(schedule, Holidays, from, to, _ => "tatil");

    private static HolidaySnapshot Holiday(int year, int month, int day, bool half = false) =>
        new(new DateOnly(year, month, day), half ? 0.5m : 1m, half ? HalfDay : null, new Dictionary<string, string> { ["tr-TR"] = "tatil" });

    internal static WorkDay Weekday(DayOfWeek day) =>
        WorkDay.Working(day, new TimeOnly(8, 30), new TimeOnly(18, 0), new TimeOnly(12, 30), new TimeOnly(13, 30));

    internal static IReadOnlyList<WorkDay> Week(bool saturday) =>
    [
        Weekday(DayOfWeek.Monday), Weekday(DayOfWeek.Tuesday), Weekday(DayOfWeek.Wednesday), Weekday(DayOfWeek.Thursday), Weekday(DayOfWeek.Friday),
        saturday ? WorkDay.Working(DayOfWeek.Saturday, new TimeOnly(8, 30), new TimeOnly(13, 30)) : WorkDay.Off(DayOfWeek.Saturday),
        WorkDay.Off(DayOfWeek.Sunday),
    ];

    private static ScheduleSnapshot FiveDays() => new(Guid.NewGuid(), Guid.NewGuid(), Week(saturday: false));

    private static ScheduleSnapshot SixDays() => new(Guid.NewGuid(), Guid.NewGuid(), Week(saturday: true));
}
