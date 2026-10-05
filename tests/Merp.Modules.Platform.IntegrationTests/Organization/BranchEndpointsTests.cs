using System.Net;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.Platform.Api.Endpoints;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Branches;
using Merp.Modules.Platform.Application.Companies;
using Merp.Modules.Platform.Domain.Branches;
using Merp.Testing.Web;
using static Merp.Modules.Platform.IntegrationTests.Organization.PlatformFactory;

namespace Merp.Modules.Platform.IntegrationTests.Organization;

public sealed class BranchEndpointsTests(PlatformFactory factory) : IClassFixture<PlatformFactory>
{
    private const string Url = "/api/platform/branches";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_ThenGet_ReturnsTheBranchWithItsCompany()
    {
        var client = factory.Manager();
        var company = await CreateCompanyAsync(client, Company(shortName: "Anadolu"));

        var id = await CreateBranchAsync(client, Branch(company, code: "ank-01", name: " Çankaya Mağazası "));

        var branch = (await client.GetFromJsonAsync<BranchDto>($"{Url}/{id}", Json, Ct))!;
        branch.CompanyId.ShouldBe(company);
        branch.CompanyName.ShouldBe("Anadolu");
        branch.Code.ShouldBe("ANK-01");
        branch.Name.ShouldBe("Çankaya Mağazası");
        branch.Type.ShouldBe(BranchType.Store);
        branch.ProvinceName.ShouldBe("Ankara");
        branch.DistrictName.ShouldBe("Çankaya");
        var created = (await factory.OutboxAsync(Tenant, "BranchCreated")).Where(e => e.GetProperty("branchId").GetGuid() == id).ShouldHaveSingleItem();
        created.GetProperty("companyId").GetGuid().ShouldBe(company);
        created.GetProperty("type").GetString().ShouldBe("Store");
    }

    [Fact]
    public async Task Create_SameCodeInTheSameCompany_Returns409_ButIsFreeInAnotherCompany()
    {
        var client = factory.Manager();
        var first = await CreateCompanyAsync(client, Company());
        var second = await CreateCompanyAsync(client, Company());
        await CreateBranchAsync(client, Branch(first, code: "MERKEZ"));

        var problem = await ProblemAsync(await client.PostAsJsonAsync(Url, Branch(first, code: "merkez"), Json, Ct), HttpStatusCode.Conflict);

        problem.GetProperty("code").GetString().ShouldBe("platform.branches.code_taken");
        await CreateBranchAsync(client, Branch(second, code: "MERKEZ"));
    }

    [Fact]
    public async Task Create_ForInactiveCompany_Returns400()
    {
        var client = factory.Manager();
        var company = await CreateCompanyAsync(client, Company());
        var dto = (await client.GetFromJsonAsync<CompanyDto>($"/api/platform/companies/{company}", Ct))!;
        await client.PutAsJsonAsync($"/api/platform/companies/{company}", new UpdateCompanyRequest(
            dto.Version, dto.Title, dto.ShortName, dto.TaxOffice, dto.TaxNumber, dto.MersisNo, dto.SgkWorkplaceNo, dto.AddressLine1,
            dto.AddressLine2, dto.ProvinceCode, dto.DistrictId, dto.PostalCode, dto.Phone, dto.Email, dto.DefaultCulture, dto.TimeZoneId, false), Ct);

        var problem = await ProblemAsync(await client.PostAsJsonAsync(Url, Branch(company), Json, Ct), HttpStatusCode.BadRequest);

        FieldCodes(problem).ShouldBe([("companyId", "platform.branches.inactive_company")]);
    }

    [Fact]
    public async Task Create_ForCompanyOutsideScope_Returns400LikeAnUnknownCompany()
    {
        var own = await CreateCompanyAsync(factory.Manager(), Company());
        var other = await CreateCompanyAsync(factory.Manager(), Company());

        var problem = await ProblemAsync(
            await factory.Manager().WithScopes($"Company:{own}").PostAsJsonAsync(Url, Branch(other), Json, Ct),
            HttpStatusCode.BadRequest);

        FieldCodes(problem).ShouldBe([("companyId", "platform.branches.unknown_company")]);
    }

