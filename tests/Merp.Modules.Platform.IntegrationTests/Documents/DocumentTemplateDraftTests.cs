using System.Net;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Security;
using Merp.Infrastructure.AI;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Documents;
using Merp.Modules.Platform.IntegrationTests.Organization;
using Merp.Testing.Web;

namespace Merp.Modules.Platform.IntegrationTests.Documents;

/// <summary>
/// AI drafts of document templates (PLAN A10, D63): from a description with the type's variables, checked against the
/// sample model and asked for once more when the draft does not render; nothing is saved.
/// </summary>
public sealed class DocumentTemplateDraftTests : IClassFixture<DocumentFactory>
{
    private const string Url = "/api/platform/document-templates/draft";

    private readonly DocumentFactory _factory;

    public DocumentTemplateDraftTests(DocumentFactory factory)
    {
        _factory = factory;
        _factory.Ai.Reset();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ADraft_ComesFromTheDescription_WithTheTypesVariables_AndRendersTheSample()
    {
        _factory.Ai.Returns(new DocumentTemplateDraftResult(
            "{% render 'letterhead' %}<h1>{{ letter.subject }}</h1>",
            "h1 { font-size: 16pt; }",
            ["İmza alanını kontrol edin.", " "]));

        var draft = await DraftAsync(new
        {
            documentType = PlatformDocumentTypes.Letter,
            culture = "tr-TR",
            description = "Üstte antet, ortada konu. Sorular için ik@acme.example",
        });

        draft.ShouldSatisfyAllConditions(
            d => d.Body.ShouldBe("{% render 'letterhead' %}<h1>{{ letter.subject }}</h1>"),
            d => d.Css.ShouldBe("h1 { font-size: 16pt; }"),
            d => d.Notes.ShouldBe(["İmza alanını kontrol edin."]),
            d => d.Valid.ShouldBeTrue(),
            d => d.PromptVersion.ShouldBe(DocumentTemplateDraftPrompt.Version));
        var sent = _factory.Ai.Requests.ShouldHaveSingleItem();
        sent.Feature.ShouldBe(PlatformAiFeatures.DocumentTemplateDraft);
        sent.Instructions.ShouldBe(DocumentTemplateDraftPrompt.Instructions);
        sent.Input.ShouldSatisfyAllConditions(
            i => i.ShouldStartWith("Output language: Turkish"),
            i => i.ShouldContain("<variable path=\"letter.subject\">Yazının konusu</variable>"),
            i => i.ShouldContain("<variable path=\"company.title\">"),
            i => i.ShouldContain("<sample_model>"),
            i => i.ShouldContain("Üstte antet, ortada konu."),
            i => i.ShouldNotContain("ik@acme.example"),
            i => i.ShouldNotContain("<current_body>"));
    }

    [Fact]
    public async Task ADraftThatDoesNotRender_IsAskedForOnceMoreWithTheError()
    {
        _factory.Ai.Returns(new DocumentTemplateDraftResult("{% if %}<p>Yarım</p>", string.Empty, []));
        _factory.Ai.Returns(new DocumentTemplateDraftResult("<p>{{ letter.subject }}</p>", string.Empty, []));

        var fixedDraft = await DraftAsync(new
        {
            documentType = PlatformDocumentTypes.Letter,
            description = "Konuyu kalın yaz.",
            currentBody = "<p>{{ letter.subject }}</p>",
            currentCss = "p { margin: 0; }",
        });

        fixedDraft.ShouldSatisfyAllConditions(d => d.Valid.ShouldBeTrue(), d => d.Body.ShouldBe("<p>{{ letter.subject }}</p>"));
        var requests = _factory.Ai.Requests.ToList();
        requests.Count.ShouldBe(2);
        requests[0].Input.ShouldContain("<current_body>\n<p>{{ letter.subject }}</p>\n</current_body>");
        requests[0].Input.ShouldNotContain("<previous_attempt_error>");
        requests[1].Input.ShouldContain("<previous_attempt_error>");

        // Twice wrong: the last draft comes back marked, for the editor's preview to show the error
        _factory.Ai.Reset();
        _factory.Ai.Returns(new DocumentTemplateDraftResult("{% if %}", string.Empty, []));
        _factory.Ai.Returns(new DocumentTemplateDraftResult("{% for %}", string.Empty, []));
        var broken = await DraftAsync(new { documentType = PlatformDocumentTypes.Letter, description = "Basit bir yazı." });
        broken.ShouldSatisfyAllConditions(d => d.Valid.ShouldBeFalse(), d => d.Body.ShouldBe("{% for %}"));
        _factory.Ai.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Requests_AreChecked_AndNeedTheTenantScope()
    {
        var admin = _factory.Administrator();
        (await Problem.ReadAsync(await admin.PostAsJsonAsync(Url, new { documentType = "platform.unknown", description = "x" }, Ct))).Errors!.ShouldContainKey("documentType");
        (await Problem.ReadAsync(await admin.PostAsJsonAsync(Url, new { documentType = PlatformDocumentTypes.Letter, description = "" }, Ct))).Errors!.ShouldContainKey("description");
        (await admin.PostAsJsonAsync(Url, new { documentType = PlatformDocumentTypes.Letter, culture = "xx-XX", description = "x" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await _factory.Reader().PostAsJsonAsync(Url, new { documentType = PlatformDocumentTypes.Letter, description = "x" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var scoped = _factory.CreateClientFor("document-scoped", "acme", PlatformPermissions.DocumentTemplates.Manage).WithScopes(AccessScope.Company(Guid.CreateVersion7()));
        var refused = await scoped.PostAsJsonAsync(Url, new { documentType = PlatformDocumentTypes.Letter, description = "x" }, Ct);
        (await Problem.ReadAsync(refused)).Code.ShouldBe("platform.documents.errors.tenant_scope_required");

        _factory.Ai.IsConfigured = false;
        var disabled = await admin.PostAsJsonAsync(Url, new { documentType = PlatformDocumentTypes.Letter, description = "x" }, Ct);
        disabled.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Problem.ReadAsync(disabled)).Code.ShouldBe("common.errors.ai_disabled");
        _factory.Ai.Requests.ShouldBeEmpty();
    }

    private async Task<DocumentTemplateDraft> DraftAsync(object body)
    {
        var response = await _factory.Administrator().PostAsJsonAsync(Url, body, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<DocumentTemplateDraft>(AiSchema.SerializerOptions, Ct))!;
    }
}
