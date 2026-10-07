using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Documents;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.Platform.Api.Endpoints;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Localization;
using Merp.Modules.Platform.IntegrationTests.Access;
using Merp.Modules.Platform.IntegrationTests.Organization;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Platform.IntegrationTests.Localization;

/// <summary>Languages and translations of a tenant (PLAN P4, D11, D28). English is added in globex only.</summary>
public sealed class LocalizationEndpointsTests(AccessFactory factory) : IClassFixture<AccessFactory>
{
    private const string Url = "/api/platform/localization";
    private const string Tenant = "globex";

    private static readonly Dictionary<string, string> English = new()
    {
        ["web.shell.menu.home"] = "Home",
        ["web.shell.menu.organization"] = "Organization",
        ["web.shell.menu.system"] = "System",
        ["web.shell.user.profile"] = "My profile",
        ["web.shell.user.logout"] = "Sign out",
        ["web.common.save"] = "Save",
        ["web.common.cancel"] = "Cancel",
        ["web.common.edit"] = "Edit",
        ["common.errors.not_found"] = "Record not found.",
        ["common.errors.forbidden"] = "You are not allowed to do this.",
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Root => factory.AsRoot(Tenant);

    /// <summary>
    /// PLAN P4 acceptance: the administrator adds en-US and imports 10 translations from Excel; a user who picks
    /// English sees them, missing texts in Turkish, and API errors in English.
    /// </summary>
    [Fact]
    public async Task EnglishFromExcel_ReachesTheUser_MissingTextsInTurkish_ErrorsInEnglish()
    {
        await AddEnglishAsync();

        // Export, translate 10 keys in column C, import
        var export = await Root.GetAsync($"{Url}/texts/en-US/export", Ct);
        export.StatusCode.ShouldBe(HttpStatusCode.OK);
        export.Content.Headers.ContentType!.MediaType.ShouldBe(ExportedFile.XlsxContentType);
        var spreadsheet = factory.Services.GetRequiredService<ISpreadsheet>();
        var rows = spreadsheet.Read(await export.Content.ReadAsStreamAsync(Ct), 100_000)!;
        rows[0].ShouldBe(["Key", "tr-TR", "en-US"]);
        var translated = spreadsheet.Write(
            "Translations",
            ["Key", "tr-TR", "en-US"],
            rows.Skip(1).Select(r => (IReadOnlyList<string?>)[r[0], r[1], English.GetValueOrDefault(r[0]!)]));

        var import = await ImportAsync(translated);
        import.StatusCode.ShouldBe(HttpStatusCode.OK, await import.Content.ReadAsStringAsync(Ct));
        var result = (await import.Content.ReadFromJsonAsync<ImportTextsResult>(Ct))!;
        result.Imported.ShouldBe(10);
        result.UnknownKeys.ShouldBeEmpty();

        // A user who chooses English
        var subject = AccessFactory.NewSubject();
        var userId = await factory.UserIdAsync(subject, Tenant);
        await factory.AssignAsync(userId, await factory.CreateRoleAsync([PlatformPermissions.Companies.Read], Tenant), tenant: Tenant);
        var user = factory.As(subject, Tenant);
        (await user.PutAsJsonAsync("/api/platform/me/preferences", new { culture = "en-US" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var texts = (await user.GetFromJsonAsync<Dictionary<string, string>>($"{Url}/bundles/en-US", Ct))!;
        texts["web.shell.menu.home"].ShouldBe("Home");
        texts["web.common.save"].ShouldBe("Save");
        texts["web.shell.menu.companies"].ShouldBe("Şirketler");

        var me = (await user.GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!;
        me.Preferences.Culture.ShouldBe("en-US");
        me.Cultures.Select(c => c.Code).ShouldBe(["tr-TR", "en-US"]);

        var notFound = await user.GetAsync($"/api/platform/companies/{Guid.NewGuid()}", Ct);
        notFound.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        notFound.Content.Headers.ContentLanguage.ShouldContain("en-US");
        (await Problem.ReadAsync(notFound)).Detail.ShouldBe("Record not found.");

        // Accept-Language works the same without a preference; a missing text falls back to Turkish
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/platform/roles") { Content = JsonContent.Create(new { code = "x y" }) };
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("en"));
        var invalid = await Root.SendAsync(request, Ct);
        invalid.Content.Headers.ContentLanguage.ShouldContain("en-US");
        (await Problem.ReadAsync(invalid)).Errors!["code"].ShouldAllBe(m => m.Length > 0);
    }

    [Fact]
    public async Task Texts_ListTheMissingOnes_AndASingleTranslationAppliesAtOnce()
    {
        await AddEnglishAsync();

        var missing = (await Root.GetFromJsonAsync<PagedResult<TextItem>>($"{Url}/texts/en-US?missing=true&prefix=web.audit.&pageSize=200", Ct))!;
        missing.Items.ShouldNotBeEmpty();
        missing.Items.ShouldAllBe(t => t.Text == null && t.Source == null && t.DefaultText != null);

        var key = missing.Items[0].Key;
        (await Root.PutAsJsonAsync($"{Url}/texts/en-US", new { key, value = "Translated" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var found = (await Root.GetFromJsonAsync<PagedResult<TextItem>>($"{Url}/texts/en-US?search=translated", Ct))!;
        found.Items.ShouldContain(new TextItem(key, missing.Items[0].DefaultText, "Translated", TextSources.Database));
        (await Root.GetFromJsonAsync<Dictionary<string, string>>($"{Url}/bundles/en-US", Ct))![key].ShouldBe("Translated");

        // Removing the translation brings the fallback back
        (await Root.PutAsJsonAsync($"{Url}/texts/en-US", new { key, value = "" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Root.GetFromJsonAsync<Dictionary<string, string>>($"{Url}/bundles/en-US", Ct))![key].ShouldBe(missing.Items[0].DefaultText);

        var cultures = (await Root.GetFromJsonAsync<List<CultureItem>>($"{Url}/cultures", Ct))!;
        var turkish = cultures.Single(c => c.Code == "tr-TR");
        (turkish.IsDefault, turkish.TranslatedCount).ShouldBe((true, turkish.TotalCount));
        cultures.Single(c => c.Code == "en-US").TranslatedCount.ShouldBeLessThan(turkish.TotalCount);
    }

    [Fact]
    public async Task InvalidInput_Returns400()
    {
        await AddEnglishAsync();

        (await Problem.ReadAsync(await Root.PostAsJsonAsync($"{Url}/cultures", new { code = "english" }, Ct))).Errors!.ShouldContainKey("code");
        (await Problem.ReadAsync(await Root.PostAsJsonAsync($"{Url}/cultures", new { code = "en" }, Ct))).Errors!.ShouldContainKey("code");
        (await Problem.ReadAsync(await Root.PostAsJsonAsync($"{Url}/cultures", new { code = "EN-us" }, Ct))).Errors!["code"].ShouldNotBeEmpty();
        (await Problem.ReadAsync(await Root.PostAsJsonAsync($"{Url}/cultures", new { code = "de-DE", fallbackCode = "fr-FR" }, Ct))).Errors!.ShouldContainKey("fallbackCode");
        (await Problem.ReadAsync(await Root.PutAsJsonAsync($"{Url}/texts/en-US", new { key = "no.such.key", value = "x" }, Ct))).Errors!.ShouldContainKey("key");

        using var form = new MultipartFormDataContent { { new ByteArrayContent("not excel"u8.ToArray()), "file", "x.xlsx" } };
        var notExcel = await Root.PostAsync($"{Url}/texts/en-US/import", form, Ct);
        notExcel.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(notExcel)).Errors!.ShouldContainKey("file");

        var spreadsheet = factory.Services.GetRequiredService<ISpreadsheet>();
        var wrongCulture = await ImportAsync(spreadsheet.Write("Translations", ["Key", "tr-TR", "de-DE"], [["web.common.save", "Kaydet", "Speichern"]]));
        (await Problem.ReadAsync(wrongCulture)).Errors!["file"].ShouldBe(["Dosya başka bir dil için. C sütununun başlığı en-US olmalı."]);

        // Master data texts need the default culture and active cultures only
        var role = await Root.PostAsJsonAsync(
            "/api/platform/roles",
            new { code = "R" + Guid.NewGuid().ToString("N")[..8], name = new Dictionary<string, string> { ["en-US"] = "Payroll" }, permissions = Array.Empty<string>() },
            Ct);
        (await Problem.ReadAsync(role)).Errors!["name"].ShouldBe(["Varsayılan dilde (tr-TR) bir metin girilmelidir."]);
        var inactive = await Root.PostAsJsonAsync(
            "/api/platform/roles",
            new { code = "R" + Guid.NewGuid().ToString("N")[..8], name = new Dictionary<string, string> { ["tr-TR"] = "Bordro", ["fr-FR"] = "Paie" }, permissions = Array.Empty<string>() },
            Ct);
        (await Problem.ReadAsync(inactive)).Errors!["name"].ShouldBe(["fr-FR dili etkin değil."]);
    }

    [Fact]
    public async Task DefaultCulture_StaysActive_AndCannotBeDeleted_OtherCulturesCan()
    {
        var turkish = (await Root.GetFromJsonAsync<List<CultureItem>>($"{Url}/cultures", Ct))!.Single(c => c.IsDefault);
        var off = await Root.PutAsJsonAsync($"{Url}/cultures/tr-TR", new { version = turkish.Version, name = "Türkçe", isActive = false }, Ct);
        off.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Root.DeleteAsync($"{Url}/cultures/tr-TR", Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        (await Root.PostAsJsonAsync($"{Url}/cultures", new { code = "de-DE", name = "Deutsch", fallbackCode = "tr-TR" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var german = (await Root.GetFromJsonAsync<List<CultureItem>>($"{Url}/cultures", Ct))!.Single(c => c.Code == "de-DE");
        (await Root.PutAsJsonAsync($"{Url}/cultures/de-DE", new { version = german.Version, name = "Deutsch", isActive = false }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Root.GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!.Cultures.Select(c => c.Code).ShouldNotContain("de-DE");
        (await Root.GetAsync($"{Url}/bundles/de-DE", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Root.DeleteAsync($"{Url}/cultures/de-DE", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Root.DeleteAsync($"{Url}/cultures/de-DE", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Localization_NeedsThePermission_WithTheTenantScope()
    {
        var (reader, _) = await factory.UserWithAsync([PlatformPermissions.Companies.Read]);
        (await reader.GetAsync($"{Url}/cultures", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.PostAsJsonAsync($"{Url}/cultures", new { code = "en-US" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var companyId = await OrganizationFactory.CreateCompanyAsync(factory.AsRoot());
        var (companyScoped, _) = await factory.UserWithAsync([PlatformPermissions.Localization.Read], "Company", companyId);
        var response = await companyScoped.GetAsync($"{Url}/texts/tr-TR", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Problem.ReadAsync(response)).Code.ShouldBe("platform.localization.errors.tenant_scope_required");
    }

    private async Task AddEnglishAsync()
    {
        var cultures = (await Root.GetFromJsonAsync<List<CultureItem>>($"{Url}/cultures", Ct))!;
        if (cultures.Any(c => c.Code == "en-US"))
        {
            return;
        }

        var response = await Root.PostAsJsonAsync($"{Url}/cultures", new { code = "en-us" }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var created = (await response.Content.ReadFromJsonAsync<CultureCreatedResponse>(Ct))!;
        created.Code.ShouldBe("en-US");
    }

    private Task<HttpResponseMessage> ImportAsync(byte[] file)
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent(file), "file", "translations-en-US.xlsx" } };
        return Root.PostAsync($"{Url}/texts/en-US/import", form, Ct);
    }
}
