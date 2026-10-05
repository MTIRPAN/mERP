using System.Net;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Api.Endpoints;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Companies;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.Testing.Data;
using Merp.Testing.Web;

namespace Merp.Modules.Platform.IntegrationTests.Organization;

public sealed class CompanyEndpointsTests(OrganizationFactory factory) : IClassFixture<OrganizationFactory>
{
    private const string Url = "/api/platform/companies";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_ThenGet_ReturnsTheCompanyWithAddressNames()
    {
        var admin = factory.Admin();
        var code = OrganizationFactory.NewCode();
        var taxNumber = TurkishData.Vkn();

        var response = await admin.PostAsJsonAsync(Url, OrganizationFactory.CompanyBody(code.ToLowerInvariant(), taxNumber), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await response.Content.ReadFromJsonAsync<EntityCreatedResponse>(Ct))!.Id;
        response.Headers.Location!.ToString().ShouldBe($"{Url}/{id}");

        var company = (await admin.GetFromJsonAsync<CompanyDetails>($"{Url}/{id}", Ct))!;
        company.Code.ShouldBe(code);
        company.TaxNumber.ShouldBe(taxNumber);
        company.Phone.ShouldBe("+902161234567");
        company.Address!.City.ShouldBe("İstanbul");
        company.Address.District.ShouldBe("Kadıköy");
        company.Culture.ShouldBe("tr-TR");
        company.TimeZoneId.ShouldBe("Europe/Istanbul");
        company.IsActive.ShouldBeTrue();
        company.Version.ShouldNotBe(0u);

        (await factory.OutboxTypesAsync("acme")).ShouldContain(typeof(CompanyCreated).FullName);
        var audited = await factory.AuditedFieldsAsync("acme", "Company", id);
        audited.ShouldContain("TaxNumber");
        audited.ShouldContain("Address.City");
    }

