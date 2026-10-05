using System.Net;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Branches;
using Merp.Modules.Platform.Application.Companies;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.Modules.Platform.Domain.Branches;
using Merp.Testing.Web;

namespace Merp.Modules.Platform.IntegrationTests.Organization;

public sealed class BranchEndpointsTests(OrganizationFactory factory) : IClassFixture<OrganizationFactory>
{
    private const string Url = "/api/platform/branches";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_ThenGet_ReturnsTheBranchWithItsCompany()
    {
        var admin = factory.Admin();
        var company = await OrganizationFactory.CreateCompanyAsync(admin);

        var response = await admin.PostAsJsonAsync(Url, OrganizationFactory.BranchBody(company, "gbz-01", BranchType.HeadOffice), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await response.Content.ReadFromJsonAsync<Api.Endpoints.EntityCreatedResponse>(Ct))!.Id;
        var branch = (await admin.GetFromJsonAsync<BranchDetails>($"{Url}/{id}", Ct))!;
        branch.CompanyId.ShouldBe(company);
        branch.CompanyName.ShouldBe("Acme");
        branch.Code.ShouldBe("GBZ-01");
        branch.Type.ShouldBe(BranchType.HeadOffice);
        branch.Address.City.ShouldBe("Kocaeli");
        branch.Address.District.ShouldBe("Gebze");
        branch.Address.ProvinceCode.ShouldBe(41);
        branch.IsActive.ShouldBeTrue();

        (await factory.OutboxTypesAsync("acme")).ShouldContain(typeof(BranchCreated).FullName);
        (await factory.AuditedFieldsAsync("acme", "Branch", id)).ShouldContain("Address.District");
    }

    [Fact]
    public async Task Create_WithInvalidInput_Returns400WithFieldErrors()
    {
        var admin = factory.Admin();
        var company = await OrganizationFactory.CreateCompanyAsync(admin);
        var body = new { companyId = company, code = "B 1", name = "", type = "Factory", address = new { line1 = "Cad. 1", provinceCode = 41, districtCode = 1421 } };

        var response = await admin.PostAsJsonAsync(Url, body, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(response)).Errors!.Keys.ShouldBe(["code", "name"], ignoreOrder: true);

        var wrongDistrict = new { companyId = company, code = "B1", name = "Gebze", type = "Factory", address = new { line1 = "Cad. 1", provinceCode = 41, districtCode = 1421 } };
        var problem = await Problem.ReadAsync(await admin.PostAsJsonAsync(Url, wrongDistrict, Ct));
        problem.Errors!["address.districtCode"].ShouldBe(["Seçilen ilçe bu ile ait değil."]);
    }

