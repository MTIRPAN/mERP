using System.Text.Json.Serialization;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Platform.Domain.Calendars;

public readonly record struct HolidayCalendarId(Guid Value) : IEntityId<HolidayCalendarId>
{
    public static HolidayCalendarId From(Guid value) => new(value);
}

public readonly record struct HolidayId(Guid Value) : IEntityId<HolidayId>
{
    public static HolidayId From(Guid value) => new(value);
}

[JsonConverter(typeof(JsonStringEnumConverter<HolidayDuration>))]
public enum HolidayDuration
{
    Full,

    /// <summary>A half day from <see cref="Holiday.StartsAt"/> (eves and 28 October, from 13:00).</summary>
    Half,
}

/// <summary>
/// Public holidays of a country (PLAN P9), e.g. <c>TR</c>. Fixed holidays repeat every year and can be copied to the
/// next one; movable (religious) holidays are entered every year from the official calendar (izin-kurallari.md §7).
/// </summary>
[Audited]
public sealed class HolidayCalendar : AuditableAggregateRoot<HolidayCalendarId>
{
    public const int CodeMaxLength = 20;

    private readonly List<Holiday> _holidays = [];

    private HolidayCalendar()
    {
    }

    public string Code { get; private set; } = null!;

    public LocalizedString Name { get; private set; } = null!;

    public IReadOnlyCollection<Holiday> Holidays => _holidays.AsReadOnly();

    public static HolidayCalendar Create(Code code, LocalizedString name)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (code.Value.Length > CodeMaxLength)
        {
            throw new ArgumentException($"A calendar code has at most {CodeMaxLength} characters.", nameof(code));
        }

        return new HolidayCalendar
        {
            Id = EntityId.New<HolidayCalendarId>(),
            Code = code.Value,
            Name = name ?? throw new ArgumentNullException(nameof(name)),
        };
    }

    public void Rename(LocalizedString name) => Name = name ?? throw new ArgumentNullException(nameof(name));

    public Holiday? On(DateOnly date) => _holidays.SingleOrDefault(h => h.Date == date);

    public Result<Holiday> AddHoliday(HolidayDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (On(definition.Date) is not null)
        {
            return CalendarErrors.DuplicateHoliday;
        }

        var holiday = new Holiday(Id, definition);
        _holidays.Add(holiday);
        return holiday;
    }

    public Result UpdateHoliday(HolidayId holidayId, HolidayDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var holiday = _holidays.SingleOrDefault(h => h.Id == holidayId);
        if (holiday is null)
        {
            return CommonErrors.NotFound;
        }

        if (_holidays.Any(h => h.Id != holidayId && h.Date == definition.Date))
        {
            return CalendarErrors.DuplicateHoliday;
        }

        holiday.Apply(definition);
        return Result.Success();
    }

    public Result RemoveHoliday(HolidayId holidayId)
    {
        var holiday = _holidays.SingleOrDefault(h => h.Id == holidayId);
        if (holiday is null)
        {
            return CommonErrors.NotFound;
        }

        _holidays.Remove(holiday);
        return Result.Success();
    }

    /// <summary>
    /// Copies the fixed holidays of <paramref name="fromYear"/> to <paramref name="toYear"/>, keeping day and month.
    /// Dates that already have a holiday are skipped. Returns the number added.
    /// </summary>
    public int CopyFixedHolidays(int fromYear, int toYear)
    {
        var added = 0;
        foreach (var holiday in _holidays.Where(h => h.Date.Year == fromYear && !h.IsMovable).ToList())
        {
            // 29 February has no copy in a common year
            if (holiday.Date.Day > DateTime.DaysInMonth(toYear, holiday.Date.Month))
            {
                continue;
            }

            var date = new DateOnly(toYear, holiday.Date.Month, holiday.Date.Day);
            if (On(date) is not null)
            {
                continue;
            }

            _holidays.Add(new Holiday(Id, holiday.Definition with { Date = date }));
            added++;
        }

        return added;
    }

    /// <summary>Whether the movable (religious) holidays of a year have been entered.</summary>
    public bool HasMovableHolidays(int year) => _holidays.Any(h => h.Date.Year == year && h.IsMovable);
}

/// <summary>What a holiday is; a half day starts at <paramref name="StartsAt"/> (13:00 when not given).</summary>
public sealed record HolidayDefinition(DateOnly Date, LocalizedString Name, HolidayDuration Duration, TimeOnly? StartsAt, bool IsMovable)
{
    public static readonly TimeOnly DefaultHalfDayStart = new(13, 0);
}

/// <summary>A public holiday on one date.</summary>
[Audited]
public sealed class Holiday : Entity<HolidayId>
{
    private Holiday()
    {
    }

    internal Holiday(HolidayCalendarId calendarId, HolidayDefinition definition)
        : base(EntityId.New<HolidayId>())
    {
        CalendarId = calendarId;
        Apply(definition);
    }

    public HolidayCalendarId CalendarId { get; private set; }

    public DateOnly Date { get; private set; }

    public LocalizedString Name { get; private set; } = null!;

    public HolidayDuration Duration { get; private set; }

    /// <summary>Start of a half day holiday; null for a full day.</summary>
    public TimeOnly? StartsAt { get; private set; }

    /// <summary>A religious holiday whose date changes every year (bayram, arife).</summary>
    public bool IsMovable { get; private set; }

    /// <summary>Share of the day that is a holiday: 1 or 0,5.</summary>
    public decimal Fraction => Duration == HolidayDuration.Full ? 1m : 0.5m;

    internal HolidayDefinition Definition => new(Date, Name, Duration, StartsAt, IsMovable);

    internal void Apply(HolidayDefinition definition)
    {
        if (!Enum.IsDefined(definition.Duration))
        {
            throw new ArgumentOutOfRangeException(nameof(definition), definition.Duration, "Unknown holiday duration.");
        }

        Date = definition.Date;
        Name = definition.Name ?? throw new ArgumentNullException(nameof(definition), "A holiday has a name.");
        Duration = definition.Duration;
        StartsAt = definition.Duration == HolidayDuration.Half ? definition.StartsAt ?? HolidayDefinition.DefaultHalfDayStart : null;
        IsMovable = definition.IsMovable;
    }
}

public static class CalendarErrors
{
    public static readonly Error DuplicateHoliday = Error.Validation("platform.calendars.errors.duplicate_holiday").ForField("date");
    public static readonly Error InvalidWorkDay = Error.Validation("platform.calendars.errors.invalid_work_day");
    public static readonly Error DefaultScheduleCannotBeDeactivated = Error.Conflict("platform.calendars.errors.default_schedule_inactive");
    public static readonly Error TenantScopeRequired = Error.Forbidden("platform.calendars.errors.tenant_scope_required");
}
