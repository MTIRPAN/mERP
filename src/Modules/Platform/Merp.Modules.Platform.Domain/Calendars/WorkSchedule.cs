using Merp.Modules.Platform.Domain.Companies;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Platform.Domain.Calendars;

public readonly record struct WorkScheduleId(Guid Value) : IEntityId<WorkScheduleId>
{
    public static WorkScheduleId From(Guid value) => new(value);
}

/// <summary>
/// A weekly working pattern (PLAN P9): which days are worked, from when to when and the break, with the public holiday
/// calendar it follows. A schedule belongs to a company or, without one, to the whole tenant. One schedule per company
/// and one for the tenant is the default; a branch may use another (D33).
/// </summary>
[Audited]
public sealed class WorkSchedule : AuditableAggregateRoot<WorkScheduleId>
{
    public const int CodeMaxLength = 20;

    private WorkSchedule()
    {
    }

    public string Code { get; private set; } = null!;

    public LocalizedString Name { get; private set; } = null!;

    /// <summary>The company, or null for a schedule every company may use.</summary>
    public CompanyId? CompanyId { get; private set; }

    public HolidayCalendarId HolidayCalendarId { get; private set; }

    /// <summary>Monday to Sunday.</summary>
    public IReadOnlyList<WorkDay> Days { get; private set; } = [];

    /// <summary>The default of its company, or of the tenant when it has no company.</summary>
    public bool IsDefault { get; private set; }

    public bool IsActive { get; private set; }

    public int WorkingDaysPerWeek => Days.Count(d => d.IsWorkingDay);

    public static Result<WorkSchedule> Create(Code code, LocalizedString name, CompanyId? companyId, HolidayCalendarId holidayCalendarId, IReadOnlyList<WorkDay> days)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (code.Value.Length > CodeMaxLength)
        {
            throw new ArgumentException($"A schedule code has at most {CodeMaxLength} characters.", nameof(code));
        }

        var schedule = new WorkSchedule
        {
            Id = EntityId.New<WorkScheduleId>(),
            Code = code.Value,
            CompanyId = companyId,
            IsActive = true,
        };
        var update = schedule.Update(name, holidayCalendarId, days);
        return update.IsSuccess ? schedule : update.Error;
    }

    public Result Update(LocalizedString name, HolidayCalendarId holidayCalendarId, IReadOnlyList<WorkDay> days)
    {
        var checkedDays = WorkDay.Week(days);
        if (checkedDays.IsFailure)
        {
            return checkedDays.Error;
        }

        Name = name ?? throw new ArgumentNullException(nameof(name));
        HolidayCalendarId = holidayCalendarId;
        Days = checkedDays.Value;
        return Result.Success();
    }

    /// <summary>Makes it the default of its scope; the previous default is cleared by <see cref="WorkScheduleMadeDefault"/>.</summary>
    public void MakeDefault()
    {
        if (IsDefault)
        {
            return;
        }

        IsDefault = true;
        IsActive = true;
        Raise(new WorkScheduleMadeDefault(Id, CompanyId));
    }

    /// <summary>Called when another schedule of the same scope becomes the default.</summary>
    public void ClearDefault() => IsDefault = false;

    public Result Deactivate()
    {
        if (IsDefault)
        {
            return CalendarErrors.DefaultScheduleCannotBeDeactivated;
        }

        IsActive = false;
        return Result.Success();
    }

    public void Activate() => IsActive = true;

    public WorkDay Day(DayOfWeek day) => Days[((int)day + 6) % 7];
}

/// <summary>Another schedule of <paramref name="CompanyId"/> (or of the tenant) must stop being the default.</summary>
public sealed record WorkScheduleMadeDefault(WorkScheduleId ScheduleId, CompanyId? CompanyId) : IDomainEvent;

/// <summary>
/// One day of the week. A working day has start and end times and may have a break inside them; a day off has no
/// times.
/// </summary>
public sealed record WorkDay(DayOfWeek Day, bool IsWorkingDay, TimeOnly? Start = null, TimeOnly? End = null, TimeOnly? BreakStart = null, TimeOnly? BreakEnd = null)
{
    private static readonly DayOfWeek[] Order =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    /// <summary>A working day; the break is optional.</summary>
    public static WorkDay Working(DayOfWeek day, TimeOnly start, TimeOnly end, TimeOnly? breakStart = null, TimeOnly? breakEnd = null) =>
        new(day, true, start, end, breakStart, breakEnd);

    public static WorkDay Off(DayOfWeek day) => new(day, false);

    /// <summary>Monday to Sunday, each day once, times consistent. Errors name the day (<c>days[0]</c> is Monday).</summary>
    public static Result<IReadOnlyList<WorkDay>> Week(IReadOnlyList<WorkDay>? days)
    {
        if (days is null || days.Count != 7 || !days.Select(d => d.Day).SequenceEqual(Order))
        {
            return CalendarErrors.InvalidWorkDay.ForField("days");
        }

        for (var i = 0; i < days.Count; i++)
        {
            if (!days[i].IsValid())
            {
                return CalendarErrors.InvalidWorkDay.ForField($"days[{i}]");
            }
        }

        IReadOnlyList<WorkDay> week = [.. days.Select(d => d.IsWorkingDay ? d : Off(d.Day))];
        return Result.Success(week);
    }

    /// <summary>Working time in hours: from start to end (or until <paramref name="until"/>), less the break inside it.</summary>
    public decimal Hours(TimeOnly? until = null)
    {
        if (!IsWorkingDay)
        {
            return 0m;
        }

        var end = until is { } cut && cut < End!.Value ? cut : End!.Value;
        if (end <= Start!.Value)
        {
            return 0m;
        }

        var worked = end - Start.Value;
        if (BreakStart is { } breakStart && BreakEnd is { } breakEnd && breakStart < end)
        {
            worked -= (breakEnd < end ? breakEnd : end) - breakStart;
        }

        return decimal.Round((decimal)worked.TotalHours, 2);
    }

    private bool IsValid()
    {
        if (!Enum.IsDefined(Day))
        {
            return false;
        }

        if (!IsWorkingDay)
        {
            return true;
        }

        if (Start is not { } start || End is not { } end || start >= end)
        {
            return false;
        }

        // A break is both times or none, and lies within the working hours
        return (BreakStart, BreakEnd) switch
        {
            (null, null) => true,
            ({ } breakStart, { } breakEnd) => start <= breakStart && breakStart < breakEnd && breakEnd <= end,
            _ => false,
        };
    }
}
