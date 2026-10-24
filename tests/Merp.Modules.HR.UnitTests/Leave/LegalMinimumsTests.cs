using Merp.Modules.HR.Domain.Leave;
using Merp.Modules.HR.Infrastructure.Leave;

namespace Merp.Modules.HR.UnitTests.Leave;

/// <summary>PLAN L1 acceptance: a setting below the law is a warning (docs/domain/izin-kurallari.md §1).</summary>
public sealed class LegalMinimumsTests
{
    private static readonly AnnualLeaveRules Legal = LeaveSeeder.LegalRules();

    private static IEnumerable<string> Codes(AnnualLeaveRules rules) => LegalMinimums.Check(rules).Select(v => v.Code);

    [Fact]
    public void FewerDaysThanTheLaw_IsFlaggedOncePerBand()
    {
        var rules = Legal with { Tiers = [new EntitlementTier(1, 12), new EntitlementTier(5, 20), new EntitlementTier(15, 24)] };

        var found = LegalMinimums.Check(rules).Where(v => v.Code == "hr.leave.legal.tier").ToList();

        found.Select(v => v.Args["years"]).ShouldBe([1, 15]);
        found[0].Args["legal"].ShouldBe(14m);
        found[1].Args["days"].ShouldBe(24m);
    }

    [Fact]
    public void MoreDaysThanTheLaw_IsFine() =>
        Codes(Legal with { Tiers = [new EntitlementTier(1, 16), new EntitlementTier(3, 22), new EntitlementTier(10, 30)] }).ShouldBeEmpty();

    [Fact]
    public void TheOtherSettingsBelowTheLaw_AreFlagged() =>
        Codes(Legal with
        {
            MinServiceYearsForEntitlement = 2,
            CarryOverLimitDays = 10,
            TenDayBlockRule = TenDayBlockRule.Off,
            MaxTravelDays = 2,
        }).ShouldBe(
            ["hr.leave.legal.min_service", "hr.leave.legal.tier", "hr.leave.legal.carry_over_limit", "hr.leave.legal.ten_day_block_off", "hr.leave.legal.travel_days"],
            ignoreOrder: true);

    [Fact]
    public void MissingAgeMinimums_AreFlaggedForTheYoungAndTheOld() =>
        LegalMinimums.Check(Legal with { AgeMinimums = [] }).Where(v => v.Code == "hr.leave.legal.age").Select(v => v.Args["age"]).ShouldBe([18, 50]);

    [Fact]
    public void PaternityLeave_IsCheckedAgainstTheLawOfEachVersion()
    {
        LeaveTypeVersion[] versions = [new(DateOnly.MinValue, FixedDays: 5), new(LegalMinimums.ParentalLeaveChange, FixedDays: 5)];

        var found = LegalMinimums.Check("BABALIK", versions).ShouldHaveSingleItem();

        found.Code.ShouldBe("hr.leave.legal.type_fixed_days");
        found.ValidFrom.ShouldBe(LegalMinimums.ParentalLeaveChange);
        LegalMinimums.Check("IDARI", [new LeaveTypeVersion(DateOnly.MinValue)]).ShouldBeEmpty();
    }
}
