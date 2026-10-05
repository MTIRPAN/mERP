using System.Net;
using System.Net.Http.Json;
using Merp.Modules.Platform.Application.Locations;

namespace Merp.Modules.Platform.IntegrationTests.Organization;

public sealed class LocationEndpointsTests(OrganizationFactory factory) : IClassFixture<OrganizationFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Provinces_AreTheEightyOneProvinces_InTurkishOrder()
    {
        // Reference data needs no permission, only a signed-in user (D22)
        var client = factory.CreateClientFor("calisan", "acme");

        var provinces = (await client.GetFromJsonAsync<List<LocationOption>>("/api/platform/locations/provinces", Ct))!;

        provinces.Count.ShouldBe(81);
        provinces[0].ShouldBe(new LocationOption(1, "Adana"));
        provinces.Single(p => p.Code == 34).Name.ShouldBe("İstanbul");
        var names = provinces.Select(p => p.Name).ToList();
        names.IndexOf("Çanakkale").ShouldBeGreaterThan(names.IndexOf("Bursa"));
        names.IndexOf("Çanakkale").ShouldBeLessThan(names.IndexOf("Denizli"));
        names.IndexOf("Iğdır").ShouldBeLessThan(names.IndexOf("İstanbul"));
        names.IndexOf("Şanlıurfa").ShouldBeGreaterThan(names.IndexOf("Sivas"));
    }

    [Fact]
    public async Task Districts_OfAProvince()
    {
        var client = factory.CreateClientFor("calisan", "acme");

        var districts = (await client.GetFromJsonAsync<List<LocationOption>>("/api/platform/locations/provinces/34/districts", Ct))!;

        districts.Count.ShouldBe(39);
        districts.ShouldContain(new LocationOption(1421, "Kadıköy"));
        districts[0].Name.ShouldBe("Adalar");
    }

    [Fact]
    public async Task Districts_OfAnUnknownProvince_Return404()
    {
        var response = await factory.CreateClientFor("calisan", "acme").GetAsync("/api/platform/locations/provinces/99/districts", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AnonymousCallers_Get401()
    {
        var response = await factory.CreateClient().GetAsync("/api/platform/locations/provinces", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
