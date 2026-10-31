using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Merp.Application.Abstractions.Security;
using Merp.Infrastructure.AI;
using Merp.Infrastructure.MultiTenancy;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Application.Openings;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.Modules.Recruitment.Infrastructure.Persistence;
using Merp.Modules.Recruitment.IntegrationTests.Requisitions;
using Merp.Testing.Data;
using Merp.Testing.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Recruitment.IntegrationTests.Openings;

/// <summary>
/// AI drafts of opening texts and the inclusive language check (PLAN A4, D57): the draft fills the form without
/// contact details or the requisition's justification reaching the provider, an opening saved with it is published
/// only after a user confirms reviewing it, and exclusionary phrases are warned about.
/// </summary>
public sealed class OpeningTextTests : IClassFixture<RequisitionFactory>
{
    private const string Url = "/api/recruitment/openings";

    private static readonly string[] Permissions =
    [
        RecruitmentPermissions.Openings.Read,
        RecruitmentPermissions.Openings.Manage,
        "platform.companies.read",
        "platform.companies.manage",
    ];

    private readonly RequisitionFactory _factory;

    public OpeningTextTests(RequisitionFactory factory)
    {
        _factory = factory;
        _factory.Ai.Reset();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ADraft_FillsTheTexts_WithoutPersonalData_AndWarnsAboutTheBrief()
    {
        var (company, manager) = await SetupAsync();
        var requisition = await RequisitionAsync(company, manager);
        _factory.Ai.Returns(new OpeningDraftResult(
            "Kurumsal müşterilerimizle uzun soluklu ilişkiler kuracak bir satış uzmanı arıyoruz.",
            ["Yeni müşteri fırsatlarını belirlemek", "Teklif ve sözleşme süreçlerini yürütmek"],
            ["En az 3 yıl B2B satış deneyimi", "Güçlü iletişim becerileri"],
            ["CRM araçlarıyla deneyim"],
            [new OpeningTextIssue(OpeningTextField.Brief, "25-35 yaş arası bayan", LanguageWarningCategory.Age, "Yaş ve cinsiyet yerine işin gerektirdiği deneyimi yazın.")]));

        var response = await Recruiter().PostAsJsonAsync(
            $"{Url}/text-draft",
            new
            {
                companyId = company,
                requisitionId = requisition,
                title = "Satış Uzmanı",
                brief = "Kurumsal satış. 25-35 yaş arası bayan. Sorular için ik@acme.example veya 0532 123 45 67.",
                workModel = "Hybrid",
                employmentType = "Indefinite",
            },
            Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var draft = (await response.Content.ReadFromJsonAsync<OpeningDraftResponse>(AiSchema.SerializerOptions, Ct))!;
        draft.ShouldSatisfyAllConditions(
            d => d.Description.ShouldBe("<p>Kurumsal müşterilerimizle uzun soluklu ilişkiler kuracak bir satış uzmanı arıyoruz.</p><h3>Sorumluluklar</h3><ul><li>Yeni müşteri fırsatlarını belirlemek</li><li>Teklif ve sözleşme süreçlerini yürütmek</li></ul>"),
            d => d.Qualifications.ShouldBe("<ul><li>En az 3 yıl B2B satış deneyimi</li><li>Güçlü iletişim becerileri</li></ul><h3>Tercih sebepleri</h3><ul><li>CRM araçlarıyla deneyim</li></ul>"),
            d => d.Warnings.ShouldHaveSingleItem().ShouldBe(new OpeningLanguageWarning(OpeningTextField.Brief, "25-35 yaş arası bayan", LanguageWarningCategory.Age, "Yaş ve cinsiyet yerine işin gerektirdiği deneyimi yazın.", LanguageWarningSource.Ai)),
            d => d.PromptVersion.ShouldBe(OpeningTextPrompt.Version));

        var sent = _factory.Ai.Requests.ShouldHaveSingleItem();
        sent.Feature.ShouldBe(RecruitmentAiFeatures.OpeningText);
        sent.Instructions.ShouldBe(OpeningTextPrompt.DraftInstructions);
        sent.Input.ShouldSatisfyAllConditions(
            i => i.ShouldContain("<title>Satış Uzmanı</title>"),
            i => i.ShouldContain("<position>Bölge Satış Uzmanı</position>"),
            i => i.ShouldContain("Kurumsal satış."),
            i => i.ShouldNotContain("ik@acme.example"),
            i => i.ShouldNotContain("0532"),
            i => i.ShouldNotContain("Ayşe Yılmaz"));
    }

    [Fact]
    public async Task AnOpeningSavedWithADraft_IsPublishedOnlyAfterTheReview_AndShowsTheRuleWarnings()
    {
        // PLAN A4 acceptance: the draft cannot be published without the user
        var (company, manager) = await SetupAsync();
        var recruiter = Recruiter();
        var id = await RequisitionFactory.CreatedIdAsync(await recruiter.PostAsJsonAsync(Url, Body(company, manager, version: 0, reviewed: false), Ct));

        var pending = await DetailsAsync(recruiter, id);
        pending.ShouldSatisfyAllConditions(
            d => d.AiDraftPending.ShouldBeTrue(),
            d => d.LanguageWarnings.Select(w => (w.Field, w.Phrase, w.Category)).ShouldBe([(OpeningTextField.Description, "Genç ve dinamik", LanguageWarningCategory.Age)]));
        var refused = await recruiter.PostAsync($"{Url}/{id}/publish", null, Ct);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(refused)).ShouldBe("recruitment.openings.errors.ai_draft_not_reviewed");

        (await recruiter.PutAsJsonAsync($"{Url}/{id}", Body(company, manager, pending.Version, reviewed: true) with { AiDrafted = false }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await DetailsAsync(recruiter, id)).AiDraftPending.ShouldBeFalse();
        (await recruiter.PostAsync($"{Url}/{id}/publish", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        _factory.Ai.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheLanguageCheck_RunsTheRules_AndTheAiReviewOnRedactedText()
    {
        var description = "<p>Genç ve dinamik bir ekip.</p><p>Başvurular: ik@acme.example</p>";
        var rules = await CheckAsync(new { title = "Satış Uzmanı", description, withAi = false });
        rules.ShouldSatisfyAllConditions(
            c => c.CheckedWithAi.ShouldBeFalse(),
            c => c.Warnings.ShouldHaveSingleItem().Source.ShouldBe(LanguageWarningSource.Rule));
        _factory.Ai.Requests.ShouldBeEmpty();

        _factory.Ai.Returns(new OpeningLanguageReview(
        [
            new OpeningTextIssue(OpeningTextField.Description, "Genç ve dinamik", LanguageWarningCategory.Age, "Enerjik"),
            new OpeningTextIssue(OpeningTextField.Description, "ekip", LanguageWarningCategory.Other, "Takım"),
        ]));
        var reviewed = await CheckAsync(new { title = "Satış Uzmanı", description, withAi = true });

        reviewed.CheckedWithAi.ShouldBeTrue();
        reviewed.Warnings.Select(w => (w.Phrase, w.Source)).ShouldBe([("Genç ve dinamik", LanguageWarningSource.Rule), ("ekip", LanguageWarningSource.Ai)]);
        var sent = _factory.Ai.Requests.ShouldHaveSingleItem();
        sent.Instructions.ShouldBe(OpeningTextPrompt.ReviewInstructions);
        sent.Input.ShouldContain("Genç ve dinamik bir ekip.");
        sent.Input.ShouldNotContain("ik@acme.example");
        sent.Input.ShouldNotContain("<p>");
    }

    [Fact]
    public async Task WithoutAi_TheDraftIs403_AndTheRulesStillRun()
    {
        _factory.Ai.IsConfigured = false;
        var draft = await Recruiter().PostAsJsonAsync($"{Url}/text-draft", new { companyId = Guid.CreateVersion7(), title = "Satış Uzmanı", workModel = "OnSite", employmentType = "Indefinite" }, Ct);
        draft.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(draft)).ShouldBe("common.errors.ai_disabled");
        (await Recruiter().PostAsJsonAsync($"{Url}/language-check", new { title = "Bayan satış uzmanı", withAi = true }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CheckAsync(new { title = "Bayan satış uzmanı", withAi = false })).Warnings.ShouldHaveSingleItem().Category.ShouldBe(LanguageWarningCategory.Gender);
        _factory.Ai.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task InvalidOrUnauthorizedRequests_AreRefused()
    {
        var reader = _factory.CreateClientFor("opening-text-reader", RequisitionFactory.Tenant, RecruitmentPermissions.Openings.Read);
        (await reader.PostAsJsonAsync($"{Url}/text-draft", new { companyId = Guid.CreateVersion7(), title = "Satış Uzmanı", workModel = "OnSite", employmentType = "Indefinite" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.PostAsJsonAsync($"{Url}/language-check", new { title = "Satış Uzmanı", withAi = false }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var tooLong = await Recruiter().PostAsJsonAsync($"{Url}/text-draft", new { companyId = Guid.CreateVersion7(), title = "Satış Uzmanı", brief = new string('a', DraftOpeningTextQuery.BriefMaxLength + 1), workModel = "OnSite", employmentType = "Indefinite" }, Ct);
        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Another company's recruiter cannot draft for this one
        var scoped = _factory.CreateClientFor("opening-text-scoped", RequisitionFactory.Tenant, Permissions)
            .WithScopes(AccessScope.Company(Guid.CreateVersion7()));
        (await scoped.PostAsJsonAsync($"{Url}/text-draft", new { companyId = Guid.CreateVersion7(), title = "Satış Uzmanı", workModel = "OnSite", employmentType = "Indefinite" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        _factory.Ai.Requests.ShouldBeEmpty();
    }

    private sealed record OpeningBody(
        uint Version,
        Guid CompanyId,
        string Title,
        string Description,
        WorkModel WorkModel,
        RequisitionEmploymentType EmploymentType,
        int Headcount,
        OpeningVisibility Visibility,
        Guid HiringManagerUserId,
        IReadOnlyList<Guid> InterviewerUserIds,
        bool RequirePhone,
        bool RequireCv,
        bool AiDrafted,
        bool AiDraftReviewed);

    private static OpeningBody Body(Guid company, Guid manager, uint version, bool reviewed) => new(
        version,
        company,
        "Satış Uzmanı",
        "<p>Genç ve dinamik ekibimize katılacak satış uzmanı arıyoruz.</p>",
        WorkModel.Hybrid,
        RequisitionEmploymentType.Indefinite,
        1,
        OpeningVisibility.Public,
        manager,
        [manager],
        true,
        true,
        AiDrafted: true,
        AiDraftReviewed: reviewed);

    private HttpClient Recruiter() => _factory.CreateClientFor("opening-text-recruiter", RequisitionFactory.Tenant, Permissions);

    private async Task<OpeningLanguageCheck> CheckAsync(object body)
    {
        var response = await Recruiter().PostAsJsonAsync($"{Url}/language-check", body, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<OpeningLanguageCheck>(Ct))!;
    }

    private async Task<(Guid Company, Guid Manager)> SetupAsync()
    {
        var company = await RequisitionFactory.CreatedIdAsync(await Recruiter().PostAsJsonAsync(
            "/api/platform/companies",
            new
            {
                code = "C" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                title = "Acme Holding Anonim Şirketi",
                shortName = "Acme",
                taxOffice = "Kadıköy",
                taxNumber = TurkishData.Vkn(),
                address = new { line1 = "Atatürk Cad. No: 1", provinceCode = 34, districtCode = 1421, postalCode = "34710" },
            },
            Ct));
        using var me = JsonDocument.Parse(await _factory.CreateClientFor("opening-text-manager", RequisitionFactory.Tenant).GetStringAsync("/api/platform/me", Ct));
        return (company, me.RootElement.GetProperty("user").GetProperty("id").GetGuid());
    }

    /// <summary>An approved requisition whose justification names a person: it must never reach the provider.</summary>
    private async Task<Guid> RequisitionAsync(Guid company, Guid manager)
    {
        var requisition = JobRequisition.Draft(
            "PT-" + Guid.NewGuid().ToString("N")[..8],
            manager,
            new RequisitionTerms(company, Guid.CreateVersion7(), null, "Bölge Satış Uzmanı", null, 1, RequisitionReason.NewHeadcount, null, RequisitionEmploymentType.Indefinite, null, null, null, null, "Ayşe Yılmaz ayrıldığı için", manager));
        requisition.Submit(DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();
        requisition.Complete(approved: true, DateTimeOffset.UtcNow);
        var tenant = (await _factory.Services.GetRequiredService<ITenantStore>().FindByCodeAsync(RequisitionFactory.Tenant, Ct))!;
        await using var scope = _factory.Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        db.JobRequisitions.Add(requisition);
        await db.SaveChangesAsync(Ct);
        return requisition.Id.Value;
    }

    private static async Task<OpeningDetails> DetailsAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<OpeningDetails>($"{Url}/{id}", Ct))!;

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return json.RootElement.GetProperty("code").GetString();
    }
}
