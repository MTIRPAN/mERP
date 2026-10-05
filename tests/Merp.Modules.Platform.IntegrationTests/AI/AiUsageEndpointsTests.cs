using System.Net;
using System.Net.Http.Json;
using Merp.Infrastructure.AI;
using Merp.Infrastructure.MultiTenancy;
using Merp.Modules.Platform.Api.Endpoints;
using Merp.Modules.Platform.Application;
using Merp.Testing.Containers;
using Merp.Testing.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Platform.IntegrationTests.AI;

public sealed class AiUsageFactory(PostgresFixture postgres) : MerpWebApplicationFactory(postgres)
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await CreateTenantAsync("acme");
        await CreateTenantAsync("globex");
    }

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Ai:Tenants:acme:MonthlyBudgetUsd"] = "25",
        ["Ai:Tenants:globex:Enabled"] = "false",
    };

    /// <summary>Writes a usage record through the ledger, as the AI service does.</summary>
    public async Task RecordAsync(string tenantCode, AiUsageEntry entry)
    {
        var tenant = (await Services.GetRequiredService<ITenantStore>().FindByCodeAsync(tenantCode, TestContext.Current.CancellationToken))!;
        await using var scope = Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        await scope.ServiceProvider.GetRequiredService<IAiUsageLedger>().RecordAsync(entry, TestContext.Current.CancellationToken);
    }
}

public sealed class AiUsageEndpointsTests(AiUsageFactory factory) : IClassFixture<AiUsageFactory>
{
    private const string Url = "/api/platform/ai-usage/summary";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Summary_TotalsTheMonthPerFeature()
    {
        var now = DateTimeOffset.UtcNow;
        var (year, month) = AiBudgetPeriod.MonthOf(now);
        var previousMonth = AiBudgetPeriod.Start(year, month).AddDays(-1);
        await factory.RecordAsync("acme", Entry("recruitment.cv_analysis", now, AiUsageOutcome.Succeeded, 0.05m));
        await factory.RecordAsync("acme", Entry("recruitment.cv_analysis", now, AiUsageOutcome.Refused, 0.01m));
        await factory.RecordAsync("acme", Entry("recruitment.job_ad_draft", now, AiUsageOutcome.Succeeded, 0.02m));
        await factory.RecordAsync("acme", Entry("recruitment.cv_analysis", previousMonth, AiUsageOutcome.Succeeded, 9m));

        var summary = (await Client("acme").GetFromJsonAsync<AiUsageSummaryResponse>(Url, Ct))!;

        summary.Month.ShouldBe($"{year:D4}-{month:D2}");
        summary.ProviderConfigured.ShouldBeTrue();
        summary.Enabled.ShouldBeTrue();
        summary.Model.ShouldBe("claude-opus-5-5");
        summary.MonthlyBudgetUsd.ShouldBe(25m);
        summary.CostUsd.ShouldBe(0.08m);
        summary.Requests.ShouldBe(3);
        summary.Features.Select(f => (f.Feature, f.Requests, f.Succeeded, f.CostUsd)).ShouldBe(
        [
            ("recruitment.cv_analysis", 2, 1, 0.06m),
            ("recruitment.job_ad_draft", 1, 1, 0.02m),
        ]);
        summary.Features[0].InputTokens.ShouldBe(2 * (1_000 + 200 + 50));
    }

    [Fact]
    public async Task Summary_ForAnEarlierMonth_UsesThatMonth()
    {
        await factory.RecordAsync("acme", Entry("recruitment.cv_analysis", new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero), AiUsageOutcome.Succeeded, 1.5m));

        var summary = (await Client("acme").GetFromJsonAsync<AiUsageSummaryResponse>($"{Url}?month=2026-03", Ct))!;

        summary.Month.ShouldBe("2026-03");
        summary.CostUsd.ShouldBe(1.5m);
    }

    [Fact]
    public async Task Summary_OnlyShowsTheCallersTenant()
    {
        await factory.RecordAsync("acme", Entry("recruitment.cv_analysis", DateTimeOffset.UtcNow, AiUsageOutcome.Succeeded, 3m));

        var summary = (await Client("globex").GetFromJsonAsync<AiUsageSummaryResponse>(Url, Ct))!;

        summary.Requests.ShouldBe(0);
        summary.Features.ShouldBeEmpty();
        summary.Enabled.ShouldBeFalse();
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("march")]
    public async Task Summary_WithInvalidMonth_Returns400(string month)
    {
        var response = await Client("acme").GetAsync($"{Url}?month={month}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("\"month\"");
    }

    [Fact]
    public async Task Summary_WithoutPermission_Returns403() =>
        (await factory.CreateClientFor("user-1", "acme").GetAsync(Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

    private HttpClient Client(string tenant) => factory.CreateClientFor("admin-1", tenant, PlatformPermissions.AiUsage.Read);

    private static AiUsageEntry Entry(string feature, DateTimeOffset at, AiUsageOutcome outcome, decimal cost) =>
        new(at, feature, "v1", "claude-opus-5-5", outcome, Guid.CreateVersion7(), 1_000, 300, 200, 50, cost, 1200, null, null, null);
}