    [Fact]
    public async Task Create_ASecondHeadOfficeOrATakenCode_Returns400()
    {
        var admin = factory.Admin();
        var company = await OrganizationFactory.CreateCompanyAsync(admin);
        await admin.PostAsJsonAsync(Url, OrganizationFactory.BranchBody(company, "MERKEZ", BranchType.HeadOffice), Ct);

        var response = await admin.PostAsJsonAsync(Url, OrganizationFactory.BranchBody(company, "MERKEZ", BranchType.HeadOffice), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await Problem.ReadAsync(response);
        problem.Errors!["type"].ShouldBe(["Bu şirketin zaten bir merkez şubesi var."]);
        problem.Errors.ShouldContainKey("code");

        // The same code in another company is fine
        var other = await OrganizationFactory.CreateCompanyAsync(admin);
        (await admin.PostAsJsonAsync(Url, OrganizationFactory.BranchBody(other, "MERKEZ", BranchType.HeadOffice), Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_InAnUnknownOrInactiveCompany_Returns400()
    {
        var admin = factory.Admin();
        var unknown = await Problem.ReadAsync(await admin.PostAsJsonAsync(Url, OrganizationFactory.BranchBody(Guid.CreateVersion7()), Ct));
        unknown.Errors!["companyId"].ShouldBe(["Şirket bulunamadı."]);

        var company = await OrganizationFactory.CreateCompanyAsync(admin);
        var details = (await admin.GetFromJsonAsync<CompanyDetails>($"/api/platform/companies/{company}", Ct))!;
        await admin.PutAsJsonAsync(
            $"/api/platform/companies/{company}",
            new UpdateCompanyCommand(details.Version, details.Title, details.ShortName, null, details.TaxNumber, null, null, null, null, null, null, null, false),
            Ct);

        var inactive = await Problem.ReadAsync(await admin.PostAsJsonAsync(Url, OrganizationFactory.BranchBody(company), Ct));
        inactive.Errors!["companyId"].ShouldBe(["Pasif bir şirkete şube eklenemez."]);
    }

    [Fact]
    public async Task Create_InACompanyOutsideTheScope_OrWithoutPermission_Returns403()
    {
        var admin = factory.Admin();
        var own = await OrganizationFactory.CreateCompanyAsync(admin);
        var other = await OrganizationFactory.CreateCompanyAsync(admin);

        var scoped = factory.Admin().WithScopes(AccessScope.Company(own));
        (await scoped.PostAsJsonAsync(Url, OrganizationFactory.BranchBody(own), Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await scoped.PostAsJsonAsync(Url, OrganizationFactory.BranchBody(other), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var reader = factory.CreateClientFor("reader", "acme", PlatformPermissions.Branches.Read);
        (await reader.PostAsJsonAsync(Url, OrganizationFactory.BranchBody(own), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_IsLimitedToTheScope_AndNarrowedByTheActiveCompany()
    {
        var admin = factory.Admin();
        var acme = await OrganizationFactory.CreateCompanyAsync(admin);
        var globex = await OrganizationFactory.CreateCompanyAsync(admin);
        var acmeBranch = await OrganizationFactory.CreateBranchAsync(admin, acme);
        var globexBranch = await OrganizationFactory.CreateBranchAsync(admin, globex);
        var globexStore = await OrganizationFactory.CreateBranchAsync(admin, globex, BranchType.Store);

        var all = await ListAsync(admin, "pageSize=200");
        all.Select(b => b.Id).ShouldContain(acmeBranch);
        all.Select(b => b.Id).ShouldContain(globexBranch);

        // Active company header (D4) and the companyId filter narrow the list
        var narrowed = factory.Admin();
        narrowed.DefaultRequestHeaders.Add("X-Company-Id", globex.ToString());
        (await ListAsync(narrowed, "pageSize=200")).Select(b => b.Id).ShouldBe([globexBranch, globexStore], ignoreOrder: true);
        (await ListAsync(admin, $"companyId={acme}")).Select(b => b.Id).ShouldBe([acmeBranch]);
        (await ListAsync(admin, $"companyId={globex}&type=Store")).Select(b => b.Id).ShouldBe([globexStore]);

        // The header never widens the scope
        var companyScoped = factory.CreateClientFor("ik", "acme", PlatformPermissions.Branches.Read).WithScopes(AccessScope.Company(acme));
        (await ListAsync(companyScoped, $"companyId={globex}")).ShouldBeEmpty();
        (await ListAsync(companyScoped, "pageSize=200")).Select(b => b.Id).ShouldBe([acmeBranch]);

        var branchScoped = factory.CreateClientFor("sube", "acme", PlatformPermissions.Branches.Read).WithScopes(AccessScope.Branch(globexStore));
        var visible = await ListAsync(branchScoped, "pageSize=200");
        visible.Select(b => b.Id).ShouldBe([globexStore]);
        visible[0].CompanyName.ShouldBe("Acme");
        visible[0].City.ShouldBe("Kocaeli");
    }

    [Fact]
    public async Task ListAndGet_WithoutPermission_Return403_AndOutsideTheScope_Return404()
    {
        var admin = factory.Admin();
        var company = await OrganizationFactory.CreateCompanyAsync(admin);
        var branch = await OrganizationFactory.CreateBranchAsync(admin, company);

        var noPermission = factory.CreateClientFor("calisan", "acme", PlatformPermissions.Companies.Read);
        (await noPermission.GetAsync(Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await noPermission.GetAsync($"{Url}/{branch}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var otherCompany = await OrganizationFactory.CreateCompanyAsync(admin);
        var outside = factory.CreateClientFor("ik", "acme", PlatformPermissions.Branches.Read).WithScopes(AccessScope.Company(otherCompany));
        (await outside.GetAsync($"{Url}/{branch}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var badSort = await admin.GetAsync($"{Url}?sort=secret", Ct);
        badSort.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_ChangesDetails_AndDetectsConcurrentChanges()
    {
        var admin = factory.Admin();
        var company = await OrganizationFactory.CreateCompanyAsync(admin);
        var id = await OrganizationFactory.CreateBranchAsync(admin, company);
        var before = (await admin.GetFromJsonAsync<BranchDetails>($"{Url}/{id}", Ct))!;

        var response = await admin.PutAsJsonAsync($"{Url}/{id}", UpdateBody(before, "Gebze Depo", BranchType.Warehouse, isActive: false), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var after = (await admin.GetFromJsonAsync<BranchDetails>($"{Url}/{id}", Ct))!;
        after.Name.ShouldBe("Gebze Depo");
        after.Type.ShouldBe(BranchType.Warehouse);
        after.IsActive.ShouldBeFalse();
        after.Code.ShouldBe(before.Code);
        (await factory.OutboxTypesAsync("acme")).ShouldContain(typeof(BranchUpdated).FullName);

        var stale = await admin.PutAsJsonAsync($"{Url}/{id}", UpdateBody(before, "Gebze", BranchType.Factory), Ct);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_InvalidOutOfScopeOrWithoutPermission_IsRejected()
    {
        var admin = factory.Admin();
        var company = await OrganizationFactory.CreateCompanyAsync(admin);
        var headOffice = await OrganizationFactory.CreateBranchAsync(admin, company, BranchType.HeadOffice);
        var id = await OrganizationFactory.CreateBranchAsync(admin, company);
        var branch = (await admin.GetFromJsonAsync<BranchDetails>($"{Url}/{id}", Ct))!;

        var secondHeadOffice = await admin.PutAsJsonAsync($"{Url}/{id}", UpdateBody(branch, "Gebze", BranchType.HeadOffice), Ct);
        secondHeadOffice.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(secondHeadOffice)).Errors!.ShouldContainKey("type");

        // The head office itself may stay a head office
        var head = (await admin.GetFromJsonAsync<BranchDetails>($"{Url}/{headOffice}", Ct))!;
        (await admin.PutAsJsonAsync($"{Url}/{headOffice}", UpdateBody(head, "Merkez", BranchType.HeadOffice), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var otherCompany = await OrganizationFactory.CreateCompanyAsync(admin);
        var outside = factory.Admin().WithScopes(AccessScope.Company(otherCompany));
        (await outside.PutAsJsonAsync($"{Url}/{id}", UpdateBody(branch, "X", BranchType.Office), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A branch scope is enough to manage that branch
        var branchScoped = factory.Admin().WithScopes(AccessScope.Branch(id));
        (await branchScoped.PutAsJsonAsync($"{Url}/{id}", UpdateBody(branch, "Gebze Ofis", BranchType.Office), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var reader = factory.CreateClientFor("reader", "acme", PlatformPermissions.Branches.Read);
        (await reader.PutAsJsonAsync($"{Url}/{id}", UpdateBody(branch, "X", BranchType.Office), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static async Task<IReadOnlyList<BranchListItem>> ListAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<PagedResult<BranchListItem>>($"{Url}?{query}", Ct))!.Items;

    private static UpdateBranchCommand UpdateBody(BranchDetails branch, string name, BranchType type, bool isActive = true) =>
        new(branch.Version, name, type, new(branch.Address.Line1, ProvinceCode: 41, DistrictCode: 1338), branch.SgkWorkplaceNumber, branch.Phone, isActive);
}
