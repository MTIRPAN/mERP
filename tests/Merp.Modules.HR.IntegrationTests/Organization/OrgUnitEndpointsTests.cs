using System.Net;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Domain.Organization;
using Merp.Testing.Web;

namespace Merp.Modules.HR.IntegrationTests.Organization;

public sealed class OrgUnitEndpointsTests(HrFactory factory) : IClassFixture<HrFactory>
{
    private const string Url = "/api/hr/org-units";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_ThenGet_ReturnsTheUnitWithItsCompanyAndParent()
    {
        var admin = factory.Admin();
        var company = await HrFactory.CreateCompanyAsync(admin);
        var branch = await HrFactory.CreateBranchAsync(admin, company);
        var sales = await HrFactory.CreateUnitAsync(admin, company);

        var response = await admin.PostAsJsonAsync(
            Url,
            new { companyId = company, parentId = sales, code = "mrm", name = "Marmara Bölgesi", type = "Department", costCenterCode = "MM-110", branchId = branch, validFrom = "2026-01-01" },
            Ct);

        var id = await HrFactory.CreatedIdAsync(response);
        var unit = (await admin.GetFromJsonAsync<OrgUnitDetails>($"{Url}/{id}", Ct))!;
        unit.CompanyId.ShouldBe(company);
        unit.CompanyName.ShouldBe("Acme");
        unit.ParentId.ShouldBe(sales);
        unit.ParentName.ShouldBe("Satış Direktörlüğü");
        unit.Code.ShouldBe("MRM");
        unit.Type.ShouldBe(OrgUnitType.Department);
        unit.CostCenterCode.ShouldBe("MM-110");
        unit.BranchId.ShouldBe(branch);
        unit.BranchName.ShouldBe("Gebze Fabrika");
        unit.ValidFrom.ShouldBe(new DateOnly(2026, 1, 1));
        unit.IsActive.ShouldBeTrue();

        (await factory.PathAsync(id)).ShouldBe($"{await factory.PathAsync(sales)}.{id:N}");
        (await factory.AuditedFieldsAsync(nameof(OrgUnit), id)).ShouldContain(nameof(OrgUnit.CostCenterCode));
    }

