using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Files;
using Merp.Modules.Platform.IntegrationTests.Organization;
using Merp.Testing.Data;
using Merp.Testing.Web;
using Merp.Web.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.IntegrationTests.Files;

/// <summary>Attachments of companies (PLAN P7, D31): the company's permissions and data scope decide.</summary>
public sealed class AttachmentEndpointsTests(OrganizationFactory factory) : IClassFixture<OrganizationFactory>
{
    private const string Url = "/api/platform/attachments";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Upload_List_Download_Delete()
    {
        var admin = factory.Admin();
        var companyId = await OrganizationFactory.CreateCompanyAsync(admin);
        var content = SampleFiles.Pdf();

        var upload = await admin.PostAsync(Url, Form(companyId, "Sözleşme.pdf", content), Ct);

        upload.StatusCode.ShouldBe(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync(Ct));
        var id = (await upload.Content.ReadFromJsonAsync<EntityCreatedResponse>(Ct))!.Id;
        upload.Headers.Location!.ToString().ShouldBe($"/api/platform/files/{id}");

        var list = (await admin.GetFromJsonAsync<AttachmentList>(ListUrl(companyId), Ct))!;
        list.CanManage.ShouldBeTrue();
        list.Extensions.ShouldContain("pdf");
        list.MaxBytes.ShouldBeGreaterThan(0);
        var item = list.Items.ShouldHaveSingleItem();
        item.Id.ShouldBe(id);
        item.FileName.ShouldBe("Sözleşme.pdf");
        item.ContentType.ShouldBe("application/pdf");
        item.Length.ShouldBe(content.Length);

        var download = await admin.GetAsync($"/api/platform/files/{id}", Ct);
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(content);
        download.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        download.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        download.Content.Headers.ContentDisposition.FileNameStar.ShouldBe("Sözleşme.pdf");
        download.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);

        var audited = await factory.AuditedFieldsAsync("acme", "StoredFile", id);
        audited.ShouldContain("FileName");
        audited.ShouldContain("Sha256");

