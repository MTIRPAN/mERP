using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Infrastructure.MultiTenancy;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Application.Applications;
using Merp.Modules.Recruitment.Application.CvAnalysis;
using Merp.Modules.Recruitment.Domain.Candidates;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.Modules.Recruitment.Infrastructure.Persistence;
using Merp.Modules.Recruitment.IntegrationTests.Requisitions;
using Merp.Testing.Data;
using Merp.Testing.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using PageSize = UglyToad.PdfPig.Content.PageSize;

namespace Merp.Modules.Recruitment.IntegrationTests.CvAnalysis;

/// <summary>
/// The CV analysis of applications (PLAN A3, D51): new applications are analyzed in the background against their
/// opening, the team runs it again from the card, and the candidate form is filled from a CV; contact details and the
/// name never reach the provider.
/// </summary>
public sealed class ApplicationCvAnalysisTests
    : IClassFixture<RequisitionFactory>
{
    private static readonly string[] CvLines =
    [
        "Ayse Yilmaz",
        "ayse.yilmaz@example.com - 0532 123 45 67 - linkedin.com/in/ayseyilmaz",
        "Sales Specialist at Kuzey Lojistik, 2019 - present",
        "Grew B2B sales in the Marmara region for five years.",
    ];

    private static readonly string[] Personal = ["Ayse", "Yilmaz", "example.com", "0532", "linkedin"];

    private readonly RequisitionFactory _factory;

    public ApplicationCvAnalysisTests(RequisitionFactory factory)
    {
        _factory = factory;
        _factory.Ai.Reset();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NewApplications_AreAnalyzedInTheBackground_AgainstTheirOpening()
    {
        // PLAN A3 acceptance: the analysis is kept on the application and shown on its card
        var (company, _) = await SetupAsync();
        var opening = await OpeningAsync(company);
        var application = await ApplyAsync(opening, withCv: true);
        var old = await ApplyAsync(opening, withCv: true);
        await WithDbAsync(db => db.JobApplications.Where(a => a.Id == Domain.Applications.JobApplicationId.From(old))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.AppliedAt, DateTimeOffset.UtcNow.AddDays(-40)), Ct));

        // Other tests of the class may have left applications with a CV too: one arranged response per run
        for (var run = 0; run < 20; run++)
        {
            _factory.Ai.Returns(Sample());
            if (!await RunNextAsync())
            {
                break;
            }
        }

        var details = await DetailsAsync(Recruiter(), application);
        details.CvAnalysis.ShouldNotBeNull().ShouldSatisfyAllConditions(
            a => a.Succeeded.ShouldBeTrue(),
            a => a.Response!.Analysis.Summary.ShouldBe("Deneyimli B2B satış uzmanı."),
            a => a.Response!.PromptVersion.ShouldBe(CvAnalysisPrompt.Version),
            a => a.RequestedByName.ShouldBeNull());
        details.CanAnalyzeCv.ShouldBeTrue();
        (await DetailsAsync(Recruiter(), old)).CvAnalysis.ShouldBeNull();

        var sent = _factory.Ai.Requests.Where(r => r.Input.Contains("Grew B2B sales", StringComparison.Ordinal)).ToList();
        sent.ShouldNotBeEmpty();
        sent.ShouldAllBe(r => r.Input.Contains("<title>Satış Uzmanı</title>") && r.Input.Contains("- 5 yıl B2B satış"));
        foreach (var personal in Personal)
        {
            sent.ShouldAllBe(r => !r.Input.Contains(personal, StringComparison.Ordinal));
        }

        (await RunNextAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task TheTeam_RunsTheAnalysisAgain_AndAFailureKeepsThePreviousOne()
    {
        var (company, managerSubject) = await SetupAsync();
        var (otherCompany, _) = await SetupAsync();
        var opening = await OpeningAsync(company);
        var application = await ApplyAsync(opening, withCv: true);
        var withoutCv = await ApplyAsync(opening, withCv: false);
        var analyst = Recruiter();

        _factory.Ai.Returns(Sample());
        var run = await analyst.PostAsync($"/api/recruitment/applications/{application}/cv-analysis", null, Ct);
        run.StatusCode.ShouldBe(HttpStatusCode.OK, await run.Content.ReadAsStringAsync(Ct));
        (await run.Content.ReadFromJsonAsync<ApplicationCvAnalysisItem>(Ct))!.Response!.Analysis.CurrentTitle.ShouldBe("Sales Specialist");

        _factory.Ai.Fails();
        (await analyst.PostAsync($"/api/recruitment/applications/{application}/cv-analysis", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var details = await DetailsAsync(analyst, application);
        details.CvAnalysis!.ShouldSatisfyAllConditions(a => a.Succeeded.ShouldBeTrue(), a => a.RequestedByName.ShouldNotBeNull());

        // The hiring team sees it but does not run it; others need the permission and the company in scope
        var team = _factory.CreateClientFor(managerSubject, RequisitionFactory.Tenant, RecruitmentPermissions.Self.Hiring);
        var teamDetails = (await team.GetFromJsonAsync<ApplicationDetails>($"/api/recruitment/me/hiring/applications/{application}", Ct))!;
        teamDetails.ShouldSatisfyAllConditions(d => d.CvAnalysis!.Succeeded.ShouldBeTrue(), d => d.CanAnalyzeCv.ShouldBeFalse());
        (await _factory.CreateClientFor("reader", RequisitionFactory.Tenant, RecruitmentPermissions.Applications.Read)
            .PostAsync($"/api/recruitment/applications/{application}/cv-analysis", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var scoped = _factory.CreateClientFor("scoped-analyst", RequisitionFactory.Tenant, RecruitmentPermissions.Cv.Analyze, RecruitmentPermissions.Applications.Read)
            .WithScopes(AccessScope.Company(otherCompany));
        (await scoped.PostAsync($"/api/recruitment/applications/{application}/cv-analysis", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var noCv = await analyst.PostAsync($"/api/recruitment/applications/{withoutCv}/cv-analysis", null, Ct);
        (await FieldCodesAsync(noCv)).ShouldContain(("cv", "recruitment.applications.errors.no_cv"));
        (await DetailsAsync(analyst, withoutCv)).CanAnalyzeCv.ShouldBeFalse();
    }

    [Fact]
    public async Task TheCandidateForm_IsFilledFromACv_WithoutSendingTheContactDetails()
    {
        var recruiter = _factory.CreateClientFor("pool-recruiter", RequisitionFactory.Tenant, RecruitmentPermissions.Candidates.Manage);
        _factory.Ai.Returns(Sample());

        var response = await recruiter.PostAsync("/api/recruitment/candidates/cv-prefill", Form(Pdf(CvLines), "cv.pdf"), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var prefill = (await response.Content.ReadFromJsonAsync<CandidateCvPrefill>(Ct))!;
        prefill.ShouldSatisfyAllConditions(
            p => p.FirstName.ShouldBe("Ayse"),
            p => p.LastName.ShouldBe("Yilmaz"),
            p => p.Email.ShouldBe("ayse.yilmaz@example.com"),
            p => p.Phone.ShouldBe("0532 123 45 67"),
            p => p.LinkedInUrl.ShouldBe("https://linkedin.com/in/ayseyilmaz"),
            p => p.CurrentTitle.ShouldBe("Sales Specialist"),
            p => p.CurrentCompany.ShouldBe("Kuzey Lojistik"),
            p => p.ExperienceYears.ShouldBe(5.5m),
            p => p.Education.ShouldBe("BSc, Business, Ankara University"),
            p => p.AiUsed.ShouldBeTrue(),
            p => p.AiErrorCode.ShouldBeNull());
        var sent = _factory.Ai.Requests.ShouldHaveSingleItem();
        foreach (var personal in Personal)
        {
            sent.Input.ShouldNotContain(personal);
        }

        // Without the AI the contact details still come; the user sees why the rest is missing
        _factory.Ai.Fails();
        var local = (await (await recruiter.PostAsync("/api/recruitment/candidates/cv-prefill", Form(Pdf(CvLines), "cv.pdf"), Ct))
            .Content.ReadFromJsonAsync<CandidateCvPrefill>(Ct))!;
        local.ShouldSatisfyAllConditions(
            p => p.Email.ShouldBe("ayse.yilmaz@example.com"),
            p => p.CurrentTitle.ShouldBeNull(),
            p => p.AiUsed.ShouldBeFalse(),
            p => p.AiErrorCode.ShouldBe("common.errors.ai_unavailable"));

        (await FieldCodesAsync(await recruiter.PostAsync("/api/recruitment/candidates/cv-prefill", Form("just text"u8.ToArray(), "cv.pdf"), Ct)))
            .ShouldContain(("cv", "common.errors.document_unsupported_format"));
        (await _factory.CreateClientFor("reader", RequisitionFactory.Tenant, RecruitmentPermissions.Candidates.Read)
            .PostAsync("/api/recruitment/candidates/cv-prefill", Form(Pdf(CvLines), "cv.pdf"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private HttpClient Recruiter() =>
        _factory.CreateClientFor(
            "cv-analyst",
            RequisitionFactory.Tenant,
            RecruitmentPermissions.Cv.Analyze,
            RecruitmentPermissions.Applications.Read,
            RecruitmentPermissions.Applications.Manage,
            "platform.companies.read",
            "platform.companies.manage");

    private static CvAnalysisResult Sample() => new(
        "Deneyimli B2B satış uzmanı.",
        "Sales Specialist",
        5.5m,
        CvSeniority.MidLevel,
        [new CvExperience("Sales Specialist", "Kuzey Lojistik", "2019", string.Empty, true, ["Marmara bölgesinde B2B satış"])],
        [new CvEducation("Ankara University", "BSc", "Business", CvEducationLevel.Bachelor, 2015, false)],
        [new CvSkill("B2B satış", CvSkillCategory.Domain, CvSkillLevel.Advanced, "Beş yıl B2B satış")],
        [],
        [],
        new CvMatch(80, CvRecommendation.Match, "Uygun.", [new CvRequirementAssessment("5 yıl B2B satış", CvRequirementStatus.Met, "Beş yıl")], ["B2B"], []),
        [new CvInterviewQuestion("En büyük satışınız?", "Deneyim")],
        []);

    /// <summary>A company and a user who hires for it.</summary>
    private async Task<(Guid Company, string ManagerSubject)> SetupAsync()
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
        var subject = $"manager-{Guid.NewGuid():N}";
        using var me = JsonDocument.Parse(await _factory.CreateClientFor(subject, RequisitionFactory.Tenant).GetStringAsync("/api/platform/me", Ct));
        _managers[company] = me.RootElement.GetProperty("user").GetProperty("id").GetGuid();
        return (company, subject);
    }

    private readonly Dictionary<Guid, Guid> _managers = [];

    /// <summary>A published public opening with requirements, asking only name and e-mail.</summary>
    private Task<Guid> OpeningAsync(Guid company) =>
        WithDbAsync(async db =>
        {
            var template = await db.StageTemplates.SingleAsync(t => t.IsDefault, Ct);
            var opening = JobOpening.Draft(
                new OpeningTerms(
                    company,
                    null,
                    "Satış Uzmanı",
                    "<p>Satış ekibimize katılacak arkadaş arıyoruz.</p>",
                    "<ul><li>5 yıl B2B satış</li><li>İyi derecede İngilizce</li></ul>",
                    null,
                    WorkModel.OnSite,
                    RequisitionEmploymentType.Indefinite,
                    1,
                    OpeningVisibility.Public,
                    null,
                    null,
                    new OpeningTeam(_managers[company], [], []),
                    new ApplicationForm(RequirePhone: false, RequireCv: false, AskCoverLetter: false, [])),
                template);
            opening.Publish(DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime.AddDays(-1)), DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();
            db.JobOpenings.Add(opening);
            await db.SaveChangesAsync(Ct);
            return opening.Id.Value;
        });

    /// <summary>Applies from the career page as a new candidate; returns the application.</summary>
    private async Task<Guid> ApplyAsync(Guid opening, bool withCv)
    {
        var email = $"aday.{Guid.NewGuid().ToString("N")[..12]}@ornek.test";
        var body = new { firstName = "Ayşe", lastName = "Yılmaz", email, disclosureAccepted = true, disclosureRevision = 0, poolConsent = false, culture = "tr-TR" };
        using var form = new MultipartFormDataContent { { new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), Encoding.UTF8), "application" } };
        if (withCv)
        {
            var cv = new ByteArrayContent(Pdf(CvLines));
            cv.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(cv, "cv", "ozgecmis.pdf");
        }

        var response = await _factory.CreateClient().PostAsync($"/api/public/{RequisitionFactory.Tenant}/careers/openings/{opening}/applications", form, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        var normalized = Candidate.NormalizeEmail(email);
        return await WithDbAsync(db => db.JobApplications
            .Where(a => a.OpeningId == JobOpeningId.From(opening))
            .Join(db.Candidates.Where(c => c.Email == normalized), a => a.CandidateId, c => c.Id, (a, _) => a.Id.Value)
            .SingleAsync(Ct));
    }

    private async Task<bool> RunNextAsync()
    {
        var tenant = (await _factory.Services.GetRequiredService<ITenantStore>().FindByCodeAsync(RequisitionFactory.Tenant, Ct))!;
        await using var scope = _factory.Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        var result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new AnalyzeNextApplicationCvQuery(), Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }

    private static async Task<ApplicationDetails> DetailsAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<ApplicationDetails>($"/api/recruitment/applications/{id}", Ct))!;

    private static MultipartFormDataContent Form(byte[] file, string fileName)
    {
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { content, "cv", fileName } };
    }

    private static byte[] Pdf(params string[] lines)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (var i = 0; i < lines.Length; i++)
        {
            page.AddText(lines[i], 11, new PdfPoint(40, 780 - (i * 18)), font);
        }

        return builder.Build();
    }

    private async Task<T> WithDbAsync<T>(Func<RecruitmentDbContext, Task<T>> work)
    {
        var tenant = (await _factory.Services.GetRequiredService<ITenantStore>().FindByCodeAsync(RequisitionFactory.Tenant, Ct))!;
        await using var scope = _factory.Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        return await work(scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>());
    }

    private static async Task<List<(string Field, string Code)>> FieldCodesAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync(Ct));
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return [.. problem.GetProperty("fieldErrors").EnumerateArray().Select(e => (e.GetProperty("field").GetString()!, e.GetProperty("code").GetString()!))];
    }
}
