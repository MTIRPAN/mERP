using System.Net;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Paging;
using Merp.Infrastructure.AI;
using Merp.Modules.Platform.Api.Endpoints;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Localization;
using Merp.Modules.Platform.IntegrationTests.Access;
using Merp.Modules.Platform.IntegrationTests.Organization;

namespace Merp.Modules.Platform.IntegrationTests.Localization;

/// <summary>
/// AI drafts of missing translations (PLAN A7, D60): drafted from the default language for the list's filter, never
/// stored until a user saves them, with a warning when a placeholder got lost.
/// </summary>
public sealed class TranslationDraftTests : IClassFixture<AccessFactory>
{
    private const string Url = "/api/platform/localization";
    private const string Culture = "fr-FR";

    private readonly AccessFactory _factory;

    public TranslationDraftTests(AccessFactory factory)
    {
        _factory = factory;
        _factory.Ai.Reset();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Root => _factory.AsRoot();

    [Fact]
    public async Task Drafts_ComeFromTheDefaultLanguage_AndAreSavedOnlyAfterTheReview()
    {
        await AddFrenchAsync();
        var missing = await MissingAsync("web.common.", 100);
        var picked = missing.Items.Take(2).ToList();
        _factory.Ai.Returns(new TranslationDraftResult([new(picked[0].Key, "Premier"), new(picked[1].Key, " Second "), new("web.unknown", "x")]));

        var response = await Root.PostAsJsonAsync($"{Url}/texts/{Culture}/drafts", new { prefix = "web.common.", count = 2 }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var drafts = (await response.Content.ReadFromJsonAsync<TranslationDraftResponse>(AiSchema.SerializerOptions, Ct))!;
        drafts.ShouldSatisfyAllConditions(
            d => d.Drafts.Select(x => (x.Key, x.Source, x.Text)).ShouldBe([(picked[0].Key, picked[0].DefaultText!, "Premier"), (picked[1].Key, picked[1].DefaultText!, "Second")]),
            d => d.Remaining.ShouldBe(missing.TotalCount - 2),
            d => d.SourceCulture.ShouldBe("tr-TR"),
            d => d.PromptVersion.ShouldBe(TranslationDraftPrompt.Version));
        var sent = _factory.Ai.Requests.ShouldHaveSingleItem();
        sent.Feature.ShouldBe(PlatformAiFeatures.TranslationDrafts);
        sent.Instructions.ShouldBe(TranslationDraftPrompt.Instructions);
        sent.Input.ShouldSatisfyAllConditions(
            i => i.ShouldContain("Source language: Turkish"),
            i => i.ShouldContain("Output language: French"),
            i => i.ShouldContain($"<text key=\"{picked[0].Key}\">{picked[0].DefaultText}</text>"));

        // Nothing is stored until the user saves the reviewed drafts
        (await MissingAsync("web.common.", 100)).TotalCount.ShouldBe(missing.TotalCount);
        var saved = await Root.PostAsJsonAsync($"{Url}/texts/{Culture}/bulk", new { texts = drafts.Drafts.Select(d => new { key = d.Key, value = d.Text }) }, Ct);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Ct));
        (await saved.Content.ReadFromJsonAsync<SetTextsResponse>(Ct))!.Changed.ShouldBe(2);
        (await MissingAsync("web.common.", 100)).TotalCount.ShouldBe(missing.TotalCount - 2);
    }

    [Fact]
    public async Task ADraftThatLostAPlaceholder_IsFlagged()
    {
        await AddFrenchAsync();
        const string key = "web.localization.cultures.delete_confirm";
        _factory.Ai.Returns(new TranslationDraftResult([new(key, "Supprimer la langue et ses traductions ?")]));

        var drafts = await DraftAsync(new { prefix = key, count = 1 });

        var draft = drafts.Drafts.ShouldHaveSingleItem();
        draft.Source.ShouldContain("{name}");
        draft.PlaceholdersMatch.ShouldBeFalse();
    }

    [Fact]
    public async Task Requests_AreChecked_AndNeedTheTenantScope()
    {
        await AddFrenchAsync();

        var source = await Root.PostAsJsonAsync($"{Url}/texts/tr-TR/drafts", new { prefix = "web.common." }, Ct);
        (await Problem.ReadAsync(source)).Errors!.ShouldContainKey("culture");
        (await Root.PostAsJsonAsync($"{Url}/texts/{Culture}/drafts", new { count = DraftTranslationsQuery.MaxCount + 1 }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Root.PostAsJsonAsync($"{Url}/texts/xx-XX/drafts", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var unknown = await Root.PostAsJsonAsync($"{Url}/texts/{Culture}/bulk", new { texts = new[] { new { key = "web.unknown.key", value = "x" } } }, Ct);
        (await Problem.ReadAsync(unknown)).Errors!.ShouldContainKey("texts[0].key");
        (await Root.PostAsJsonAsync($"{Url}/texts/{Culture}/bulk", new { texts = Array.Empty<object>() }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var (reader, _) = await _factory.UserWithAsync([PlatformPermissions.Localization.Read]);
        (await reader.PostAsJsonAsync($"{Url}/texts/{Culture}/drafts", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.PostAsJsonAsync($"{Url}/texts/{Culture}/bulk", new { texts = new[] { new { key = "web.common.save", value = "x" } } }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var companyId = await OrganizationFactory.CreateCompanyAsync(_factory.AsRoot());
        var (companyScoped, _) = await _factory.UserWithAsync([PlatformPermissions.Localization.Manage], "Company", companyId);
        var scoped = await companyScoped.PostAsJsonAsync($"{Url}/texts/{Culture}/drafts", new { }, Ct);
        (await Problem.ReadAsync(scoped)).Code.ShouldBe("platform.localization.errors.tenant_scope_required");

        _factory.Ai.IsConfigured = false;
        var disabled = await Root.PostAsJsonAsync($"{Url}/texts/{Culture}/drafts", new { }, Ct);
        disabled.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Problem.ReadAsync(disabled)).Code.ShouldBe("common.errors.ai_disabled");
        _factory.Ai.Requests.ShouldBeEmpty();
    }

    private async Task<TranslationDraftResponse> DraftAsync(object body)
    {
        var response = await Root.PostAsJsonAsync($"{Url}/texts/{Culture}/drafts", body, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<TranslationDraftResponse>(AiSchema.SerializerOptions, Ct))!;
    }

    private async Task<PagedResult<TextItem>> MissingAsync(string prefix, int pageSize) =>
        (await Root.GetFromJsonAsync<PagedResult<TextItem>>($"{Url}/texts/{Culture}?missing=true&prefix={prefix}&pageSize={pageSize}", Ct))!;

    private async Task AddFrenchAsync()
    {
        var cultures = (await Root.GetFromJsonAsync<List<CultureItem>>($"{Url}/cultures", Ct))!;
        if (cultures.Any(c => c.Code == Culture))
        {
            return;
        }

        var response = await Root.PostAsJsonAsync($"{Url}/cultures", new { code = Culture, name = "Français" }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
    }
}
