using System.Text.Json.Serialization;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.HR.Domain.Leave;

public readonly record struct LeavePolicyId(Guid Value) : IEntityId<LeavePolicyId>
{
    public static LeavePolicyId From(Guid value) => new(value);
}

/// <summary>When the annual entitlement is earned (docs/domain/izin-kurallari.md §2).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AccrualMethod>))]
public enum AccrualMethod
{
    /// <summary>The whole year's days on each seniority anniversary (the law).</summary>
    AnniversaryLumpSum,

    /// <summary>A twelfth of the year's days each month.</summary>
    MonthlyAccrual,

    /// <summary>The whole year's days each 1 January.</summary>
    CalendarYearLumpSum,
}

/// <summary>The rule that one part of a split annual leave is at least ten days (4857 md. 56).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TenDayBlockRule>))]
public enum TenDayBlockRule
{
    Off,
    Warn,
    Enforce,
}

[JsonConverter(typeof(JsonStringEnumConverter<LeaveRounding>))]
public enum LeaveRounding
{
    None,
    HalfDayDown,
    HalfDayNearest,
}

/// <param name="MinServiceYears">From this many completed years of service.</param>
/// <param name="Days">Days of annual leave a year.</param>
public sealed record EntitlementTier(int MinServiceYears, decimal Days);

/// <summary>At least <paramref name="MinDays"/> for employees at most <paramref name="MaxAge"/> or at least <paramref name="MinAge"/> years old.</summary>
public sealed record AgeMinimum(int? MaxAge, int? MinAge, decimal MinDays)
{
    public bool Applies(int age) => (MaxAge is null || age <= MaxAge) && (MinAge is null || age >= MinAge);
}

/// <summary>Days added after the age minimum for employees the criterion matches, e.g. +4 for underground work.</summary>
public sealed record ExtraDayRule(PolicyCriterion Criterion, decimal Days);

/// <summary>
/// The annual leave rules of a policy version (docs/domain/izin-kurallari.md §2). The seed holds the law's values;
/// a company may set others, and those below the law are flagged (<see cref="LegalMinimums"/>) but saved.
/// </summary>
public sealed record AnnualLeaveRules(
    int MinServiceYearsForEntitlement,
    IReadOnlyList<EntitlementTier> Tiers,
    IReadOnlyList<AgeMinimum> AgeMinimums,
    IReadOnlyList<ExtraDayRule> ExtraDayRules,
    AccrualMethod AccrualMethod,
    TenDayBlockRule TenDayBlockRule,
    bool AllowTravelDays,
    int MaxTravelDays,
    decimal? CarryOverLimitDays,
    bool UseGroupSeniority,
    bool UnpaidLeaveShiftsAccrualDate,
    decimal AdvanceLimitDays,
    bool AllowBeforeFirstEntitlement,
    bool AllowHalfDay,
    bool AllowHourly,
    LeaveRounding Rounding,
    bool TransferBalanceOnGroupTransfer,
    int? MinNoticeDays,
    decimal? MaxDaysPerRequest,
    bool AllowPastDatesForHr)
{
    public const int MaxRules = 20;

    /// <summary>
    /// Days of annual leave for <paramref name="completedYears"/> of service at <paramref name="age"/> (unknown age:
    /// no age minimum): the tier's days, raised to the age minimum, then the extra days of the rules that match.
    /// </summary>
    public decimal Entitlement(int completedYears, int? age, Func<PolicyCriterion, bool> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        if (completedYears < Math.Max(MinServiceYearsForEntitlement, 0))
        {
            return 0;
        }

        var days = Tiers.Where(t => t.MinServiceYears <= completedYears).MaxBy(t => t.MinServiceYears)?.Days ?? 0;
        if (age is { } years)
        {
            days = AgeMinimums.Where(m => m.Applies(years)).Select(m => m.MinDays).Append(days).Max();
        }

        return days + ExtraDayRules.Where(r => matches(r.Criterion)).Sum(r => r.Days);
    }
}

/// <param name="ValidFrom">First day in force.</param>
/// <param name="Rules">The annual leave rules.</param>
public sealed record LeavePolicyVersion(DateOnly ValidFrom, AnnualLeaveRules Rules) : IVersioned;

/// <summary>
/// An annual leave policy (docs/domain/izin-kurallari.md §1): versioned rules, assigned to employees by the rules of
/// their company or by hand. The default policy applies when nothing else does; one may be the default of every
/// company and one the default of each company. The code never changes.
/// </summary>
[Audited]
public sealed class LeavePolicy : AuditableAggregateRoot<LeavePolicyId>
{
    public const int CodeMaxLength = 30;
    public const int NameMaxLength = 100;

    private LeavePolicy()
    {
    }

    public string Code { get; private set; } = null!;

    public LocalizedString Name { get; private set; } = null!;

    /// <summary>The company it belongs to; null for every company.</summary>
    public Guid? CompanyId { get; private set; }

    /// <summary>Applies when no rule and no override does: in its company, or in every company without one.</summary>
    public bool IsDefault { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyList<LeavePolicyVersion> Versions { get; private set; } = [];

    public static Result<LeavePolicy> Create(Code code, LocalizedString name, Guid? companyId, bool isDefault, IReadOnlyList<LeavePolicyVersion> versions)
    {
        ArgumentNullException.ThrowIfNull(code);
        var initial = Leave.Versions.Initial(versions);
        if (initial.IsFailure)
        {
            return initial.Error;
        }

        var policy = new LeavePolicy
        {
            Id = EntityId.New<LeavePolicyId>(),
            Code = code.Value,
            Name = name ?? throw new ArgumentNullException(nameof(name)),
            CompanyId = companyId,
            IsActive = true,
            Versions = initial.Value,
        };
        policy.SetDefault(isDefault);
        return policy;
    }

    /// <summary>Changes the policy; versions that have started on <paramref name="today"/> stay as they are.</summary>
    public Result Update(LocalizedString name, bool isDefault, bool isActive, IReadOnlyList<LeavePolicyVersion> versions, DateOnly today)
    {
        var replaced = Leave.Versions.Replace(Versions, versions, today);
        if (replaced.IsFailure)
        {
            return replaced.Error;
        }

        if (isDefault && !isActive)
        {
            return Error.Validation([new FieldError("isActive", LeavePolicyErrors.DefaultInactiveCode)]);
        }

        Name = name ?? throw new ArgumentNullException(nameof(name));
        IsActive = isActive;
        Versions = replaced.Value;
        SetDefault(isDefault);
        return Result.Success();
    }

    /// <summary>No longer the default: another policy of the same company became it.</summary>
    public void ClearDefault() => IsDefault = false;

    public LeavePolicyVersion? VersionOn(DateOnly day) => Leave.Versions.On(Versions, day);

    private void SetDefault(bool isDefault)
    {
        if (isDefault && !IsDefault)
        {
            Raise(new LeavePolicyMadeDefault(Id, CompanyId));
        }

        IsDefault = isDefault;
    }
}

public static class LeavePolicyErrors
{
    public const string DefaultInactiveCode = "hr.leave.errors.default_policy_inactive";
}

/// <summary>A policy became the default of its company (or of every company): the previous default is not any more.</summary>
public sealed record LeavePolicyMadeDefault(LeavePolicyId PolicyId, Guid? CompanyId) : IDomainEvent;
