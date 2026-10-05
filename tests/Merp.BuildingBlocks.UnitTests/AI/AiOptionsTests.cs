using Merp.Infrastructure.AI;

namespace Merp.BuildingBlocks.UnitTests.AI;

public sealed class AiOptionsTests
{
    [Theory]
    [InlineData("*", "recruitment.cv_analysis", true)]
    [InlineData("recruitment.*", "recruitment.cv_analysis", true)]
    [InlineData("recruitment.cv_analysis", "recruitment.cv_analysis", true)]
    [InlineData("RECRUITMENT.CV_ANALYSIS", "recruitment.cv_analysis", true)]
    [InlineData("hr.*", "recruitment.cv_analysis", false)]
    [InlineData("recruitment.cv", "recruitment.cv_analysis", false)]
    public void Allows_MatchesFeaturePatterns(string pattern, string feature, bool expected) =>
        new AiTenantSettings(true, [pattern], 0).Allows(feature).ShouldBe(expected);

    [Fact]
    public void Allows_IsFalse_WhenTenantIsDisabled() =>
        new AiTenantSettings(false, ["*"], 0).Allows("recruitment.cv_analysis").ShouldBeFalse();

    [Fact]
    public void ForTenant_FallsBackToDefaults_PerValue()
    {
        var options = new AiOptions
        {
            Defaults = new AiTenantOptions { Enabled = false, Features = ["*"], MonthlyBudgetUsd = 10 },
            Tenants = { ["acme"] = new AiTenantOptions { Enabled = true } },
        };

        options.ForTenant("ACME").ShouldBe(new AiTenantSettings(true, options.Defaults.Features!, 10));
        options.ForTenant("other").Enabled.ShouldBeFalse();
        options.ForTenant(null).Enabled.ShouldBeFalse();
    }

    [Fact]
    public void Pricing_ComputesCost_PerMillionTokens() =>
        new AiPricingOptions().Cost(input: 1_000_000, output: 100_000, cacheRead: 500_000, cacheWrite: 200_000)
            .ShouldBe(4m + 2m + 0.1m + 1m);

    [Fact]
    public void BudgetPeriod_StartsAtMidnight_InIstanbul()
    {
        AiBudgetPeriod.Start(2026, 10).ShouldBe(new DateTimeOffset(2026, 9, 30, 21, 0, 0, TimeSpan.Zero));
        AiBudgetPeriod.MonthOf(new DateTimeOffset(2026, 9, 30, 21, 30, 0, TimeSpan.Zero)).ShouldBe((2026, 10));
        AiBudgetPeriod.MonthOf(new DateTimeOffset(2026, 9, 30, 20, 59, 0, TimeSpan.Zero)).ShouldBe((2026, 9));
    }
}
