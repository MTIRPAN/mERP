using System.Net;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Security;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Application.Settings;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.Modules.Recruitment.IntegrationTests.CvAnalysis;
using Merp.Testing.Web;
using Merp.Web.Endpoints;

namespace Merp.Modules.Recruitment.IntegrationTests.Settings;

/// <summary>Recruitment settings (PLAN R1, D47): the seeded pipeline and lists, their changes and the KVKK texts.</summary>
public sealed class RecruitmentSettingsTests(RecruitmentFactory factory) : IClassFixture<RecruitmentFactory>
{
    private const string Url = "/api/recruitment/settings";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin(string tenant = "acme") =>
        factory.CreateClientFor("ayarlar", tenant, RecruitmentPermissions.Settings.Read, RecruitmentPermissions.Settings.Manage);

    [Fact]
    public async Task TheDefaultPipeline_AndTheLists_AreSeeded()
    {
        // PLAN R1 acceptance: a seeded stage template (the e-mail templates are the built-in texts of the external types)
        var templates = (await Admin().GetFromJsonAsync<List<StageTemplateItem>>($"{Url}/stage-templates", Ct))!;

        var standard = templates.ShouldHaveSingleItem();
        standard.IsDefault.ShouldBeTrue();
        standard.Stages[0].Type.ShouldBe(StageType.Applied);
        standard.Stages.TakeLast(3).Select(s => s.Type).ShouldBe([StageType.Hired, StageType.Rejected, StageType.Withdrawn]);
        var sources = (await Admin().GetFromJsonAsync<List<RecruitmentLookupItem>>($"{Url}/lookups?kind=CandidateSource", Ct))!;
        sources.ShouldContain(s => s.Code == "REFERRAL");
        sources.ShouldAllBe(s => s.Kind == LookupKind.CandidateSource);
    }

