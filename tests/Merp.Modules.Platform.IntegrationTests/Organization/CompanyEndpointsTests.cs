using System.Net;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.Platform.Api.Endpoints;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Companies;
using Merp.Testing.Web;
using static Merp.Modules.Platform.IntegrationTests.Organization.PlatformFactory;

namespace Merp.Modules.Platform.IntegrationTests.Organization;

public sealed class CompanyEndpointsTests(PlatformFactory factory) : IClassFixture<PlatformFactory>
{
    private const string Url = "/api/platform/companies";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_ThenGet_ReturnsTheCompanyWithOfficialLocationNames()
    {
        var client = factory.Manager();
        var command = Company(title: "  Anadolu Gıda Sanayi A.Ş. ", shortName: "Anadolu Gıda") with { Code = NewCode("anadolu-") };

        var response = await client.PostAsJsonAsync(Url, command, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await response.Content.ReadFromJsonAsync<Merp.Web.Errors.CreatedResponse>(Ct))!.Id;
        response.Headers.Location!.ToString().ShouldBe($"{Url}/{id}");

        var company = (await client.GetFromJsonAsync<CompanyDto>($"{Url}/{id}", Ct))!;
        company.Code.ShouldBe(command.Code.ToUpperInvariant());
        company.Title.ShouldBe("Anadolu Gıda Sanayi A.Ş.");
        company.TaxNumber.ShouldBe(command.TaxNumber);
        company.ProvinceName.ShouldBe("İstanbul");
        company.DistrictName.ShouldBe("Kadıköy");
        company.Phone.ShouldBe("+902161234567");
        company.TimeZoneId.ShouldBe("Europe/Istanbul");
        company.IsActive.ShouldBeTrue();
        company.Version.ShouldNotBe(0u);

        var created = (await factory.OutboxAsync(Tenant, "CompanyCreated")).Where(e => e.GetProperty("companyId").GetGuid() == id).ShouldHaveSingleItem();
        created.GetProperty("code").GetString().ShouldBe(company.Code);
    }

    [Fact]
    public async Task Create_WithInvalidTaxNumber_Returns400WithTurkishMessage()
    {
        var problem = await ProblemAsync(
            await factory.Manager().PostAsJsonAsync(Url, Company() with { TaxNumber = "1234567891" }, Ct),
            HttpStatusCode.BadRequest);

        var error = problem.GetProperty("fieldErrors").EnumerateArray().ShouldHaveSingleItem();
        error.GetProperty("field").GetString().ShouldBe("taxNumber");
        error.GetProperty("code").GetString().ShouldBe("common.errors.invalid_vkn");
        error.GetProperty("message").GetString().ShouldBe("Vergi kimlik numarası geçersiz.");
    }

    [Fact]
    public async Task Create_WithDistrictOfAnotherProvince_Returns400()
    {
        var problem = await ProblemAsync(
            await factory.Manager().PostAsJsonAsync(Url, Company() with { ProvinceCode = Locations.Izmir }, Ct),
            HttpStatusCode.BadRequest);

        FieldCodes(problem).ShouldBe([("districtId", "platform.locations.unknown_district")]);
    }

    [Fact]
    public async Task Create_WithUnknownProvince_Returns400()
    {
        var problem = await ProblemAsync(
            await factory.Manager().PostAsJsonAsync(Url, Company() with { ProvinceCode = 82 }, Ct),
            HttpStatusCode.BadRequest);

        FieldCodes(problem).ShouldBe([("provinceCode", "platform.locations.unknown_province")]);
    }

    [Fact]
    public async Task Create_WithTakenCode_Returns409_IgnoringCase()
    {
        var code = NewCode("dup");
        await CreateCompanyAsync(factory.Manager(), Company(code: code.ToUpperInvariant()));

        var problem = await ProblemAsync(
            await factory.Manager().PostAsJsonAsync(Url, Company(code: code.ToLowerInvariant()), Ct),
            HttpStatusCode.Conflict);

        problem.GetProperty("code").GetString().ShouldBe("platform.companies.code_taken");
    }