    [Fact]
    public async Task Create_WithUnknownType_Returns400()
    {
        var company = await CreateCompanyAsync(factory.Manager(), Company());
        var body = new
        {
            companyId = company, code = "X1", name = "X", type = "Castle", addressLine1 = "X", provinceCode = Locations.Ankara,
            districtId = Locations.Cankaya,
        };

        (await factory.Manager().PostAsJsonAsync(Url, body, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_WithInvalidFields_Returns400()
    {
        var company = await CreateCompanyAsync(factory.Manager(), Company());

        var problem = await ProblemAsync(
            await factory.Manager().PostAsJsonAsync(Url, Branch(company) with { Name = " ", SgkWorkplaceNo = "12" }, Json, Ct),
            HttpStatusCode.BadRequest);

        FieldCodes(problem).ShouldBe([("name", "common.errors.required"), ("sgkWorkplaceNo", "common.errors.invalid_sgk_workplace_no")], ignoreOrder: true);
    }

    [Fact]
    public async Task Create_WithoutPermission_Returns403()
    {
        var company = await CreateCompanyAsync(factory.Manager(), Company());

        (await factory.CreateClientFor("reader-1", Tenant, PlatformPermissions.Branches.Read).PostAsJsonAsync(Url, Branch(company), Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Update_ChangesTheBranch_AndRejectsAStaleVersion()
    {
        var client = factory.Manager();
        var company = await CreateCompanyAsync(client, Company());
        var id = await CreateBranchAsync(client, Branch(company));
        var before = (await client.GetFromJsonAsync<BranchDto>($"{Url}/{id}", Json, Ct))!;

        var response = await client.PutAsJsonAsync($"{Url}/{id}", UpdateOf(before) with { Name = "Kızılay Depo", Type = BranchType.Warehouse }, Json, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(Ct));
        var after = (await client.GetFromJsonAsync<BranchDto>($"{Url}/{id}", Json, Ct))!;
        after.Name.ShouldBe("Kızılay Depo");
        after.Type.ShouldBe(BranchType.Warehouse);
        (await factory.OutboxAsync(Tenant, "BranchUpdated")).ShouldContain(e => e.GetProperty("branchId").GetGuid() == id);

        var problem = await ProblemAsync(await client.PutAsJsonAsync($"{Url}/{id}", UpdateOf(before), Json, Ct), HttpStatusCode.Conflict);
        problem.GetProperty("code").GetString().ShouldBe("common.errors.concurrency_conflict");
    }

    [Fact]
    public async Task BranchScope_CanReadAndUpdateItsBranchOnly()
    {
        var manager = factory.Manager();
        var company = await CreateCompanyAsync(manager, Company());
        var own = await CreateBranchAsync(manager, Branch(company));
        var other = await CreateBranchAsync(manager, Branch(company));
        var scoped = factory.Manager().WithScopes($"Branch:{own}");

        var dto = (await scoped.GetFromJsonAsync<BranchDto>($"{Url}/{own}", Json, Ct))!;
        (await scoped.PutAsJsonAsync($"{Url}/{own}", UpdateOf(dto) with { Name = "Yeni ad" }, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await scoped.GetAsync($"{Url}/{other}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var otherDto = (await manager.GetFromJsonAsync<BranchDto>($"{Url}/{other}", Json, Ct))!;
        (await scoped.PutAsJsonAsync($"{Url}/{other}", UpdateOf(otherDto), Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // Creating needs the company in scope, not just one of its branches.
        (await scoped.PostAsJsonAsync(Url, Branch(company), Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_IsLimitedByScope_AndNarrowedByCompanyAndActiveCompanyHeader()
    {
        var tenant = await factory.NewTenantAsync();
        var manager = factory.Manager(tenant);
        var acme = await CreateCompanyAsync(manager, Company(shortName: "Acme"));
        var globex = await CreateCompanyAsync(manager, Company(shortName: "Globex"));
        var acmeAnkara = await CreateBranchAsync(manager, Branch(acme, name: "Ankara"));
        var acmeIzmir = await CreateBranchAsync(manager, Branch(acme, name: "İzmir") with { ProvinceCode = Locations.Izmir, DistrictId = Locations.Konak });
        var globexAnkara = await CreateBranchAsync(manager, Branch(globex, name: "Bursa"));

        (await ListAsync(manager, string.Empty)).Items.Select(b => b.Id).ShouldBe([acmeAnkara, globexAnkara, acmeIzmir]);
        (await ListAsync(manager, $"companyId={globex}")).Items.Select(b => b.Id).ShouldBe([globexAnkara]);
        (await ListAsync(factory.Manager(tenant).WithCompany(acme), string.Empty)).Items.Select(b => b.Id).ShouldBe([acmeAnkara, acmeIzmir]);
        (await ListAsync(manager, "filter=izmir")).Items.Select(b => b.Id).ShouldBe([acmeIzmir]);
        (await ListAsync(manager, "sort=-companyCode&pageSize=1")).TotalCount.ShouldBe(3);

        HttpClient CompanyScoped() => factory.CreateClientFor("hr-1", tenant, PlatformPermissions.Branches.Read).WithScopes($"Company:{globex}");
        var scoped = (await ListAsync(CompanyScoped(), string.Empty)).Items.ShouldHaveSingleItem();
        scoped.Id.ShouldBe(globexAnkara);
        scoped.CompanyName.ShouldBe("Globex");
        // The active company header narrows the scope; it never widens it.
        (await ListAsync(CompanyScoped().WithCompany(acme), string.Empty)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task List_WithInvalidSort_Returns400() =>
        FieldCodes(await ProblemAsync(await factory.Manager().GetAsync($"{Url}?sort=sgkWorkplaceNo", Ct), HttpStatusCode.BadRequest))
            .ShouldBe([("sort", "common.errors.invalid_sort")]);

    [Fact]
    public async Task List_WithoutPermission_Returns403() =>
        (await factory.CreateClientFor("user-1", Tenant, PlatformPermissions.Companies.Read).GetAsync(Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

    private static async Task<PagedResult<BranchListItem>> ListAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<PagedResult<BranchListItem>>($"{Url}?{query}", Json, Ct))!;

    private static UpdateBranchRequest UpdateOf(BranchDto b) => new(
        b.Version, b.Name, b.Type, b.AddressLine1, b.AddressLine2, b.ProvinceCode, b.DistrictId, b.PostalCode, b.SgkWorkplaceNo, b.IsActive);
}
