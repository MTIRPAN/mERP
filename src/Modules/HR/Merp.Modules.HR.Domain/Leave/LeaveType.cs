using System.Text.Json.Serialization;
using Merp.Modules.HR.Domain.Employees;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.HR.Domain.Leave;

public readonly record struct LeaveTypeId(Guid Value) : IEntityId<LeaveTypeId>
{
    public static LeaveTypeId From(Guid value) => new(value);
}

[JsonConverter(typeof(JsonStringEnumConverter<LeaveCategory>))]
public enum LeaveCategory
{
    Annual,

    /// <summary>Mazeret izni: marriage, bereavement, paternity…</summary>
    Excuse,

    Maternity,

    Unpaid,

    /// <summary>An arrangement rather than requests, e.g. nursing hours (süt izni).</summary>
    Arrangement,

    Sick,

    Administrative,

    Travel,
}

/// <summary>Who pays the days off.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LeavePayer>))]
public enum LeavePayer
{
    Employer,

    /// <summary>SGK pays a benefit (doğum, rapor).</summary>
    Sgk,

    /// <summary>Unpaid.</summary>
    None,
}

/// <summary>The smallest part a request may have.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LeaveUnit>))]
public enum LeaveUnit
{
    Day,
    HalfDay,
    Hour,
}

/// <summary>How the days of a request are counted (docs/domain/izin-kurallari.md §4).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DurationBasis>))]
public enum DurationBasis
{
    /// <summary>Working days of the employee's calendar, holidays not counted.</summary>
    WorkingDays,

    CalendarDays,

    Hours,
}

/// <summary>
/// The limits of a leave type from a day on (D41). Which version applies is decided by the event's date for event
/// based types (the birth for paternity and maternity leave), by the first day of the leave for the others.
/// </summary>
/// <param name="ValidFrom">First day in force.</param>
/// <param name="FixedDays">Days per event, e.g. 3 for marriage; maternity leave counts the whole leave.</param>
/// <param name="DaysBeforeEvent">Of the fixed days, those before the event (maternity leave: before the birth).</param>
/// <param name="MultipleBirthExtraDays">Days added before the event for a multiple pregnancy.</param>
/// <param name="MaxDaysPerYear">At most this many days in a year, e.g. 10 for a disabled child's treatment.</param>
/// <param name="MaxDaysPerRequest">At most this many days in one request.</param>
/// <param name="DailyHours">Hours a day of an arrangement, e.g. 1.5 for nursing.</param>
/// <param name="MinNoticeDays">Days between the request and the first day of leave.</param>
public sealed record LeaveTypeVersion(
    DateOnly ValidFrom,
    decimal? FixedDays = null,
    decimal? DaysBeforeEvent = null,
    decimal? MultipleBirthExtraDays = null,
    decimal? MaxDaysPerYear = null,
    decimal? MaxDaysPerRequest = null,
    decimal? DailyHours = null,
    int? MinNoticeDays = null) : IVersioned;

/// <summary>What a leave type is and how it behaves; its limits are versioned (D41).</summary>
/// <param name="Category">Kind of leave.</param>
/// <param name="Payer">Who pays it.</param>
/// <param name="DeductsFrom">The code of the leave type whose balance it uses (YILLIK); null when it has none.</param>
/// <param name="Unit">Smallest part of a request.</param>
/// <param name="DurationBasis">How days are counted.</param>
/// <param name="RequiresDocument">A document is attached to the request.</param>
/// <param name="RequiresApproval">The request goes through approval.</param>
/// <param name="GenderRestriction">Only employees of this gender may use it.</param>
/// <param name="AllowPastDates">HR may enter it for past days.</param>
/// <param name="LegalBasis">The article it rests on, e.g. "4857 Ek md. 2".</param>
public sealed record LeaveTypeProfile(
    LeaveCategory Category,
    LeavePayer Payer,
    string? DeductsFrom,
    LeaveUnit Unit,
    DurationBasis DurationBasis,
    bool RequiresDocument,
    bool RequiresApproval,
    Gender? GenderRestriction,
    bool AllowPastDates,
    string? LegalBasis);

/// <summary>
/// A leave type of the tenant (docs/domain/izin-kurallari.md §3): seeded from Turkish law, changed and extended by
/// the tenant. Without a company it is offered in every company. The code never changes.
/// </summary>
[Audited]
public sealed class LeaveType : AuditableAggregateRoot<LeaveTypeId>
{
    public const int CodeMaxLength = 30;
    public const int NameMaxLength = 100;
    public const int LegalBasisMaxLength = 200;

    private LeaveType()
    {
    }

    public string Code { get; private set; } = null!;

    public LocalizedString Name { get; private set; } = null!;

    /// <summary>The company it belongs to; null for every company.</summary>
    public Guid? CompanyId { get; private set; }

    public LeaveCategory Category { get; private set; }

    public LeavePayer Payer { get; private set; }

    public string? DeductsFrom { get; private set; }

    public LeaveUnit Unit { get; private set; }

    public DurationBasis DurationBasis { get; private set; }

    public bool RequiresDocument { get; private set; }

    public bool RequiresApproval { get; private set; }

    public Gender? GenderRestriction { get; private set; }

    public bool AllowPastDates { get; private set; }

    public string? LegalBasis { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyList<LeaveTypeVersion> Versions { get; private set; } = [];

    public static Result<LeaveType> Create(Code code, LocalizedString name, Guid? companyId, LeaveTypeProfile profile, IReadOnlyList<LeaveTypeVersion> versions)
    {
        ArgumentNullException.ThrowIfNull(code);
        var initial = Leave.Versions.Initial(versions);
        if (initial.IsFailure)
        {
            return initial.Error;
        }

        var type = new LeaveType { Id = EntityId.New<LeaveTypeId>(), Code = code.Value, CompanyId = companyId, IsActive = true, Versions = initial.Value };
        type.Apply(name, profile);
        return type;
    }

    /// <summary>Changes the type; versions that have started on <paramref name="today"/> stay as they are.</summary>
    public Result Update(LocalizedString name, LeaveTypeProfile profile, bool isActive, IReadOnlyList<LeaveTypeVersion> versions, DateOnly today)
    {
        var replaced = Leave.Versions.Replace(Versions, versions, today);
        if (replaced.IsFailure)
        {
            return replaced.Error;
        }

        Apply(name, profile);
        IsActive = isActive;
        Versions = replaced.Value;
        return Result.Success();
    }

    /// <summary>The limits on <paramref name="day"/>: the event's date or the first day of leave.</summary>
    public LeaveTypeVersion? VersionOn(DateOnly day) => Leave.Versions.On(Versions, day);

    private void Apply(LocalizedString name, LeaveTypeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Category = profile.Category;
        Payer = profile.Payer;
        DeductsFrom = profile.DeductsFrom?.Trim().ToUpperInvariant();
        Unit = profile.Unit;
        DurationBasis = profile.DurationBasis;
        RequiresDocument = profile.RequiresDocument;
        RequiresApproval = profile.RequiresApproval;
        GenderRestriction = profile.GenderRestriction;
        AllowPastDates = profile.AllowPastDates;
        LegalBasis = Text.Optional(profile.LegalBasis, LegalBasisMaxLength, nameof(profile.LegalBasis));
    }
}