    [Fact]
    public async Task AStageTemplate_IsChecked_AndANewDefaultReplacesTheOldOne()
    {
        var admin = Admin("thrifty");
        object Stage(string name, string type, int? sla = null) => new { name = new Dictionary<string, string> { ["tr-TR"] = name }, type, slaDays = sla };

        var wrong = await admin.PostAsJsonAsync(
            $"{Url}/stage-templates",
            new { name = new Dictionary<string, string> { ["tr-TR"] = "Hatalı" }, stages = new[] { Stage("Başvuru", "Applied"), Stage("İşe alındı", "Hired"), Stage("Ön eleme", "Screening"), Stage("Ret", "Rejected"), Stage("Çekilme", "Withdrawn") } },
            Ct);
        wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var id = await CreatedAsync(await admin.PostAsJsonAsync(
            $"{Url}/stage-templates",
            new
            {
                name = new Dictionary<string, string> { ["tr-TR"] = "Teknik pozisyon" },
                stages = new[] { Stage("Başvuru", "Applied", 2), Stage("Teknik test", "Assessment", 5), Stage("İşe alındı", "Hired"), Stage("Ret", "Rejected"), Stage("Çekilme", "Withdrawn") },
                isDefault = true,
            },
            Ct));

        var templates = (await admin.GetFromJsonAsync<List<StageTemplateItem>>($"{Url}/stage-templates", Ct))!;
        templates.Single(t => t.IsDefault).Id.ShouldBe(id);
        templates.Count(t => t.IsDefault).ShouldBe(1);
        var created = templates.Single(t => t.Id == id);
        created.Stages.ShouldAllBe(s => s.Id != Guid.Empty);

        // A stale version is refused
        var update = new { version = created.Version + 1, name = created.Names, stages = created.Stages.Select(s => new { id = s.Id, name = s.Names, type = s.Type.ToString(), slaDays = s.SlaDays }), isDefault = true, isActive = true };
        (await admin.PutAsJsonAsync($"{Url}/stage-templates/{id}", update, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await admin.PutAsJsonAsync($"{Url}/stage-templates/{id}", update with { version = created.Version, isActive = false }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ALookup_HasAUniqueCodeInItsList_AndCanBeRetired()
    {
        var admin = Admin();
        var body = new { kind = "Tag", code = "SENIOR", name = new Dictionary<string, string> { ["tr-TR"] = "Kıdemli" }, sortOrder = 10 };

        var id = await CreatedAsync(await admin.PostAsJsonAsync($"{Url}/lookups", body, Ct));
        var duplicate = await admin.PostAsJsonAsync($"{Url}/lookups", body, Ct);
        duplicate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync($"{Url}/lookups", body with { kind = "RejectionReason" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var tag = (await admin.GetFromJsonAsync<List<RecruitmentLookupItem>>($"{Url}/lookups?kind=Tag", Ct))!.Single(t => t.Id == id);
        (await admin.PutAsJsonAsync($"{Url}/lookups/{id}", new { version = tag.Version, name = tag.Names, sortOrder = 20, isActive = false }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetFromJsonAsync<List<RecruitmentLookupItem>>($"{Url}/lookups?kind=Tag", Ct))!.ShouldNotContain(t => t.Id == id);
    }

    [Fact]
    public async Task TheCatalogs_NeedTheTenantScope_AndThePermission()
    {
        var company = Guid.CreateVersion7();
        var scoped = factory.CreateClientFor("sirket-ik", "acme", RecruitmentPermissions.Settings.Read, RecruitmentPermissions.Settings.Manage)
            .WithScopes(AccessScope.Company(company));
        var body = new { kind = "Tag", code = "X", name = new Dictionary<string, string> { ["tr-TR"] = "X" } };

        (await scoped.PostAsJsonAsync($"{Url}/lookups", body, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await scoped.GetAsync($"{Url}/lookups", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var reader = factory.CreateClientFor("okur", "acme", RecruitmentPermissions.Settings.Read);
        (await reader.PostAsJsonAsync($"{Url}/lookups", body, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await factory.CreateClientFor("yabanci", "acme", RecruitmentPermissions.Cv.Analyze).GetAsync($"{Url}/stage-templates", Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task KvkkTexts_AreVersioned_ByCompanyAndLanguage_WithTheBuiltInTextUntilOneIsPublished()
    {
        var admin = Admin();
        var company = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var current = $"{Url}/consent-texts/current?kind=Disclosure&companyId={company}&culture=tr-TR";

        var builtIn = (await admin.GetFromJsonAsync<ConsentTextItem>(current, Ct))!;
        builtIn.Revision.ShouldBe(0);
        builtIn.Body.ShouldContain("aydınlatma");
        builtIn.Body.ShouldContain("yapay zeka");
        var withoutAi = (await Admin("globex").GetFromJsonAsync<ConsentTextItem>($"{Url}/consent-texts/current?kind=Disclosure", Ct))!;
        withoutAi.Body.ShouldNotContain("yapay zeka");

        await CreatedAsync(await admin.PostAsJsonAsync($"{Url}/consent-texts", new { kind = "Disclosure", companyId = company, culture = "tr-TR", body = "Birinci sürüm" }, Ct));
        await CreatedAsync(await admin.PostAsJsonAsync($"{Url}/consent-texts", new { kind = "Disclosure", companyId = company, culture = "tr-TR", body = "İkinci sürüm" }, Ct));

        var published = (await admin.GetFromJsonAsync<ConsentTextItem>(current, Ct))!;
        published.ShouldSatisfyAllConditions(t => t.Revision.ShouldBe(2), t => t.Body.ShouldBe("İkinci sürüm"));
        (await admin.GetFromJsonAsync<List<ConsentTextItem>>($"{Url}/consent-texts?companyId={company}", Ct))!.Select(t => t.Revision).ShouldBe([2, 1]);
        (await admin.GetFromJsonAsync<ConsentTextItem>($"{Url}/consent-texts/current?kind=Disclosure&companyId={other}", Ct))!.Revision.ShouldBe(0);

        // A company's HR publishes for its company only; every company's text needs the Tenant scope
        var scoped = factory.CreateClientFor("sirket-ik", "acme", RecruitmentPermissions.Settings.Read, RecruitmentPermissions.Settings.Manage)
            .WithScopes(AccessScope.Company(company));
        (await scoped.PostAsJsonAsync($"{Url}/consent-texts", new { kind = "ExplicitConsent", companyId = company, culture = "tr-TR", body = "Rıza" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await scoped.PostAsJsonAsync($"{Url}/consent-texts", new { kind = "ExplicitConsent", companyId = other, culture = "tr-TR", body = "Rıza" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await scoped.PostAsJsonAsync($"{Url}/consent-texts", new { kind = "ExplicitConsent", culture = "tr-TR", body = "Rıza" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await admin.PostAsJsonAsync($"{Url}/consent-texts", new { kind = "Disclosure", culture = "tr", body = "x" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static async Task<Guid> CreatedAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<EntityCreatedResponse>(Ct))!.Id;
    }
}
