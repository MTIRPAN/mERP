using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Api.Endpoints;
using Merp.Modules.Platform.Application.Documents;
using Merp.Modules.Platform.Contracts.Documents;
using Merp.Modules.Platform.IntegrationTests.Organization;
using Merp.Testing.Data;
using Merp.Testing.Web;
using UglyToad.PdfPig;

namespace Merp.Modules.Platform.IntegrationTests.Documents;

/// <summary>Document templates, previews and PDFs (PLAN P12, D36).</summary>
public sealed class DocumentTemplateEndpointsTests(DocumentFactory factory) : IClassFixture<DocumentFactory>
{
    private const string Url = "/api/platform/document-templates";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Types_ListTheLetter_WithItsSampleFieldsAndPartials()
    {
        var types = (await factory.Reader().GetFromJsonAsync<List<DocumentTypeItem>>($"{Url}/types", Ct))!;

        var letter = types.Single(t => t.Key == PlatformDocumentTypes.Letter);
        letter.Name.ShouldBe("Antetli genel yazı");
        letter.DefaultBody.ShouldContain("{% render 'letterhead' %}");
        letter.Fields.ShouldContain(new DocumentFieldItem("letter.subject", "Yazının konusu"));
        letter.Fields.ShouldContain(new DocumentFieldItem("company.title", "Şirketin ticaret unvanı"));
        letter.Partials.Select(p => p.Path).ShouldBe(["logo", "letterhead"]);
    }

    [Fact]
    public async Task Preview_OfTheBuiltInTemplate_UsesTheCompany_InTheDocumentsLanguage()
    {
        var admin = factory.Administrator();
        var companyId = await OrganizationFactory.CreateCompanyAsync(admin);

        var html = await PreviewAsync(admin, companyId);

        html.ShouldContain("<html lang=\"tr-TR\">");
        html.ShouldContain("Acme Holding Anonim Şirketi");
        html.ShouldContain("Atatürk Cad. No: 1");
        html.ShouldContain("<th>Konu</th>");
        html.ShouldContain("Çalışma belgesi talebi hk.");
        html.ShouldContain("VKN:");
    }

