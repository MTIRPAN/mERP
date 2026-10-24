using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Leave;
using Merp.Modules.HR.Infrastructure.Leave;

namespace Merp.Modules.HR.UnitTests.Leave;

/// <summary>PLAN L1 acceptance: the seed is the tables of docs/domain/izin-kurallari.md §2 and §3.</summary>
public sealed class LeaveSeedTests
{
    [Fact]
    public void LegalPolicy_IsTheTableOfSection2()
    {
        var rules = LeaveSeeder.LegalRules();

        rules.MinServiceYearsForEntitlement.ShouldBe(1);
        rules.Tiers.ShouldBe([new EntitlementTier(1, 14), new EntitlementTier(5, 20), new EntitlementTier(15, 26)]);
        rules.AgeMinimums.ShouldBe([new AgeMinimum(18, null, 20), new AgeMinimum(null, 50, 20)]);
        var underground = rules.ExtraDayRules.ShouldHaveSingleItem();
        underground.Days.ShouldBe(4);
        underground.Criterion.Kind.ShouldBe(CriterionKind.CustomField);
        underground.Criterion.FieldCode.ShouldBe("underground");
        underground.Criterion.Values.ShouldBe(["true"]);
        rules.AccrualMethod.ShouldBe(AccrualMethod.AnniversaryLumpSum);
        rules.TenDayBlockRule.ShouldBe(TenDayBlockRule.Warn);
        rules.AllowTravelDays.ShouldBeTrue();
        rules.MaxTravelDays.ShouldBe(4);
        rules.CarryOverLimitDays.ShouldBeNull();
        rules.UseGroupSeniority.ShouldBeFalse();
        rules.UnpaidLeaveShiftsAccrualDate.ShouldBeFalse();
        rules.AdvanceLimitDays.ShouldBe(0);
        rules.AllowBeforeFirstEntitlement.ShouldBeFalse();
        rules.AllowHalfDay.ShouldBeTrue();
        rules.AllowHourly.ShouldBeFalse();
        rules.Rounding.ShouldBe(LeaveRounding.None);
        rules.TransferBalanceOnGroupTransfer.ShouldBeTrue();
        LegalMinimums.Check(rules).ShouldBeEmpty();
    }

    [Fact]
    public void LeaveTypes_AreTheTableOfSection3()
    {
        var types = LeaveSeeder.Types().ToDictionary(t => t.Code);

        types.Keys.ShouldBe(
            ["YILLIK", "EVLILIK", "EVLAT_EDINME", "VEFAT", "BABALIK", "ENGELLI_COCUK", "DOGUM", "DOGUM_SONRASI_UCRETSIZ", "SUT", "RAPOR", "UCRETSIZ", "IDARI", "YOL", "SAATLIK"],
            ignoreOrder: true);
        types["YILLIK"].Profile.DeductsFrom.ShouldBe("YILLIK");
        types["YILLIK"].Profile.DurationBasis.ShouldBe(DurationBasis.WorkingDays);
        types["EVLILIK"].Versions.ShouldHaveSingleItem().FixedDays.ShouldBe(3);
        types["EVLILIK"].Profile.DurationBasis.ShouldBe(DurationBasis.CalendarDays);
        types["EVLILIK"].Profile.RequiresDocument.ShouldBeTrue();
        types["BABALIK"].Versions.Select(v => (v.ValidFrom, v.FixedDays)).ShouldBe([(DateOnly.MinValue, 5m), (new DateOnly(2026, 5, 1), 10m)]);
        types["BABALIK"].Profile.GenderRestriction.ShouldBe(Gender.Male);
        types["DOGUM"].Versions.Select(v => (v.ValidFrom, v.FixedDays, v.DaysBeforeEvent))
            .ShouldBe([(DateOnly.MinValue, 112m, 56m), (new DateOnly(2026, 5, 1), 168m, 56m)]);
        types["DOGUM"].Profile.Payer.ShouldBe(LeavePayer.Sgk);
        types["ENGELLI_COCUK"].Versions.ShouldHaveSingleItem().MaxDaysPerYear.ShouldBe(10);
        types["SUT"].Versions.ShouldHaveSingleItem().DailyHours.ShouldBe(1.5m);
        types["SUT"].Profile.Unit.ShouldBe(LeaveUnit.Hour);
        types["UCRETSIZ"].Profile.Payer.ShouldBe(LeavePayer.None);
        types["YOL"].Versions.ShouldHaveSingleItem().MaxDaysPerRequest.ShouldBe(4);
        types.Values.ShouldAllBe(t => LegalMinimums.Check(t.Code, t.Versions).Count == 0);
    }
}
