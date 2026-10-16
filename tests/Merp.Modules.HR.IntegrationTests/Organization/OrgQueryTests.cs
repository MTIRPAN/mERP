using System.Net.Http.Json;
using Merp.Application.Abstractions.Security;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Contracts.Organization;
using Merp.Modules.HR.Domain.Organization;
using Merp.Modules.Platform.Contracts.Approvals;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.HR.IntegrationTests.Organization;

/// <summary>What other modules get from HR: <see cref="IOrgQuery"/>, the resolvers and the scope types (D37).</summary>
public sealed class OrgQueryTests(HrFactory factory) : IClassFixture<HrFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Subtree_AndUnitManager_FollowTheTree()
    {
        var admin = factory.Admin();
        var company = await HrFactory.CreateCompanyAsync(admin);
        var sales = await HrFactory.CreateUnitAsync(admin, company);
        var region = await HrFactory.CreateUnitAsync(admin, company, sales, "Marmara", OrgUnitType.Department);
        var team = await HrFactory.CreateUnitAsync(admin, company, region, "İstanbul", OrgUnitType.Team);
        var job = await HrFactory.CreateJobAsync(admin);
        var director = await HrFactory.CreatePositionAsync(admin, sales, job);
        var version = (await admin.GetFromJsonAsync<OrgUnitDetails>($"/api/hr/org-units/{sales}", Ct))!.Version;
        await admin.PutAsJsonAsync($"/api/hr/org-units/{sales}", new { version, name = "Satış", type = "Directorate", managerPositionId = director }, Ct);

        var (subtree, manager, unknown) = await factory.InTenantAsync(async services =>
        {
            var query = services.GetRequiredService<IOrgQuery>();
            return (
                await query.GetSubtreeUnitIdsAsync(region, Ct),
                await query.GetUnitManagerAsync(team, Ct),
                await query.GetUnitManagerAsync(Guid.CreateVersion7(), Ct));
        });

        subtree.ShouldBe([region, team], ignoreOrder: true);
        manager.ShouldNotBeNull();
        manager.OrgUnitId.ShouldBe(sales);
        manager.PositionId.ShouldBe(director);
        manager.Holders.ShouldBeEmpty();
        unknown.ShouldBeNull();
    }

    [Fact]
    public async Task HR_RegistersItsResolversAndScopeTypes_WithPlatform()
    {
        var keys = await factory.InTenantAsync(services =>
            Task.FromResult(services.GetServices<IApproverResolver>().Select(r => r.Key).ToList()));
        var scopeTypes = factory.Services.GetRequiredService<IScopeTypeRegistry>();

        keys.ShouldContain("line-manager");
        keys.ShouldContain("org-unit-manager");
        keys.ShouldContain("position");
        scopeTypes.Find(AccessScopeTypes.OrgUnit).ShouldSatisfyAllConditions(
            t => t.ShouldNotBeNull(),
            t => t!.RequiresId.ShouldBeTrue(),
            t => t!.SupportsSubUnits.ShouldBeTrue());
        scopeTypes.Find(AccessScopeTypes.ManagedTeam)!.RequiresId.ShouldBeFalse();
    }
}
