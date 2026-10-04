using System.Net;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Jobs;
using Merp.Infrastructure.MultiTenancy;
using Merp.Infrastructure.Persistence.Auditing;
using Merp.Modules.Platform.Infrastructure.Persistence;
using Merp.Testing.Containers;
using Merp.Testing.Web;
using Merp.Web.Modules;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.BuildingBlocks.IntegrationTests.MultiTenancy;

public sealed class TenantIsolationFactory(PostgresFixture postgres) : MerpWebApplicationFactory(postgres)
{
    public TenantInfo TenantA { get; private set; } = null!;

    public TenantInfo TenantB { get; private set; } = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        TenantA = await CreateTenantAsync("alpha");
        TenantB = await CreateTenantAsync("beta");
        await CreateTenantAsync("gamma", active: false);

        await SeedAsync("alpha", "secret-of-alpha");
        await SeedAsync("beta", "secret-of-beta");
    }

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IModule>(new TenantProbeModule());

    private async Task SeedAsync(string tenantCode, string marker)
    {
        var tenant = (await Services.GetRequiredService<ITenantStore>().FindByCodeAsync(tenantCode))!;
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenant.ToTenantInfo(), tenant.ConnectionString);
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        db.AuditEntries.Add(new AuditEntry { EntityType = marker, EntityId = "1", Action = AuditAction.Insert, OccurredAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }
}

public sealed class TenantIsolationTests(TenantIsolationFactory factory) : IClassFixture<TenantIsolationFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("alpha", "secret-of-alpha")]
    [InlineData("beta", "secret-of-beta")]
    public async Task AuthenticatedUser_OnlyReachesOwnTenantDatabase(string tenant, string expectedData)
    {
        var probe = await factory.CreateClientFor("user-1", tenant).GetFromJsonAsync<TenantProbe>("/api/probe/tenant", Ct);

        probe!.Tenant.ShouldBe(tenant);
        probe.Data.ShouldBe([expectedData]);
    }

    [Fact]
    public async Task HeadersAndQueryStrings_CannotSwitchTenant()
    {
        var client = factory.CreateClientFor("user-1", "alpha");
        client.DefaultRequestHeaders.Add("X-Tenant", "beta");
        client.DefaultRequestHeaders.Add("X-Tenant-Id", factory.TenantB.Id.ToString());

        var probe = await client.GetFromJsonAsync<TenantProbe>("/api/probe/tenant?tenant=beta&tenantCode=beta", Ct);

        probe!.Tenant.ShouldBe("alpha");
        probe.Data.ShouldNotContain("secret-of-beta");
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("gamma")] // inactive
    [InlineData(null)] // no tenant claim
    public async Task UnknownInactiveOrMissingTenantClaim_Returns401(string? tenant)
    {
        var response = await factory.CreateClientFor("user-1", tenant).GetAsync("/api/probe/tenant", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct);
        problem!.Extensions["code"]!.ToString().ShouldBe("common.errors.unknown_tenant");
    }

    [Fact]
    public async Task AnonymousRequest_ToProtectedEndpoint_Returns401() =>
        (await factory.CreateClient().GetAsync("/api/probe/tenant", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task PublicRoute_ResolvesTenantFromUrl()
    {
        var probe = await factory.CreateClient().GetFromJsonAsync<TenantProbe>("/api/public/beta/probe", Ct);

        probe!.Tenant.ShouldBe("beta");
        probe.Data.ShouldBe(["secret-of-beta"]);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("gamma")]
    public async Task PublicRoute_WithUnknownOrInactiveTenant_Returns404(string tenant) =>
        (await factory.CreateClient().GetAsync($"/api/public/{tenant}/probe", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

    [Fact]
    public async Task PublicRoute_DropsTokenOfAnotherTenant()
    {
        var probe = await factory.CreateClientFor("user-1", "alpha").GetFromJsonAsync<TenantProbe>("/api/public/beta/probe", Ct);

        probe!.Tenant.ShouldBe("beta");
        probe.Authenticated.ShouldBeFalse();
    }

    [Fact]
    public async Task TenantJobRunner_VisitsActiveTenants_WithTheirOwnContext()
    {
        var visited = new List<(string Tenant, bool IsSystem, List<string> Data)>();

        await factory.Services.GetRequiredService<ITenantJobRunner>().RunForEachTenantAsync(
            async (services, token) =>
            {
                var db = services.GetRequiredService<PlatformDbContext>();
                visited.Add((
                    services.GetRequiredService<ITenantContext>().RequiredTenant.Code,
                    services.GetRequiredService<ICurrentUser>().IsSystem,
                    [.. db.AuditEntries.Select(e => e.EntityType)]));
                await Task.CompletedTask;
            },
            Ct);

        visited.Select(v => v.Tenant).ShouldBe(["alpha", "beta"], ignoreOrder: true);
        visited.ShouldAllBe(v => v.IsSystem);
        visited.Single(v => v.Tenant == "alpha").Data.ShouldBe(["secret-of-alpha"]);
    }
}
