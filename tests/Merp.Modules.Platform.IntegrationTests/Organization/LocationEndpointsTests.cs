using System.Net;
using System.Net.Http.Json;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Locations;

namespace Merp.Modules.Platform.IntegrationTests.Organization;

public sealed class LocationEndpointsTests(PlatformFactory factory) : IClassFixture<PlatformFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Provinces_AreTheEightyOneProvincesByPlateCode()
    {
        var provinces = (await Client().GetFromJsonAsync<List<ProvinceDto>>("/api/platform/provinces", Ct))!;

        provinces.Count.ShouldBe(81);
        provinces.Select(p => p.Code).ShouldBe(Enumerable.Range(1, 81));
        provinces[0].ShouldBe(new ProvinceDto(1, "Adana"));
        provinces[33].ShouldBe(new ProvinceDto(34, "İstanbul"));
        provinces[80].ShouldBe(new ProvinceDto(81, "Düzce"));
    }

    [Fact]
    public async Task Districts_AreInTurkishAlphabeticalOrder()
    {
        var districts = (await Client().GetFromJsonAsync<List<DistrictDto>>("/api/platform/provinces/34/districts", Ct))!;

        districts.Count.ShouldBe(39);
        districts[0].Name.ShouldBe("Adalar");
        var names = districts.Select(d => d.Name).ToList();
        // C before Ç before D, S before Ş before T, U before Ü before V.
        names.IndexOf("Büyükçekmece").ShouldBeLessThan(names.IndexOf("Çatalca"));
        names.IndexOf("Çekmeköy").ShouldBeLessThan(names.IndexOf("Esenler"));
        names.IndexOf("Sultangazi").ShouldBeLessThan(names.IndexOf("Şile"));
        names.IndexOf("Ümraniye").ShouldBeLessThan(names.IndexOf("Zeytinburnu"));
        names[^1].ShouldBe("Zeytinburnu");
    }

    [Fact]
    public async Task Districts_OfUnknownProvince_Returns404() =>
        (await Client().GetAsync("/api/platform/provinces/82/districts", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

    [Fact]
    public async Task WithoutPermission_Returns403() =>
        (await factory.CreateClientFor("user-1", PlatformFactory.Tenant, PlatformPermissions.Companies.Read).GetAsync("/api/platform/provinces", Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

    private HttpClient Client() => factory.CreateClientFor("user-1", PlatformFactory.Tenant, PlatformPermissions.ReferenceData.Read);
}