        var delete = await admin.DeleteAsync($"{Url}/{id}", Ct);
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await admin.GetFromJsonAsync<AttachmentList>(ListUrl(companyId), Ct))!.Items.ShouldBeEmpty();
        (await admin.GetAsync($"/api/platform/files/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // The content is removed right after the commit; the row stays for the audit trail
        var stored = await factory.QueryAsync("acme", db => db.StoredFiles.IgnoreQueryFilters().AsNoTracking().SingleAsync(f => f.Id == id, Ct));
        stored.IsDeleted.ShouldBeTrue();
        File.Exists(Path.Combine(factory.FilesRoot, stored.StorageKey)).ShouldBeFalse();
    }

    [Fact]
    public async Task Upload_OfANotAllowedType_Returns400WithTheAllowedTypes()
    {
        var admin = factory.Admin();
        var companyId = await OrganizationFactory.CreateCompanyAsync(admin);

        var response = await admin.PostAsync(Url, Form(companyId, "kurulum.exe", SampleFiles.Executable()), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await Problem.ReadAsync(response);
        problem.Errors!["file"].ShouldHaveSingleItem().ShouldStartWith("Bu dosya türü yüklenemez. İzin verilen türler: csv, docx, jpeg, jpg, pdf");
    }

    [Fact]
    public async Task Upload_OfAProgramRenamedToPdf_Returns400()
    {
        var admin = factory.Admin();
        var companyId = await OrganizationFactory.CreateCompanyAsync(admin);

        var response = await admin.PostAsync(Url, Form(companyId, "fatura.pdf", SampleFiles.Executable()), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(response)).Errors!["file"].ShouldBe(
            ["Dosyanın içeriği uzantısıyla uyuşmuyor. Dosya bozuk olabilir ya da uzantısı değiştirilmiş olabilir."]);
    }

    [Fact]
    public async Task Upload_WithoutAFile_Returns400()
    {
        var admin = factory.Admin();
        var companyId = await OrganizationFactory.CreateCompanyAsync(admin);
        using var form = new MultipartFormDataContent
        {
            { new StringContent(PlatformFileOwners.Company), "ownerType" },
            { new StringContent(companyId.ToString()), "ownerId" },
        };

        var response = await admin.PostAsync(Url, form, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(response)).Errors!["file"].ShouldBe(["Bu alan zorunludur."]);
    }

    [Fact]
    public async Task ReadOnlyUser_SeesAndDownloads_ButCannotUploadOrDelete()
    {
        var admin = factory.Admin();
        var companyId = await OrganizationFactory.CreateCompanyAsync(admin);
        var id = await UploadAsync(admin, companyId);
        var reader = factory.CreateClientFor("reader", "acme", PlatformPermissions.Companies.Read);

        var list = (await reader.GetFromJsonAsync<AttachmentList>(ListUrl(companyId), Ct))!;
        list.CanManage.ShouldBeFalse();
        list.Items.ShouldHaveSingleItem();
        (await reader.GetAsync($"/api/platform/files/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await reader.PostAsync(Url, Form(companyId, "a.pdf", SampleFiles.Pdf()), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.DeleteAsync($"{Url}/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CompanyOutsideTheScope_HasNoFiles()
    {
        var admin = factory.Admin();
        var mine = await OrganizationFactory.CreateCompanyAsync(admin);
        var other = await OrganizationFactory.CreateCompanyAsync(admin);
        var id = await UploadAsync(admin, other);
        var scoped = factory.Admin().WithScopes(AccessScope.Company(mine));

        (await scoped.GetAsync(ListUrl(other), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await scoped.GetAsync($"/api/platform/files/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await scoped.PostAsync(Url, Form(other, "a.pdf", SampleFiles.Pdf()), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await scoped.DeleteAsync($"{Url}/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await scoped.GetAsync(ListUrl(mine), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task FilesOfAnotherTenant_AreNotFound()
    {
        var id = await UploadAsync(factory.Admin("globex"), await OrganizationFactory.CreateCompanyAsync(factory.Admin("globex")));

        (await factory.Admin().GetAsync($"/api/platform/files/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UserWithoutCompanyPermissions_GetsNotFound()
    {
        var admin = factory.Admin();
        var companyId = await OrganizationFactory.CreateCompanyAsync(admin);
        var id = await UploadAsync(admin, companyId);
        var stranger = factory.CreateClientFor("stranger", "acme", PlatformPermissions.Branches.Read);

        (await stranger.GetAsync(ListUrl(companyId), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.GetAsync($"/api/platform/files/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("hr.employee")]
    [InlineData("platform.unknown")]
    public async Task UnknownOwnerType_IsNotFound(string ownerType)
    {
        var admin = factory.Admin();

        (await admin.GetAsync($"{Url}?ownerType={ownerType}&ownerId={Guid.NewGuid()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await admin.PostAsync(Url, Form(Guid.NewGuid(), "a.pdf", SampleFiles.Pdf(), ownerType), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    internal static MultipartFormDataContent Form(Guid ownerId, string fileName, byte[] content, string ownerType = PlatformFileOwners.Company)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent
        {
            { new StringContent(ownerType), "ownerType" },
            { new StringContent(ownerId.ToString()), "ownerId" },
            { file, "file", fileName },
        };
    }

    internal static async Task<Guid> UploadAsync(HttpClient client, Guid companyId)
    {
        var response = await client.PostAsync(Url, Form(companyId, "rapor.pdf", SampleFiles.Pdf()), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (await response.Content.ReadFromJsonAsync<EntityCreatedResponse>(TestContext.Current.CancellationToken))!.Id;
    }

    private static string ListUrl(Guid companyId) => $"{Url}?ownerType={PlatformFileOwners.Company}&ownerId={companyId}";
}