    [Fact]
    public async Task Create_WithInvalidTaxNumber_Returns400WithATurkishMessage()
    {
        var response = await factory.Admin().PostAsJsonAsync(Url, OrganizationFactory.CompanyBody(taxNumber: "1234567891"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await Problem.ReadAsync(response);
        problem.Code.ShouldBe("common.errors.validation");
        problem.Errors!["taxNumber"].ShouldBe(["Vergi kimlik numarası geçersiz."]);
    }

    [Fact]
    public async Task Create_WithTakenCodeOrTaxNumber_Returns400()
    {
        var admin = factory.Admin();
        var code = OrganizationFactory.NewCode();
        var taxNumber = TurkishData.Vkn();
        await OrganizationFactory.CreateCompanyAsync(admin, OrganizationFactory.CompanyBody(code, taxNumber));

        var response = await admin.PostAsJsonAsync(Url, OrganizationFactory.CompanyBody(code, taxNumber), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await Problem.ReadAsync(response);
        problem.Errors!.Keys.ShouldBe(["code", "taxNumber"], ignoreOrder: true);
        problem.Errors["code"].ShouldBe(["Bu değer başka bir kayıtta kullanılıyor."]);
    }

    [Fact]
    public async Task Create_WithADistrictOfAnotherProvince_Returns400()
    {
        var body = new
        {
            code = OrganizationFactory.NewCode(),
            title = "Acme",
            shortName = "Acme",
            taxNumber = TurkishData.Vkn(),
            address = new { line1 = "Cad. 1", provinceCode = 6, districtCode = 1421 },
        };

        var response = await factory.Admin().PostAsJsonAsync(Url, body, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(response)).Errors!["address.districtCode"].ShouldBe(["Seçilen ilçe bu ile ait değil."]);
    }

    [Fact]
    public async Task Create_WithoutPermission_Returns403()
    {
        var client = factory.CreateClientFor("reader", "acme", PlatformPermissions.Companies.Read);

        var response = await client.PostAsJsonAsync(Url, OrganizationFactory.CompanyBody(), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_WithOnlyACompanyScope_Returns403()
    {
        var existing = await OrganizationFactory.CreateCompanyAsync(factory.Admin());
        var client = factory.Admin().WithScopes(AccessScope.Company(existing));

        var response = await client.PostAsJsonAsync(Url, OrganizationFactory.CompanyBody(), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Problem.ReadAsync(response)).Code.ShouldBe("platform.companies.errors.tenant_scope_required");
    }

    [Fact]
    public async Task UserScopedToOneCompany_SeesOnlyThatCompany_InTheListAndTheSelector()
    {
        var admin = factory.Admin();
        var own = await OrganizationFactory.CreateCompanyAsync(admin);
        var other = await OrganizationFactory.CreateCompanyAsync(admin);
        var client = factory.CreateClientFor("ik", "acme", PlatformPermissions.Companies.Read, PlatformPermissions.Branches.Read)
            .WithScopes(AccessScope.Company(own));

        var list = (await client.GetFromJsonAsync<PagedResult<CompanyListItem>>($"{Url}?pageSize=200", Ct))!;
        list.Items.Select(c => c.Id).ShouldBe([own]);
        list.TotalCount.ShouldBe(1);

        var me = (await client.GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!;
        me.Companies.Select(c => c.Id).ShouldBe([own]);

        (await client.GetAsync($"{Url}/{other}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await admin.GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!.Companies.Select(c => c.Id).ShouldContain(other);
    }

    [Fact]
    public async Task UserScopedToABranch_SeesTheCompanyOfTheBranch()
    {
        var admin = factory.Admin();
        var company = await OrganizationFactory.CreateCompanyAsync(admin);
        var branch = await OrganizationFactory.CreateBranchAsync(admin, company);
        var client = factory.CreateClientFor("sube", "acme", PlatformPermissions.Companies.Read).WithScopes(AccessScope.Branch(branch));

        var list = (await client.GetFromJsonAsync<PagedResult<CompanyListItem>>(Url, Ct))!;

        list.Items.Select(c => c.Id).ShouldBe([company]);
    }

    [Fact]
    public async Task List_SearchesFiltersSortsAndPages()
    {
        var admin = factory.Admin();
        var marker = OrganizationFactory.NewCode("S");
        var first = await OrganizationFactory.CreateCompanyAsync(admin, OrganizationFactory.CompanyBody(marker + "A", title: $"Çağdaş Gıda {marker}"));
        var second = await OrganizationFactory.CreateCompanyAsync(admin, OrganizationFactory.CompanyBody(marker + "B", title: $"Işık Enerji {marker}"));
        var inactive = await OrganizationFactory.CreateCompanyAsync(admin, OrganizationFactory.CompanyBody(marker + "C", title: $"Eski {marker}"));
        await DeactivateAsync(admin, inactive);

        var page = (await admin.GetFromJsonAsync<PagedResult<CompanyListItem>>($"{Url}?search={marker.ToLowerInvariant()}&isActive=true&sort=-code&pageSize=1", Ct))!;
        page.TotalCount.ShouldBe(2);
        page.PageSize.ShouldBe(1);
        page.Items.Single().Id.ShouldBe(second);

        var byTitle = (await admin.GetFromJsonAsync<PagedResult<CompanyListItem>>($"{Url}?search={Uri.EscapeDataString("çağdaş gıda " + marker)}", Ct))!;
        byTitle.Items.Select(c => c.Id).ShouldBe([first]);
        byTitle.Items[0].City.ShouldBe("İstanbul");
    }

    [Fact]
    public async Task List_WithAnUnknownSort_Returns400()
    {
        var response = await factory.Admin().GetAsync($"{Url}?sort=password", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(response)).Errors!["sort"].ShouldBe(["Sıralama alanı geçersiz."]);
    }

    [Fact]
    public async Task ListAndGet_WithoutPermission_Return403()
    {
        var id = await OrganizationFactory.CreateCompanyAsync(factory.Admin());
        var client = factory.CreateClientFor("calisan", "acme", PlatformPermissions.Branches.Read);

        (await client.GetAsync(Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync($"{Url}/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Update_ChangesDetailsAndDeactivates_AndAuditsTheChange()
    {
        var admin = factory.Admin();
        var id = await OrganizationFactory.CreateCompanyAsync(admin);
        var before = (await admin.GetFromJsonAsync<CompanyDetails>($"{Url}/{id}", Ct))!;

        var response = await admin.PutAsJsonAsync($"{Url}/{id}", UpdateBody(before, isActive: false, provinceCode: 6, districtCode: 1231), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var after = (await admin.GetFromJsonAsync<CompanyDetails>($"{Url}/{id}", Ct))!;
        after.Title.ShouldBe("Acme Sanayi ve Ticaret A.Ş.");
        after.IsActive.ShouldBeFalse();
        after.Address!.City.ShouldBe("Ankara");
        after.Address.District.ShouldBe("Çankaya");
        after.Code.ShouldBe(before.Code);
        after.Version.ShouldNotBe(before.Version);
        after.ModifiedAt.ShouldNotBeNull();

        (await factory.OutboxTypesAsync("acme")).ShouldContain(typeof(CompanyUpdated).FullName);
        var audited = await factory.AuditedFieldsAsync("acme", "Company", id);
        audited.ShouldContain("Title");
        audited.ShouldContain("IsActive");
        audited.ShouldContain("Address.City");

        // Inactive companies leave the selector but stay in the list
        (await admin.GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!.Companies.Select(c => c.Id).ShouldNotContain(id);
    }

    [Fact]
    public async Task Update_WithAStaleVersion_Returns409()
    {
        var admin = factory.Admin();
        var id = await OrganizationFactory.CreateCompanyAsync(admin);
        var stale = (await admin.GetFromJsonAsync<CompanyDetails>($"{Url}/{id}", Ct))!;
        (await admin.PutAsJsonAsync($"{Url}/{id}", UpdateBody(stale), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var response = await admin.PutAsJsonAsync($"{Url}/{id}", UpdateBody(stale, isActive: false), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Problem.ReadAsync(response)).Code.ShouldBe("common.errors.concurrency_conflict");
    }

    [Fact]
    public async Task Update_WithInvalidInput_Returns400()
    {
        var admin = factory.Admin();
        var id = await OrganizationFactory.CreateCompanyAsync(admin);
        var company = (await admin.GetFromJsonAsync<CompanyDetails>($"{Url}/{id}", Ct))!;

        var response = await admin.PutAsJsonAsync($"{Url}/{id}", UpdateBody(company) with { TaxNumber = "1", TimeZoneId = "Mars/Olympus" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(response)).Errors!["timeZoneId"].ShouldBe(["Saat dilimi geçersiz."]);
    }

    [Fact]
    public async Task Update_OutsideTheScope_Returns404_AndWithoutPermission_Returns403()
    {
        var admin = factory.Admin();
        var own = await OrganizationFactory.CreateCompanyAsync(admin);
        var other = await OrganizationFactory.CreateCompanyAsync(admin);
        var company = (await admin.GetFromJsonAsync<CompanyDetails>($"{Url}/{other}", Ct))!;

        var scoped = factory.Admin().WithScopes(AccessScope.Company(own));
        (await scoped.PutAsJsonAsync($"{Url}/{other}", UpdateBody(company), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await scoped.PutAsJsonAsync($"{Url}/{Guid.CreateVersion7()}", UpdateBody(company), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var reader = factory.CreateClientFor("reader", "acme", PlatformPermissions.Companies.Read);
        (await reader.PutAsJsonAsync($"{Url}/{other}", UpdateBody(company), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CompaniesOfAnotherTenant_AreInvisible()
    {
        var id = await OrganizationFactory.CreateCompanyAsync(factory.Admin("acme"));
        var globex = factory.Admin("globex");

        (await globex.GetAsync($"{Url}/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await globex.GetFromJsonAsync<PagedResult<CompanyListItem>>($"{Url}?pageSize=200", Ct))!.Items.Select(c => c.Id).ShouldNotContain(id);
    }

    private static async Task DeactivateAsync(HttpClient client, Guid id)
    {
        var company = (await client.GetFromJsonAsync<CompanyDetails>($"{Url}/{id}", Ct))!;
        (await client.PutAsJsonAsync($"{Url}/{id}", UpdateBody(company, isActive: false), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static UpdateCompanyCommand UpdateBody(CompanyDetails company, bool isActive = true, int provinceCode = 34, int districtCode = 1421) =>
        new(
            company.Version,
            "Acme Sanayi ve Ticaret A.Ş.",
            company.ShortName,
            company.TaxOffice,
            company.TaxNumber,
            company.MersisNumber,
            company.SgkWorkplaceNumber,
            new(company.Address!.Line1, ProvinceCode: provinceCode, DistrictCode: districtCode),
            company.Phone,
            company.Email,
            company.Culture,
            company.TimeZoneId,
            isActive);
}
