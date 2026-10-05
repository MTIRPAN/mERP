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
        new AiTenantSettings(true, [pattern], 0, AiTransferBasis.StandardContract).Allows(feature).ShouldBe(expected);

    [Fact]
    public void Allows_IsFalse_WhenTenantIsDisabled() =>
        new AiTenantSettings(false, ["*"], 0, AiTransferBasis.StandardContract).Allows("recruitment.cv_analysis").ShouldBeFalse();

    [Fact]
    public void Allows_IsFalse_WithoutKvkkTransferBasis() =>
        new AiTenantSettings(true, ["*"], 0).Allows("recruitment.cv_analysis").ShouldBeFalse();

    [Fact]
    public void ForTenant_TakesTransferBasisFromDefaultsOrTenant()
    {
        var options = new AiOptions
        {
            Defaults = new AiTenantOptions { Enabled = true, Features = ["*"], TransferBasis = AiTransferBasis.StandardContract, TransferReference = "KEP 2026/1" },
            Tenants = { ["acme"] = new AiTenantOptions { TransferBasis = AiTransferBasis.BindingCorporateRules } },
        };

        options.ForTenant("acme").TransferBasis.ShouldBe(AiTransferBasis.BindingCorporateRules);
        options.ForTenant("other").TransferReference.ShouldBe("KEP 2026/1");
    }

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
    public void DefaultModel_IsSonnet() => new AiOptions().Model.ShouldBe("claude-sonnet-5-5");

    [Theory]
    [InlineData("claude-sonnet-5-5", 2 + 1 + 0.1 + 0.5)]
    [InlineData("CLAUDE-SONNET-5-5", 2 + 1 + 0.1 + 0.5)]
    [InlineData("claude-opus-5-5", 4 + 2 + 0.1 + 1)]
    [InlineData("some-future-model", 4 + 2 + 0.1 + 1)]
    public void Pricing_UsesBuiltInPrices_AndTheMostExpensiveForUnknownModels(string model, double expected) =>
        new AiOptions().PricingFor(model).Cost(input: 1_000_000, output: 100_000, cacheRead: 500_000, cacheWrite: 200_000)
            .ShouldBe((decimal)expected);

    [Fact]
    public void Pricing_FromConfiguration_ReplacesBuiltInPrices()
    {
        var options = new AiOptions
        {
            Pricing = { ["claude-sonnet-5-5"] = new AiPricingOptions { InputPerMillionTokens = 1, OutputPerMillionTokens = 5 } },
        };

        options.PricingFor("claude-sonnet-5-5").Cost(1_000_000, 1_000_000, 0, 0).ShouldBe(6m);
    }

    [Fact]
    public void BudgetPeriod_StartsAtMidnight_InIstanbul()
    {
        AiBudgetPeriod.Start(2026, 10).ShouldBe(new DateTimeOffset(2026, 9, 30, 21, 0, 0, TimeSpan.Zero));
        AiBudgetPeriod.MonthOf(new DateTimeOffset(2026, 9, 30, 21, 30, 0, TimeSpan.Zero)).ShouldBe((2026, 10));
        AiBudgetPeriod.MonthOf(new DateTimeOffset(2026, 9, 30, 20, 59, 0, TimeSpan.Zero)).ShouldBe((2026, 9));
    }
}
