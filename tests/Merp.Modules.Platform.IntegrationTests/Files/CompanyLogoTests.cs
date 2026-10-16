using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Companies;
using Merp.Modules.Platform.IntegrationTests.Organization;
using Merp.Testing.Data;
using Merp.Testing.Web;
using Merp.Web.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.IntegrationTests.Files;

public sealed class CompanyLogoTests(OrganizationFactory factory) : IClassFixture<OrganizationFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SetLogo_ShowsOnTheCompany_ReplacingDeletesTheOldFile_AndRemoveClearsIt()
    {
        var admin = factory.Admin();
        var companyId = await OrganizationFactory.CreateCompanyAsync(admin);
        var version = (await GetCompanyAsync(admin, companyId)).Version;

        var first = await SetLogoAsync(admin, companyId, "logo.png", SampleFiles.Png());
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync(Ct));
        var firstId = (await first.Content.ReadFromJsonAsync<EntityCreatedResponse>(Ct))!.Id;
        var withLogo = await GetCompanyAsync(admin, companyId);
        withLogo.LogoFileId.ShouldBe(firstId);
        withLogo.Version.ShouldBe(version);

        var image = await admin.GetAsync($"/api/platform/files/{firstId}", Ct);
        image.StatusCode.ShouldBe(HttpStatusCode.OK);
        image.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        // Pictures are shown, not downloaded
        image.Content.Headers.ContentDisposition.ShouldBeNull();
        (await image.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(SampleFiles.Png());

        var second = await SetLogoAsync(admin, companyId, "logo.jpg", SampleFiles.Jpeg());
        var secondId = (await second.Content.ReadFromJsonAsync<EntityCreatedResponse>(Ct))!.Id;
        (await GetCompanyAsync(admin, companyId)).LogoFileId.ShouldBe(secondId);
        (await admin.GetAsync($"/api/platform/files/{firstId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var old = await factory.QueryAsync("acme", db => db.StoredFiles.IgnoreQueryFilters().AsNoTracking().SingleAsync(f => f.Id == firstId, Ct));
        File.Exists(Path.Combine(factory.FilesRoot, old.StorageKey)).ShouldBeFalse();

        (await admin.DeleteAsync($"/api/platform/companies/{companyId}/logo", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetCompanyAsync(admin, companyId)).LogoFileId.ShouldBeNull();
        (await admin.GetAsync($"/api/platform/files/{secondId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A file of the company, not a field: the company keeps its version while its form is open
        (await factory.AuditedFieldsAsync("acme", "StoredFile", secondId)).ShouldContain("Purpose");
        (await factory.AuditedFieldsAsync("acme", "Company", companyId)).ShouldNotContain("LogoFileId");
    }

    [Theory]
    [InlineData("logo.svg")]
    [InlineData("logo.pdf")]
    public async Task SetLogo_WithANonImage_Returns400(string fileName)
    {
        var admin = factory.Admin();
        var companyId = await OrganizationFactory.CreateCompanyAsync(admin);

        var response = await SetLogoAsync(admin, companyId, fileName, SampleFiles.Pdf());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(response)).Errors!["file"].ShouldBe(["Bu dosya türü yüklenemez. İzin verilen türler: jpeg, jpg, png, webp."]);
    }

    [Fact]
    public async Task SetLogo_LargerThanOneMegabyte_Returns400()
    {
        var admin = factory.Admin();
        var companyId = await OrganizationFactory.CreateCompanyAsync(admin);
        var content = SampleFiles.Png().Concat(new byte[SetCompanyLogoCommand.MaxBytes]).ToArray();

        var response = await SetLogoAsync(admin, companyId, "logo.png", content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(response)).Errors!["file"].ShouldBe(["Dosya en fazla 1 MB olabilir."]);
    }

    [Fact]
    public async Task SetLogo_WithoutManagePermission_Returns403()
    {
        var companyId = await OrganizationFactory.CreateCompanyAsync(factory.Admin());
        var reader = factory.CreateClientFor("reader", "acme", PlatformPermissions.Companies.Read);

        (await SetLogoAsync(reader, companyId, "logo.png", SampleFiles.Png())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SetLogo_OfACompanyOutsideTheScope_Returns404()
    {
        var admin = factory.Admin();
        var mine = await OrganizationFactory.CreateCompanyAsync(admin);
        var other = await OrganizationFactory.CreateCompanyAsync(admin);
        var scoped = factory.Admin().WithScopes(AccessScope.Company(mine));

        (await SetLogoAsync(scoped, other, "logo.png", SampleFiles.Png())).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static Task<HttpResponseMessage> SetLogoAsync(HttpClient client, Guid companyId, string fileName, byte[] content)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var form = new MultipartFormDataContent { { file, "file", fileName } };
        return client.PutAsync($"/api/platform/companies/{companyId}/logo", form, TestContext.Current.CancellationToken);
    }

    private static async Task<CompanyDetails> GetCompanyAsync(HttpClient client, Guid companyId) =>
        (await client.GetFromJsonAsync<CompanyDetails>($"/api/platform/companies/{companyId}", TestContext.Current.CancellationToken))!;
}