    [Fact]
    public async Task Create_WithNumberAsText_Returns400()
    {
        // Numbers must be JSON numbers (strict number handling), not "34".
        var response = await factory.Manager().PostAsync(
            Url,
            JsonContent.Create(new { code = "X1", title = "X", shortName = "X", taxNumber = "1234567890", addressLine1 = "X", provinceCode = "34", districtId = 34023, defaultCulture = "tr-TR", timeZoneId = "Europe/Istanbul" }),
            Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_WithoutPermission_Returns403() =>
        (await factory.CreateClientFor("reader-1", Tenant, PlatformPermissions.Companies.Read).PostAsJsonAsync(Url, Company(), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

    [Fact]
    public async Task Create_ByCompanyScopedManager_Returns403()
    {
        var own = await CreateCompanyAsync(factory.Manager(), Company());

        var response = await factory.Manager().WithScopes($"Company:{own}").PostAsJsonAsync(Url, Company(), Ct);

        (await ProblemAsync(response, HttpStatusCode.Forbidden)).GetProperty("code").GetString().ShouldBe("common.errors.forbidden");
    }

    [Fact]
    public async Task Update_WithCurrentVersion_ChangesTheCompany_AndPublishesAnEvent()
    {
        var client = factory.Manager();
        var id = await CreateCompanyAsync(client, Company());
        var before = (await client.GetFromJsonAsync<CompanyDto>($"{Url}/{id}", Ct))!;

        var response = await client.PutAsJsonAsync($"{Url}/{id}", UpdateOf(before) with { ShortName = "ACME Holding", DistrictId = 34038, IsActive = false }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(Ct));
        var after = (await client.GetFromJsonAsync<CompanyDto>($"{Url}/{id}", Ct))!;
        after.ShortName.ShouldBe("ACME Holding");
        after.DistrictName.ShouldBe("Üsküdar");
        after.IsActive.ShouldBeFalse();
        after.Code.ShouldBe(before.Code);
        after.Version.ShouldNotBe(before.Version);
        (await factory.OutboxAsync(Tenant, "CompanyUpdated"))
            .ShouldContain(e => e.GetProperty("companyId").GetGuid() == id && !e.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Update_WithStaleVersion_Returns409()
    {
        var client = factory.Manager();
        var id = await CreateCompanyAsync(client, Company());
        var stale = (await client.GetFromJsonAsync<CompanyDto>($"{Url}/{id}", Ct))!;
        (await client.PutAsJsonAsync($"{Url}/{id}", UpdateOf(stale) with { Title = "First" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var problem = await ProblemAsync(await client.PutAsJsonAsync($"{Url}/{id}", UpdateOf(stale) with { Title = "Second" }, Ct), HttpStatusCode.Conflict);

        problem.GetProperty("code").GetString().ShouldBe("common.errors.concurrency_conflict");
        (await client.GetFromJsonAsync<CompanyDto>($"{Url}/{id}", Ct))!.Title.ShouldBe("First");
    }

    [Fact]
    public async Task Update_OutsideScope_Returns404()
    {
        var own = await CreateCompanyAsync(factory.Manager(), Company());
        var other = await CreateCompanyAsync(factory.Manager(), Company());
        var dto = (await factory.Manager().GetFromJsonAsync<CompanyDto>($"{Url}/{other}", Ct))!;

        var response = await factory.Manager().WithScopes($"Company:{own}").PutAsJsonAsync($"{Url}/{other}", UpdateOf(dto), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_Unknown_Returns404() =>
        (await factory.Manager().GetAsync($"{Url}/{Guid.CreateVersion7()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

    [Fact]
    public async Task List_SearchesIgnoringCaseAndTurkishLetters()
    {
        var tenant = await factory.NewTenantAsync();
        var client = factory.Manager(tenant);
        await CreateCompanyAsync(client, Company(title: "ŞİŞE VE CAM SANAYİİ A.Ş.", shortName: "Şişecam"));
        await CreateCompanyAsync(client, Company(title: "Ilgın Tekstil A.Ş.", shortName: "Ilgın") with { ProvinceCode = Locations.Izmir, DistrictId = Locations.Konak });

        (await ListAsync(client, "filter=sisecam")).Items.ShouldHaveSingleItem().ShortName.ShouldBe("Şişecam");
        (await ListAsync(client, "filter=ŞİŞE")).Items.ShouldHaveSingleItem().ShortName.ShouldBe("Şişecam");
        (await ListAsync(client, "filter=ilgin")).Items.ShouldHaveSingleItem().ShortName.ShouldBe("Ilgın");
        // City names come from the reference data ("İstanbul", "İzmir").
        (await ListAsync(client, "filter=istanbul")).Items.ShouldHaveSingleItem().City.ShouldBe("İstanbul");
        (await ListAsync(client, "filter=IZMIR")).Items.ShouldHaveSingleItem().City.ShouldBe("İzmir");
    }

    [Fact]
    public async Task List_PagesSortsAndFilters()
    {
        var tenant = await factory.NewTenantAsync();
        var client = factory.Manager(tenant);
        foreach (var (code, name) in new[] { ("C1", "Çınar"), ("C2", "Cevher"), ("C3", "Doğa"), ("C4", "Bereket") })
        {
            await CreateCompanyAsync(client, Company(code: code, shortName: name));
        }

        var inactive = await CreateCompanyAsync(client, Company(code: "C5", shortName: "Arda"));
        var dto = (await client.GetFromJsonAsync<CompanyDto>($"{Url}/{inactive}", Ct))!;
        await client.PutAsJsonAsync($"{Url}/{inactive}", UpdateOf(dto) with { IsActive = false }, Ct);

        // Default sort is the short name in Turkish order: C before Ç.
        (await ListAsync(client, "isActive=true")).Items.Select(c => c.ShortName).ShouldBe(["Bereket", "Cevher", "Çınar", "Doğa"]);

        var page = await ListAsync(client, "sort=-code&page=2&pageSize=2");
        page.TotalCount.ShouldBe(5);
        page.Page.ShouldBe(2);
        page.Items.Select(c => c.Code).ShouldBe(["C3", "C2"]);

        (await ListAsync(client, "isActive=false")).Items.ShouldHaveSingleItem().Id.ShouldBe(inactive);
    }

    [Theory]
    [InlineData("sort=taxNumber", "sort", "common.errors.invalid_sort")]
    [InlineData("pageSize=201", "pageSize", "common.errors.between")]
    [InlineData("page=0", "page", "common.errors.greater_than_or_equal")]
    public async Task List_WithInvalidPaging_Returns400(string query, string field, string code)
    {
        var problem = await ProblemAsync(await factory.Manager().GetAsync($"{Url}?{query}", Ct), HttpStatusCode.BadRequest);

        FieldCodes(problem).ShouldContain((field, code));
    }

    [Fact]
    public async Task List_WithoutPermission_Returns403() =>
        (await factory.CreateClientFor("user-1", Tenant, PlatformPermissions.Branches.Read).GetAsync(Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

    [Fact]
    public async Task DataScope_LimitsListGetAndTheCompanySelector()
    {
        var tenant = await factory.NewTenantAsync();
        var manager = factory.Manager(tenant);
        var acme = await CreateCompanyAsync(manager, Company(shortName: "Acme"));
        var globex = await CreateCompanyAsync(manager, Company(shortName: "Globex"));
        var initech = await CreateCompanyAsync(manager, Company(shortName: "Initech"));
        var initechBranch = await CreateBranchAsync(manager, Branch(initech));

        var companyScoped = factory.CreateClientFor("hr-1", tenant, PlatformPermissions.Companies.Read).WithScopes($"Company:{acme}");
        (await ListAsync(companyScoped, string.Empty)).Items.ShouldHaveSingleItem().Id.ShouldBe(acme);
        (await companyScoped.GetAsync($"{Url}/{globex}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A branch scope reaches the branch's company for reading.
        var branchScoped = factory.CreateClientFor("store-1", tenant, PlatformPermissions.Companies.Read).WithScopes($"Branch:{initechBranch}");
        (await ListAsync(branchScoped, string.Empty)).Items.ShouldHaveSingleItem().Id.ShouldBe(initech);

        var noScope = factory.CreateClientFor("nobody-1", tenant, PlatformPermissions.Companies.Read).WithScopes();
        (await ListAsync(noScope, string.Empty)).Items.ShouldBeEmpty();

        var me = (await companyScoped.GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!;
        me.Companies.ShouldHaveSingleItem().ShouldBe(new CompanyResponse(acme, me.Companies[0].Code, "Acme"));
        var all = (await manager.GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!;
        all.Companies.Select(c => c.Name).ShouldBe(["Acme", "Globex", "Initech"]);
    }

    [Fact]
    public async Task CompanySelector_HidesInactiveCompanies()
    {
        var tenant = await factory.NewTenantAsync();
        var manager = factory.Manager(tenant);
        await CreateCompanyAsync(manager, Company(shortName: "Açık"));
        var closed = await CreateCompanyAsync(manager, Company(shortName: "Kapalı"));
        var dto = (await manager.GetFromJsonAsync<CompanyDto>($"{Url}/{closed}", Ct))!;
        await manager.PutAsJsonAsync($"{Url}/{closed}", UpdateOf(dto) with { IsActive = false }, Ct);

        (await manager.GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!.Companies.ShouldHaveSingleItem().Name.ShouldBe("Açık");
    }

    private static async Task<PagedResult<CompanyListItem>> ListAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<PagedResult<CompanyListItem>>($"{Url}?{query}", Ct))!;

    private static UpdateCompanyRequest UpdateOf(CompanyDto c) => new(
        c.Version, c.Title, c.ShortName, c.TaxOffice, c.TaxNumber, c.MersisNo, c.SgkWorkplaceNo, c.AddressLine1, c.AddressLine2,
        c.ProvinceCode, c.DistrictId, c.PostalCode, c.Phone, c.Email, c.DefaultCulture, c.TimeZoneId, c.IsActive);
}