    [Fact]
    public async Task TheCompanysTemplate_ComesFirst_ThenTheGeneralOne_ThenTheBuiltInOne()
    {
        var admin = factory.Administrator();
        var companyId = await OrganizationFactory.CreateCompanyAsync(admin);
        var otherId = await OrganizationFactory.CreateCompanyAsync(admin);
        var general = await CreateAsync(admin, null, "<p>GENEL {{ letter.subject }}</p>");
        await CreateAsync(admin, companyId, "<p>ŞİRKETE ÖZEL {{ company.title }}</p>");

        (await PreviewAsync(admin, companyId)).ShouldContain("<p>ŞİRKETE ÖZEL Acme Holding Anonim Şirketi</p>");
        (await PreviewAsync(admin, otherId)).ShouldContain("<p>GENEL Çalışma belgesi talebi hk.</p>");

        var details = (await admin.GetFromJsonAsync<DocumentTemplateDetails>($"{Url}/{general}", Ct))!;
        var off = await admin.PutAsJsonAsync($"{Url}/{general}", new { version = details.Version, body = details.Body, css = details.Css, isActive = false }, Ct);
        off.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await PreviewAsync(admin, otherId)).ShouldContain("class=\"letterhead\"");
    }

    [Fact]
    public async Task Create_WithABrokenTemplate_Returns400WithTheLine()
    {
        // Each test uses its own company: another test creates the template for all companies
        var admin = factory.Administrator();
        var companyId = await OrganizationFactory.CreateCompanyAsync(admin);

        var response = await admin.PostAsJsonAsync(Url, Body(companyId: companyId, body: "<p>\n{% if letter %}\n"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(response)).Errors!["body"].ShouldHaveSingleItem().ShouldStartWith("Şablon ");
    }

    [Fact]
    public async Task Create_WithAnUnknownPartial_Returns400()
    {
        var admin = factory.Administrator();
        var companyId = await OrganizationFactory.CreateCompanyAsync(admin);

        var response = await admin.PostAsJsonAsync(Url, Body(companyId: companyId, body: "{% render 'footer' %}"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(response)).Errors!["body"].ShouldHaveSingleItem().ShouldStartWith("Şablon ");
    }

    [Fact]
    public async Task Create_WithAnUnknownType_OrTwice_Returns400()
    {
        var admin = factory.Administrator();
        var companyId = await OrganizationFactory.CreateCompanyAsync(admin);
        await CreateAsync(admin, companyId, "<p>bir</p>");

        var unknown = await admin.PostAsJsonAsync(Url, Body(type: "hr.nonexistent"), Ct);
        var twice = await admin.PostAsJsonAsync(Url, Body(companyId: companyId), Ct);

        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(unknown)).Errors!["documentType"].ShouldBe(["Böyle bir belge türü yok."]);
        twice.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(twice)).Errors!["documentType"].ShouldBe(["Bu değer başka bir kayıtta kullanılıyor."]);
    }

    [Fact]
    public async Task Update_RaisesTheRevision_RefusesAStaleVersion_AndDeleteRemoves()
    {
        var admin = factory.Administrator();
        var companyId = await OrganizationFactory.CreateCompanyAsync(admin);
        var id = await CreateAsync(admin, companyId, "<p>bir</p>");
        var version = (await admin.GetFromJsonAsync<DocumentTemplateDetails>($"{Url}/{id}", Ct))!.Version;

        var update = await admin.PutAsJsonAsync($"{Url}/{id}", new { version, body = "<p>iki</p>", css = "p { color: #333; }", isActive = true }, Ct);
        update.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.PutAsJsonAsync($"{Url}/{id}", new { version, body = "<p>üç</p>", isActive = true }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var details = (await admin.GetFromJsonAsync<DocumentTemplateDetails>($"{Url}/{id}", Ct))!;
        details.Revision.ShouldBe(2);
        details.Css.ShouldBe("p { color: #333; }");
        (await admin.GetFromJsonAsync<List<DocumentTemplateItem>>($"{Url}?documentType={PlatformDocumentTypes.Letter}", Ct))!
            .ShouldContain(t => t.Id == id && t.CompanyName == "Acme" && t.Revision == 2);
        (await factory.AuditedFieldsAsync(id)).ShouldContain("Body");

        (await admin.DeleteAsync($"{Url}/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.DeleteAsync($"{Url}/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WithoutManagePermission_OrTheTenantScope_Returns403()
    {
        var admin = factory.Administrator();
        var companyId = await OrganizationFactory.CreateCompanyAsync(admin);
        var reader = factory.Reader();
        var scoped = factory.Administrator().WithScopes(AccessScope.Company(companyId));

        (await reader.GetAsync(Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.PostAsJsonAsync(Url, Body(), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await factory.CreateClientFor("nobody", "acme").GetAsync($"{Url}/types", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var response = await scoped.PostAsJsonAsync(Url, Body(companyId: companyId), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Problem.ReadAsync(response)).Code.ShouldBe("platform.documents.errors.tenant_scope_required");
    }

    [Fact]
    public async Task PdfPreview_PrintsTurkishLetters_AndTheLogo()
    {
        var admin = factory.Administrator();
        var companyId = await OrganizationFactory.CreateCompanyAsync(admin);
        var logo = new ByteArrayContent(SampleFiles.Png());
        logo.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        (await admin.PutAsync($"/api/platform/companies/{companyId}/logo", new MultipartFormDataContent { { logo, "file", "logo.png" } }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await admin.PostAsJsonAsync($"{Url}/preview/pdf", new { documentType = PlatformDocumentTypes.Letter, companyId, culture = "tr-TR" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        using var pdf = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync(Ct));
        var page = pdf.GetPage(1);
        var text = string.Join(" ", page.GetWords().Select(w => w.Text));
        text.ShouldContain("Şirketi");
        text.ShouldContain("Çalışma");
        text.ShouldContain("Öztürk");
        text.ShouldContain("Yılmaz");
        page.GetImages().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Generator_PrintsWithTheTenantsTemplate_ForOtherModules()
    {
        var admin = factory.Administrator();
        var companyId = await OrganizationFactory.CreateCompanyAsync(admin);
        var templateId = await CreateAsync(admin, companyId, "<h1>{{ letter.subject }}</h1><p>{{ document.date }}</p>");

        var document = await factory.GenerateAsync(new DocumentRequest(
            PlatformDocumentTypes.Letter,
            companyId,
            "tr-TR",
            new Dictionary<string, object?> { ["letter"] = new Dictionary<string, object?> { ["subject"] = "Ücret bordrosu ğüşiöç" } }));

        document.TemplateId.ShouldBe(templateId);
        document.Revision.ShouldBe(1);
        using var pdf = PdfDocument.Open(document.Content);
        string.Join(" ", pdf.GetPage(1).GetWords().Select(w => w.Text)).ShouldContain("ğüşiöç");
    }

    private static object Body(string type = PlatformDocumentTypes.Letter, Guid? companyId = null, string body = "<p>{{ letter.subject }}</p>") =>
        new { documentType = type, companyId, culture = "tr-TR", body, css = (string?)null };

    private static async Task<Guid> CreateAsync(HttpClient admin, Guid? companyId, string body)
    {
        var response = await admin.PostAsJsonAsync(Url, Body(companyId: companyId, body: body), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (await response.Content.ReadFromJsonAsync<EntityCreatedResponse>(TestContext.Current.CancellationToken))!.Id;
    }

    private static async Task<string> PreviewAsync(HttpClient admin, Guid companyId)
    {
        var response = await admin.PostAsJsonAsync($"{Url}/preview", new { documentType = PlatformDocumentTypes.Letter, companyId, culture = "tr-TR" }, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (await response.Content.ReadFromJsonAsync<DocumentPreview>(TestContext.Current.CancellationToken))!.Html;
    }
}
