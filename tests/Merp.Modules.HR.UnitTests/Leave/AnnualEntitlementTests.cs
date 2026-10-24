using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Leave;
using Merp.Modules.HR.Infrastructure.Leave;

namespace Merp.Modules.HR.UnitTests.Leave;

/// <summary>The entitlement scenarios of docs/domain/izin-kurallari.md §8.2 that the policy alone decides.</summary>
public sealed class AnnualEntitlementTests
{
    private static readonly AnnualLeaveRules Legal = LeaveSeeder.LegalRules();

    private static bool Nothing(PolicyCriterion criterion) => false;

    private static PolicySubject Subject(string? level = null, bool underground = false) => new(
        Guid.CreateVersion7(),
        null,
        EmploymentType.Indefinite,
        null,
        [],
        level,
        new Dictionary<string, string?> { ["underground"] = underground ? "true" : "false" });

    [Theory]
    [InlineData(0, 30, 0)]
    [InlineData(4, 30, 14)] // E2: seniority 15.03.2021, entitlement on 15.03.2025
    [InlineData(5, 30, 20)] // E2b: on 15.03.2026
    [InlineData(15, 40, 26)] // E3: seniority 01.07.2011, on 01.07.2026
    [InlineData(3, 50, 20)] // E4: born 10.02.1976, 3 years on 01.06.2026
    [InlineData(1, 18, 20)] // E5: born 01.08.2007, 1 year on 01.07.2026
    [InlineData(14, 51, 20)]
    public void LegalPolicy_GivesTheLawsDays(int years, int age, decimal expected) =>
        Legal.Entitlement(years, age, Nothing).ShouldBe(expected);

    [Fact]
    public void E6_UndergroundWork_AddsFourDaysAfterTheAgeMinimum()
    {
        var underground = Subject(underground: true);
        var surface = Subject();

        Legal.Entitlement(3, 50, c => c.Matches(underground)).ShouldBe(24);
        Legal.Entitlement(3, 50, c => c.Matches(surface)).ShouldBe(20);
    }

    [Fact]
    public void E7_ExtraDaysForALevel_AreAddedToTheTier()
    {
        var rules = Legal with { ExtraDayRules = [new ExtraDayRule(new PolicyCriterion(CriterionKind.JobLevel, ["M1", "M2"]), 2)] };
        var manager = Subject("m1");
        var staff = Subject("U1");

        rules.Entitlement(2, 35, c => c.Matches(manager)).ShouldBe(16);
        rules.Entitlement(2, 35, c => c.Matches(staff)).ShouldBe(14);
    }

    [Fact]
    public void UnknownAge_SkipsTheAgeMinimum() => Legal.Entitlement(1, null, Nothing).ShouldBe(14);

    [Fact]
    public void E11_PaternityLeave_FollowsTheVersionOfTheBirthDate()
    {
        var paternity = LeaveSeeder.Types().Single(t => t.Code == "BABALIK").Versions;

        Versions.On(paternity, new DateOnly(2026, 4, 20))!.FixedDays.ShouldBe(5);
        Versions.On(paternity, new DateOnly(2026, 5, 10))!.FixedDays.ShouldBe(10);
    }
}
