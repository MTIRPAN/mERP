using Merp.Modules.Platform.Domain.Calendars;
using Merp.Modules.Platform.Domain.Companies;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Platform.UnitTests.Calendars;

public sealed class CalendarTests
{
    private static readonly LocalizedString Name = LocalizedString.Of("tr-TR", "Ad");

    [Fact]
    public void HalfDayHolidays_StartAt13_UnlessGiven()
    {
        var calendar = Calendar();

        var eve = calendar.AddHoliday(new HolidayDefinition(new DateOnly(2026, 10, 28), Name, HolidayDuration.Half, null, IsMovable: false)).Value;
        var custom = calendar.AddHoliday(new HolidayDefinition(new DateOnly(2026, 12, 31), Name, HolidayDuration.Half, new TimeOnly(12, 0), IsMovable: false)).Value;
        var full = calendar.AddHoliday(new HolidayDefinition(new DateOnly(2026, 10, 29), Name, HolidayDuration.Full, new TimeOnly(12, 0), IsMovable: false)).Value;

        eve.StartsAt.ShouldBe(new TimeOnly(13, 0));
        eve.Fraction.ShouldBe(0.5m);
        custom.StartsAt.ShouldBe(new TimeOnly(12, 0));
        full.StartsAt.ShouldBeNull();
        full.Fraction.ShouldBe(1m);
    }

    [Fact]
    public void ADateHasOneHoliday()
    {
        var calendar = Calendar();
        var first = calendar.AddHoliday(Fixed(2026, 1, 1)).Value;
        var second = calendar.AddHoliday(Fixed(2026, 4, 23)).Value;

        calendar.AddHoliday(Fixed(2026, 1, 1)).Error.ShouldBe(CalendarErrors.DuplicateHoliday);
        calendar.UpdateHoliday(second.Id, Fixed(2026, 1, 1)).Error.ShouldBe(CalendarErrors.DuplicateHoliday);
        calendar.UpdateHoliday(first.Id, Fixed(2026, 1, 1)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void CopyFixedHolidays_KeepsDayAndMonth_SkipsReligiousAndExistingOnes()
    {
        var calendar = Calendar();
        calendar.AddHoliday(Fixed(2027, 1, 1));
        calendar.AddHoliday(Fixed(2027, 5, 19));
        calendar.AddHoliday(new HolidayDefinition(new DateOnly(2027, 3, 9), Name, HolidayDuration.Full, null, IsMovable: true));
        calendar.AddHoliday(Fixed(2028, 1, 1));

        calendar.CopyFixedHolidays(2027, 2028).ShouldBe(1);

        calendar.On(new DateOnly(2028, 5, 19)).ShouldNotBeNull().IsMovable.ShouldBeFalse();
        calendar.On(new DateOnly(2028, 3, 9)).ShouldBeNull();
        calendar.HasMovableHolidays(2027).ShouldBeTrue();
        calendar.HasMovableHolidays(2028).ShouldBeFalse();
    }

    [Fact]
    public void CopyFixedHolidays_SkipsTheLeapDayInACommonYear()
    {
        var calendar = Calendar();
        calendar.AddHoliday(Fixed(2028, 2, 29));

        calendar.CopyFixedHolidays(2028, 2029).ShouldBe(0);
    }

    [Fact]
    public void Schedule_NeedsMondayToSunday_WithConsistentTimes()
    {
        var week = WorkingDaysTests.Week(saturday: false);
        Create(week).IsSuccess.ShouldBeTrue();

        Create(week.Take(6).ToList()).Error.Fields.ShouldHaveSingleItem().Field.ShouldBe("days");
        Create([.. week.Skip(1), week[0]]).Error.Fields.ShouldHaveSingleItem().Field.ShouldBe("days");

        var endBeforeStart = week.ToList();
        endBeforeStart[2] = WorkDay.Working(DayOfWeek.Wednesday, new TimeOnly(18, 0), new TimeOnly(8, 30));
        Create(endBeforeStart).Error.Fields.ShouldHaveSingleItem().Field.ShouldBe("days[2]");

        var breakOutside = week.ToList();
        breakOutside[0] = WorkDay.Working(DayOfWeek.Monday, new TimeOnly(8, 30), new TimeOnly(12, 0), new TimeOnly(12, 30), new TimeOnly(13, 30));
        Create(breakOutside).Error.Fields.ShouldHaveSingleItem().Field.ShouldBe("days[0]");
    }

    [Fact]
    public void Schedule_ForgetsTheTimesOfDaysOff()
    {
        var week = WorkingDaysTests.Week(saturday: false).ToList();
        week[6] = new WorkDay(DayOfWeek.Sunday, false, new TimeOnly(9, 0), new TimeOnly(17, 0));

        var schedule = Create(week).Value;

        schedule.Day(DayOfWeek.Sunday).Start.ShouldBeNull();
        schedule.WorkingDaysPerWeek.ShouldBe(5);
        schedule.Day(DayOfWeek.Monday).Hours().ShouldBe(8.5m);
    }

    [Fact]
    public void MakingADefault_AsksTheOthersToStepBack_AndTheDefaultStaysActive()
    {
        var schedule = Create(WorkingDaysTests.Week(saturday: true), CompanyId.From(Guid.CreateVersion7())).Value;

        schedule.MakeDefault();

        schedule.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<WorkScheduleMadeDefault>().CompanyId.ShouldBe(schedule.CompanyId);
        schedule.Deactivate().Error.ShouldBe(CalendarErrors.DefaultScheduleCannotBeDeactivated);
        schedule.ClearDefault();
        schedule.Deactivate().IsSuccess.ShouldBeTrue();
        schedule.IsActive.ShouldBeFalse();
    }

    private static HolidayCalendar Calendar() => HolidayCalendar.Create(Code.Create("TR", 20).Value, Name);

    private static HolidayDefinition Fixed(int year, int month, int day) =>
        new(new DateOnly(year, month, day), Name, HolidayDuration.Full, null, IsMovable: false);

    private static Merp.SharedKernel.Results.Result<WorkSchedule> Create(IReadOnlyList<WorkDay> days, CompanyId? companyId = null) =>
        WorkSchedule.Create(Code.Create("STANDART", 20).Value, Name, companyId, HolidayCalendarId.From(Guid.CreateVersion7()), days);
}