    [Fact]
    public async Task Create_WithInvalidInput_Returns400WithFieldErrors()
    {
        var admin = factory.Admin();
        var company = await HrFactory.CreateCompanyAsync(admin);
        var body = new { companyId = company, code = "S 1", name = "", validFrom = "2026-02-01", validTo = "2026-01-31" };

        var response = await admin.PostAsJsonAsync(Url, body, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await Problem.ReadAsync(response);
        problem.Errors!.Keys.ShouldBe(["code", "name", "type", "validTo"], ignoreOrder: true);
        problem.Errors["validTo"].ShouldBe(["Bitiş tarihi başlangıç tarihinden önce olamaz."]);
    }

    [Fact]
    public async Task Create_WithATakenCode_AnUnknownParent_OrAForeignBranch_Returns400()
    {
        var admin = factory.Admin();
        var company = await HrFactory.CreateCompanyAsync(admin);
        var other = await HrFactory.CreateCompanyAsync(admin);
        var foreignUnit = await HrFactory.CreateUnitAsync(admin, other);
        var foreignBranch = await HrFactory.CreateBranchAsync(admin, other);
        await HrFactory.CreatedIdAsync(await admin.PostAsJsonAsync(Url, HrFactory.UnitBody(company, code: "SATIS"), Ct));

        var response = await admin.PostAsJsonAsync(
            Url,
            new { companyId = company, parentId = foreignUnit, code = "SATIS", name = "Satış", type = "Directorate", branchId = foreignBranch },
            Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await Problem.ReadAsync(response);
        problem.Errors!.Keys.ShouldBe(["code", "parentId", "branchId"], ignoreOrder: true);
        problem.Errors["parentId"].ShouldBe(["Üst birim bulunamadı ya da bu şirkete ait değil."]);

        // The same code in another company is fine
        await HrFactory.CreatedIdAsync(await admin.PostAsJsonAsync(Url, HrFactory.UnitBody(other, code: "SATIS"), Ct));
    }

    [Fact]
    public async Task Create_InAnUnknownCompany_Returns400()
    {
        var problem = await Problem.ReadAsync(await factory.Admin().PostAsJsonAsync(Url, HrFactory.UnitBody(Guid.CreateVersion7()), Ct));

        problem.Errors!["companyId"].ShouldBe(["Şirket bulunamadı."]);
    }

    [Fact]
    public async Task Create_InACompanyOutsideTheScope_OrWithoutPermission_Returns403()
    {
        var admin = factory.Admin();
        var own = await HrFactory.CreateCompanyAsync(admin);
        var other = await HrFactory.CreateCompanyAsync(admin);

        var scoped = factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Org.Read, HrPermissions.Org.Manage).WithScopes(AccessScope.Company(own));
        await HrFactory.CreatedIdAsync(await scoped.PostAsJsonAsync(Url, HrFactory.UnitBody(own), Ct));
        (await scoped.PostAsJsonAsync(Url, HrFactory.UnitBody(other), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var reader = factory.CreateClientFor("reader", HrFactory.Tenant, HrPermissions.Org.Read);
        (await reader.PostAsJsonAsync(Url, HrFactory.UnitBody(own), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await factory.CreateClientFor("nobody", HrFactory.Tenant).GetAsync($"{Url}/tree?companyId={own}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UnitsOutsideTheScope_Return404()
    {
        var admin = factory.Admin();
        var own = await HrFactory.CreateCompanyAsync(admin);
        var other = await HrFactory.CreateCompanyAsync(admin);
        var foreign = await HrFactory.CreateUnitAsync(admin, other);
        var details = (await admin.GetFromJsonAsync<OrgUnitDetails>($"{Url}/{foreign}", Ct))!;

        var scoped = factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Org.Read, HrPermissions.Org.Manage).WithScopes(AccessScope.Company(own));

        (await scoped.GetAsync($"{Url}/{foreign}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await scoped.PutAsJsonAsync($"{Url}/{foreign}", new { version = details.Version, name = "X", type = "Department" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await scoped.PostAsJsonAsync($"{Url}/{foreign}/move", new { version = details.Version }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await scoped.GetFromJsonAsync<OrgUnitTree>($"{Url}/tree?companyId={other}", Ct))!.Units.ShouldBeEmpty();
        (await scoped.GetFromJsonAsync<PagedResult<OrgUnitListItem>>($"{Url}?companyId={other}", Ct))!.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Tree_OfThreeLevels_ReportsTheVacancies()
    {
        var admin = factory.Admin();
        var company = await HrFactory.CreateCompanyAsync(admin);
        var office = await HrFactory.CreateUnitAsync(admin, company, name: "Genel Müdürlük");
        var sales = await HrFactory.CreateUnitAsync(admin, company, office, "Satış Direktörlüğü");
        var istanbul = await HrFactory.CreateUnitAsync(admin, company, sales, "İstanbul Satış Ekibi", OrgUnitType.Team);
        var job = await HrFactory.CreateJobAsync(admin, "Satış Temsilcisi");
        await HrFactory.CreatePositionAsync(admin, office, job);
        await HrFactory.CreatePositionAsync(admin, istanbul, job, norm: 4);
        var frozen = await HrFactory.CreatePositionAsync(admin, istanbul, job, norm: 2);
        var position = (await admin.GetFromJsonAsync<PositionDetails>($"/api/hr/positions/{frozen}", Ct))!;
        (await admin.PutAsJsonAsync(
            $"/api/hr/positions/{frozen}",
            new { version = position.Version, orgUnitId = istanbul, jobId = job, norm = 2, status = "Frozen", isManager = false },
            Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var tree = (await admin.GetFromJsonAsync<OrgUnitTree>($"{Url}/tree?companyId={company}", Ct))!;

        tree.CompanyId.ShouldBe(company);
        tree.Units.Select(u => u.Id).ShouldBe([office, istanbul, sales], ignoreOrder: true);
        tree.Units.Single(u => u.Id == office).ParentId.ShouldBeNull();
        tree.Units.Single(u => u.Id == sales).ParentId.ShouldBe(office);
        var team = tree.Units.Single(u => u.Id == istanbul);
        team.ParentId.ShouldBe(sales);
        team.Type.ShouldBe(OrgUnitType.Team);
        (team.PositionCount, team.Norm, team.Filled, team.Vacant).ShouldBe((2, 4, 0, 4));
        tree.Units.Single(u => u.Id == sales).Vacant.ShouldBe(0);

        // The active company header gives the same tree
        var withHeader = factory.Admin();
        withHeader.DefaultRequestHeaders.Add("X-Company-Id", company.ToString());
        (await withHeader.GetFromJsonAsync<OrgUnitTree>($"{Url}/tree", Ct))!.Units.Count.ShouldBe(3);
        (await Problem.ReadAsync(await admin.GetAsync($"{Url}/tree", Ct))).Errors!.Keys.ShouldBe(["companyId"]);
    }

    [Fact]
    public async Task Move_RepointsTheSubUnits_AndTheScopeFollows()
    {
        var admin = factory.Admin();
        var company = await HrFactory.CreateCompanyAsync(admin);
        var sales = await HrFactory.CreateUnitAsync(admin, company, name: "Satış");
        var marketing = await HrFactory.CreateUnitAsync(admin, company, name: "Pazarlama");
        var region = await HrFactory.CreateUnitAsync(admin, company, sales, "Marmara", OrgUnitType.Department);
        var team = await HrFactory.CreateUnitAsync(admin, company, region, "İstanbul", OrgUnitType.Team);
        var salesScoped = factory.CreateClientFor("satis", HrFactory.Tenant, HrPermissions.Org.Read)
            .WithScopes(new AccessScope(AccessScopeTypes.OrgUnit, sales, IncludeSubUnits: true));
        var marketingScoped = factory.CreateClientFor("pazarlama", HrFactory.Tenant, HrPermissions.Org.Read)
            .WithScopes(new AccessScope(AccessScopeTypes.OrgUnit, marketing, IncludeSubUnits: true));
        (await TreeIdsAsync(salesScoped, company)).ShouldBe([sales, region, team], ignoreOrder: true);
        (await TreeIdsAsync(marketingScoped, company)).ShouldBe([marketing]);

        var version = (await admin.GetFromJsonAsync<OrgUnitDetails>($"{Url}/{region}", Ct))!.Version;
        (await admin.PostAsJsonAsync($"{Url}/{region}/move", new { version, parentId = marketing }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var marketingPath = await factory.PathAsync(marketing);
        (await factory.PathAsync(region)).ShouldBe($"{marketingPath}.{region:N}");
        (await factory.PathAsync(team)).ShouldBe($"{marketingPath}.{region:N}.{team:N}");
        (await TreeIdsAsync(salesScoped, company)).ShouldBe([sales]);
        (await TreeIdsAsync(marketingScoped, company)).ShouldBe([marketing, region, team], ignoreOrder: true);
        (await admin.GetFromJsonAsync<OrgUnitDetails>($"{Url}/{team}", Ct))!.ParentId.ShouldBe(region);

        // A scope without its sub units covers the unit only
        var unitOnly = factory.CreateClientFor("bolge", HrFactory.Tenant, HrPermissions.Org.Read)
            .WithScopes(new AccessScope(AccessScopeTypes.OrgUnit, region));
        (await TreeIdsAsync(unitOnly, company)).ShouldBe([region]);
    }

    [Fact]
    public async Task Move_UnderItselfOrASubUnit_Returns400()
    {
        var admin = factory.Admin();
        var company = await HrFactory.CreateCompanyAsync(admin);
        var sales = await HrFactory.CreateUnitAsync(admin, company);
        var region = await HrFactory.CreateUnitAsync(admin, company, sales, "Marmara", OrgUnitType.Department);
        var version = (await admin.GetFromJsonAsync<OrgUnitDetails>($"{Url}/{sales}", Ct))!.Version;

        var response = await admin.PostAsJsonAsync($"{Url}/{sales}/move", new { version, parentId = region }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(response)).Errors!["parentId"].ShouldBe(["Birim kendisinin ya da alt birimlerinden birinin altına taşınamaz."]);
        (await factory.PathAsync(region)).ShouldStartWith(await factory.PathAsync(sales));
    }

    [Fact]
    public async Task Update_SetsTheManager_WhichTheUnitsPositionsReportTo()
    {
        var admin = factory.Admin();
        var company = await HrFactory.CreateCompanyAsync(admin);
        var sales = await HrFactory.CreateUnitAsync(admin, company);
        var other = await HrFactory.CreateUnitAsync(admin, company, name: "Pazarlama");
        var job = await HrFactory.CreateJobAsync(admin);
        var director = await HrFactory.CreatePositionAsync(admin, sales, job);
        var rep = await HrFactory.CreatePositionAsync(admin, sales, job, norm: 3);
        var outsider = await HrFactory.CreatePositionAsync(admin, other, job);
        var version = (await admin.GetFromJsonAsync<OrgUnitDetails>($"{Url}/{sales}", Ct))!.Version;

        var invalid = await admin.PutAsJsonAsync($"{Url}/{sales}", new { version, name = "Satış", type = "Directorate", managerPositionId = outsider }, Ct);
        (await Problem.ReadAsync(invalid)).Errors!.Keys.ShouldBe(["managerPositionId"]);

        var response = await admin.PutAsJsonAsync($"{Url}/{sales}", new { version, name = "Satış", type = "Directorate", managerPositionId = director }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var unit = (await admin.GetFromJsonAsync<OrgUnitDetails>($"{Url}/{sales}", Ct))!;
        unit.ManagerPositionId.ShouldBe(director);
        unit.ManagerJobName.ShouldBe("Satış Müdürü");
        (await factory.ReportingLinesAsync(company)).ShouldBe([(director, rep, 1)]);
        (await admin.GetFromJsonAsync<PositionDetails>($"/api/hr/positions/{rep}", Ct))!.ManagerPositionId.ShouldBe(director);

        // The client edited an old version
        (await admin.PutAsJsonAsync($"{Url}/{sales}", new { version, name = "Satış", type = "Directorate" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task List_SearchesAndFilters_WithinTheScope()
    {
        var admin = factory.Admin();
        var company = await HrFactory.CreateCompanyAsync(admin);
        var sales = await HrFactory.CreateUnitAsync(admin, company, name: "Satış Direktörlüğü");
        var region = await HrFactory.CreateUnitAsync(admin, company, sales, "Marmara Bölgesi", OrgUnitType.Department);

        var found = (await admin.GetFromJsonAsync<PagedResult<OrgUnitListItem>>($"{Url}?companyId={company}&search=marmara", Ct))!;
        found.Items.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            u => u.Id.ShouldBe(region),
            u => u.ParentName.ShouldBe("Satış Direktörlüğü"),
            u => u.CompanyName.ShouldBe("Acme"),
            u => u.IsActive.ShouldBeTrue());
        (await admin.GetFromJsonAsync<PagedResult<OrgUnitListItem>>($"{Url}?parentId={sales}", Ct))!.Items.Select(u => u.Id).ShouldBe([region]);
        (await admin.GetFromJsonAsync<PagedResult<OrgUnitListItem>>($"{Url}?companyId={company}&type=Directorate", Ct))!.Items.Select(u => u.Id).ShouldBe([sales]);
        (await admin.GetAsync($"{Url}?sort=path", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static async Task<List<Guid>> TreeIdsAsync(HttpClient client, Guid companyId) =>
        [.. (await client.GetFromJsonAsync<OrgUnitTree>($"{Url}/tree?companyId={companyId}", Ct))!.Units.Select(u => u.Id)];
}
