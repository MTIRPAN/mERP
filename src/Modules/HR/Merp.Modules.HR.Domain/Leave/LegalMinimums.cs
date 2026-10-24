namespace Merp.Modules.HR.Domain.Leave;

/// <summary>A setting below what Turkish law gives (docs/domain/izin-kurallari.md §1): a warning, the record is saved.</summary>
/// <param name="Code">Localization key of the warning.</param>
/// <param name="Args">Its parameters.</param>
/// <param name="ValidFrom">The version it was found in.</param>
public sealed record LegalMinimumViolation(string Code, IReadOnlyDictionary<string, object?> Args, DateOnly? ValidFrom = null);

/// <summary>
/// The legal minimums of 4857 (md. 53–56, Ek md. 2, md. 74) the seed carries (docs/domain/izin-kurallari.md §2–3),
/// as checks of the tenant's own settings. Not legal advice; the values follow the seed.
/// </summary>
public static class LegalMinimums
{
    /// <summary>The day the paternity and maternity leaves changed (Ek md. 2, md. 74).</summary>
    public static readonly DateOnly ParentalLeaveChange = new(2026, 5, 1);

    private const int LegalMinServiceYears = 1;
    private const decimal YoungOrOldMinDays = 20;
    private const int YoungMaxAge = 18;
    private const int OldMinAge = 50;
    private const int LegalTravelDays = 4;
    private const int AgeOfAdult = 30;

    /// <summary>The law's days for completed years of service: 14, 20 from 5 years, 26 from 15.</summary>
    public static decimal AnnualDays(int completedYears) => completedYears switch
    {
        < LegalMinServiceYears => 0,
        < 5 => 14,
        < 15 => 20,
        _ => 26,
    };

    /// <summary>The settings of a policy's annual leave rules below the law.</summary>
    public static IReadOnlyList<LegalMinimumViolation> Check(AnnualLeaveRules rules, DateOnly? validFrom = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var found = new List<LegalMinimumViolation>();
        void Add(string code, IReadOnlyDictionary<string, object?>? args = null) =>
            found.Add(new LegalMinimumViolation("hr.leave.legal." + code, args ?? new Dictionary<string, object?>(), validFrom));

        if (rules.MinServiceYearsForEntitlement > LegalMinServiceYears)
        {
            Add("min_service", new Dictionary<string, object?> { ["years"] = rules.MinServiceYearsForEntitlement, ["legal"] = LegalMinServiceYears });
        }

        // Each band of the law once, at its first year below the law; extra day rules are left out
        static bool None(PolicyCriterion _) => false;
        foreach (var (from, to) in new[] { (1, 4), (5, 14), (15, 50) })
        {
            var low = Enumerable.Range(from, to - from + 1).FirstOrDefault(y => rules.Entitlement(y, AgeOfAdult, None) < AnnualDays(y));
            if (low > 0)
            {
                Add("tier", new Dictionary<string, object?> { ["years"] = low, ["days"] = rules.Entitlement(low, AgeOfAdult, None), ["legal"] = AnnualDays(low) });
            }
        }

        foreach (var age in new[] { YoungMaxAge, OldMinAge })
        {
            var days = rules.Entitlement(LegalMinServiceYears, age, None);
            if (rules.MinServiceYearsForEntitlement <= LegalMinServiceYears && days < YoungOrOldMinDays)
            {
                Add("age", new Dictionary<string, object?> { ["age"] = age, ["days"] = days, ["legal"] = YoungOrOldMinDays });
            }
        }

        if (rules.CarryOverLimitDays is { } limit)
        {
            Add("carry_over_limit", new Dictionary<string, object?> { ["days"] = limit });
        }

        if (rules.TenDayBlockRule == TenDayBlockRule.Off)
        {
            Add("ten_day_block_off");
        }

        if (!rules.AllowTravelDays || rules.MaxTravelDays < LegalTravelDays)
        {
            Add("travel_days", new Dictionary<string, object?> { ["days"] = rules.AllowTravelDays ? rules.MaxTravelDays : 0, ["legal"] = LegalTravelDays });
        }

        return found;
    }

    /// <summary>The versions of a seeded leave type whose days are below the law; other codes have no minimum.</summary>
    public static IReadOnlyList<LegalMinimumViolation> Check(string code, IReadOnlyList<LeaveTypeVersion> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        var found = new List<LegalMinimumViolation>();
        foreach (var version in versions)
        {
            var after = version.ValidFrom >= ParentalLeaveChange;
            var (field, value, legal) = code switch
            {
                "EVLILIK" or "EVLAT_EDINME" or "VEFAT" => ("fixed_days", version.FixedDays, 3m),
                "BABALIK" => ("fixed_days", version.FixedDays, after ? 10m : 5m),
                "DOGUM" => ("fixed_days", version.FixedDays, after ? 168m : 112m),
                "ENGELLI_COCUK" => ("max_days_per_year", version.MaxDaysPerYear, 10m),
                "SUT" => ("daily_hours", version.DailyHours, 1.5m),
                _ => ((string?)null, (decimal?)null, 0m),
            };
            if (field is not null && (value ?? 0) < legal)
            {
                found.Add(new LegalMinimumViolation(
                    "hr.leave.legal.type_" + field,
                    new Dictionary<string, object?> { ["value"] = value ?? 0, ["legal"] = legal },
                    version.ValidFrom));
            }
        }

        return found;
    }
}
