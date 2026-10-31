using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Infrastructure.MultiTenancy;
using Merp.Modules.Platform.Contracts.Notifications;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Application.Applications;
using Merp.Modules.Recruitment.Application.CvAnalysis;
using Merp.Modules.Recruitment.Application.Openings;
using Merp.Modules.Recruitment.Domain.Applications;
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

namespace Merp.Modules.Recruitment.IntegrationTests.Openings;

/// <summary>
/// The batch screening of an opening (PLAN A6, D59): HR asks for it, the night run sends the applications without a
/// current analysis in a batch within the night's limit, the results are stored as the applications' analyses and
/// shown as evidence by requirement; there is no score, no ranking and no rejection.
/// </summary>
public sealed class OpeningScreeningTests : IClassFixture<RequisitionFactory>
{
    private const string Url = "/api/recruitment/openings";

    private static readonly string[] CvLines =
    [
        "Ayse Yilmaz",
        "ayse.yilmaz@example.com - 0532 123 45 67",
        "Sales Specialist at Kuzey Lojistik, 2019 - present",
        "Grew B2B sales in the Marmara region for five years.",
    ];

    private static readonly string[] Permissions =
    [
        RecruitmentPermissions.Cv.Analyze,
        RecruitmentPermissions.Applications.Read,
        "platform.companies.read",
        "platform.companies.manage",
    ];

    private readonly RequisitionFactory _factory;
    private readonly Dictionary<Guid, Guid> _managers = [];

    public OpeningScreeningTests(RequisitionFactory factory)
    {
        _factory = factory;
        _factory.Ai.Reset();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AScreening_SendsTheDueApplicationsAtNight_AndShowsTheEvidenceByRequirement()
    {
        var company = await CompanyAsync();
        var opening = await OpeningAsync(company);
        var first = await ApplyAsync(opening, withCv: true);
        var second = await ApplyAsync(opening, withCv: true);
        var withoutCv = await ApplyAsync(opening, withCv: false);
        var analyzed = await ApplyAsync(opening, withCv: true);
        await AnalyzedAsync(analyzed);

        var before = await ViewAsync(opening);
        before.ShouldSatisfyAllConditions(
            v => v.Due.ShouldBe(2),
            v => v.CanRequest.ShouldBeTrue(),
            v => v.Screening.ShouldBeNull(),
            v => v.Rows.Select(r => r.ApplicationId).ShouldBe([first, second, withoutCv, analyzed]),
            v => v.Rows.Select(r => r.State).ShouldBe([ScreeningRowState.NotAnalyzed, ScreeningRowState.NotAnalyzed, ScreeningRowState.NoCv, ScreeningRowState.Current]),
            v => v.Requirements.ShouldBe(["5 yıl B2B satış"]));

        var requested = await Recruiter().PostAsync($"{Url}/{opening}/screenings", null, Ct);
        requested.StatusCode.ShouldBe(HttpStatusCode.Created, await requested.Content.ReadAsStringAsync(Ct));
        var again = await Recruiter().PostAsync($"{Url}/{opening}/screenings", null, Ct);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(again)).ShouldBe("recruitment.screenings.errors.already_open");

        // The night run sends the two applications without an analysis, without names or contact details
        await SubmitAllAsync(500);
        var (batchId, items) = _factory.Ai.Batches.ShouldHaveSingleItem();
        items.Select(i => i.CustomId).ShouldBe([Key(first), Key(second)]);
        items.ShouldAllBe(i => i.Request.Feature == RecruitmentAiFeatures.BatchScreening && i.Request.Instructions == CvAnalysisPrompt.Instructions);
        items[0].Request.Input.ShouldSatisfyAllConditions(
            i => i.ShouldContain("5 yıl B2B satış"),
            i => i.ShouldContain("Grew B2B sales"),
            i => i.ShouldNotContain("Ayse"),
            i => i.ShouldNotContain("Yilmaz"),
            i => i.ShouldNotContain("example.com"),
            i => i.ShouldNotContain("0532"));
        var submitted = (await ViewAsync(opening)).Screening!;
        submitted.ShouldSatisfyAllConditions(s => s.Status.ShouldBe(ScreeningStatus.Submitted), s => s.Running.ShouldBe(2), s => s.EstimatedCostUsd.ShouldNotBeNull(), s => s.CanCancel.ShouldBeFalse());

        // Nothing is stored while the batch runs
        await CollectAllAsync();
        (await ViewAsync(opening)).Screening!.Status.ShouldBe(ScreeningStatus.Submitted);

        _factory.Ai.EndsBatch(batchId, new Dictionary<string, CvAnalysisResult?> { [Key(first)] = Sample(), [Key(second)] = null });
        await CollectAllAsync();

        var after = await ViewAsync(opening);
        after.ShouldSatisfyAllConditions(
            v => v.Screening!.Status.ShouldBe(ScreeningStatus.Completed),
            v => v.Screening!.Succeeded.ShouldBe(1),
            v => v.Screening!.Failed.ShouldBe(1),
            v => v.Due.ShouldBe(1),
            v => v.CanRequest.ShouldBeTrue());
        var row = after.Rows.Single(r => r.ApplicationId == first);
        row.ShouldSatisfyAllConditions(
            r => r.State.ShouldBe(ScreeningRowState.Current),
            r => r.Cells.ShouldHaveSingleItem().ShouldBe(new ScreeningCell("5 yıl B2B satış", CvRequirementStatus.Met, "Beş yıl B2B satış")));
        after.Rows.Single(r => r.ApplicationId == second).State.ShouldBe(ScreeningRowState.Failed);

        // The application card shows the analysis; the requester hears it is done
        (await Recruiter().GetFromJsonAsync<ApplicationDetails>($"/api/recruitment/applications/{first}", Ct))!.CvAnalysis!.Succeeded.ShouldBeTrue();
        var requester = await UserIdAsync("screening-recruiter");
        (await _factory.OutboxAsync<NotificationRequested>()).ShouldContain(n =>
            n.Type == ScreeningNotificationTypes.Completed && n.RecipientUserIds.Contains(requester) && n.Args["succeeded"] == "1" && n.Link == $"/recruitment/openings/{opening}/screening");

        // Other requirements make every analysis outdated
        await WithDbAsync(db => db.JobOpenings.Where(o => o.Id == JobOpeningId.From(opening))
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Qualifications, "<ul><li>3 yıl B2B satış</li></ul>"), Ct));
        var changed = await ViewAsync(opening);
        changed.ShouldSatisfyAllConditions(
            v => v.Due.ShouldBe(3),
            v => v.Rows.Single(r => r.ApplicationId == first).State.ShouldBe(ScreeningRowState.Outdated),
            v => v.Rows.Single(r => r.ApplicationId == analyzed).State.ShouldBe(ScreeningRowState.Outdated));
    }

    [Fact]
    public async Task TheNightsLimit_LeavesTheRestForTheNextNight_AndAHeldScreeningIsTriedAgain()
    {
        var company = await CompanyAsync();
        var opening = await OpeningAsync(company);
        Guid[] applications = [await ApplyAsync(opening, withCv: true), await ApplyAsync(opening, withCv: true), await ApplyAsync(opening, withCv: true)];
        (await Recruiter().PostAsync($"{Url}/{opening}/screenings", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

        // Without AI tonight it is held with the reason
        _factory.Ai.IsConfigured = false;
        await SubmitAllAsync(500);
        (await ViewAsync(opening)).Screening!.ShouldSatisfyAllConditions(s => s.Status.ShouldBe(ScreeningStatus.Held), s => s.HoldReason.ShouldBe("common.errors.ai_disabled"), s => s.CanCancel.ShouldBeTrue());
        _factory.Ai.Batches.ShouldBeEmpty();
        _factory.Ai.IsConfigured = true;

        // A limit of one sends the oldest application; the screening goes on the next night
        await SubmitAllAsync(1);
        var (firstBatch, firstItems) = _factory.Ai.Batches.ShouldHaveSingleItem();
        firstItems.ShouldHaveSingleItem().CustomId.ShouldBe(Key(applications[0]));
        _factory.Ai.EndsBatch(firstBatch, new Dictionary<string, CvAnalysisResult?> { [Key(applications[0])] = Sample() });
        await CollectAllAsync();
        (await ViewAsync(opening)).Screening!.ShouldSatisfyAllConditions(s => s.Status.ShouldBe(ScreeningStatus.Queued), s => s.Succeeded.ShouldBe(1));

        await SubmitAllAsync(500);
        var (secondBatch, secondItems) = _factory.Ai.Batches.Single(b => b.Key != firstBatch);
        secondItems.Select(i => i.CustomId).ShouldBe([Key(applications[1]), Key(applications[2])]);
        _factory.Ai.EndsBatch(secondBatch, new Dictionary<string, CvAnalysisResult?> { [Key(applications[1])] = Sample(), [Key(applications[2])] = Sample() });
        await CollectAllAsync();
        (await ViewAsync(opening)).ShouldSatisfyAllConditions(
            v => v.Screening!.Status.ShouldBe(ScreeningStatus.Completed),
            v => v.Screening!.Succeeded.ShouldBe(3),
            v => v.Due.ShouldBe(0));
    }

    [Fact]
    public async Task Requests_AreChecked_AndOnlyThoseWhoAnalyzeCvsMakeThem()
    {
        var company = await CompanyAsync();
        var opening = await OpeningAsync(company);
        var bare = await OpeningAsync(company, qualifications: null);

        var noRequirements = await Recruiter().PostAsync($"{Url}/{bare}/screenings", null, Ct);
        noRequirements.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ViewAsync(bare)).ShouldSatisfyAllConditions(v => v.HasRequirements.ShouldBeFalse(), v => v.CanRequest.ShouldBeFalse());

        var reader = _factory.CreateClientFor("screening-reader", RequisitionFactory.Tenant, RecruitmentPermissions.Applications.Read);
        (await reader.PostAsync($"{Url}/{opening}/screenings", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.GetFromJsonAsync<OpeningScreeningView>($"{Url}/{opening}/screening", Ct))!.CanRequest.ShouldBeFalse();
        (await _factory.CreateClientFor("screening-outsider", RequisitionFactory.Tenant, RecruitmentPermissions.Openings.Read).GetAsync($"{Url}/{opening}/screening", Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var scoped = _factory.CreateClientFor("screening-scoped", RequisitionFactory.Tenant, Permissions).WithScopes(AccessScope.Company(Guid.CreateVersion7()));
        (await scoped.PostAsync($"{Url}/{opening}/screenings", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await scoped.GetAsync($"{Url}/{opening}/screening", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A screening not sent yet may be called off and asked for again
        var id = await RequisitionFactory.CreatedIdAsync(await Recruiter().PostAsync($"{Url}/{opening}/screenings", null, Ct));
        (await Recruiter().PostAsync($"{Url}/{opening}/screenings/{id}/cancel", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ViewAsync(opening)).Screening!.Status.ShouldBe(ScreeningStatus.Cancelled);
        var cancelled = await Recruiter().PostAsync($"{Url}/{opening}/screenings/{id}/cancel", null, Ct);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(cancelled)).ShouldBe("recruitment.screenings.errors.not_cancellable");
        (await Recruiter().PostAsync($"{Url}/{bare}/screenings/{id}/cancel", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Recruiter().PostAsync($"{Url}/{opening}/screenings", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

        _factory.Ai.IsConfigured = false;
        var disabled = await Recruiter().PostAsync($"{Url}/{bare}/screenings", null, Ct);
        disabled.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(disabled)).ShouldBe("common.errors.ai_disabled");
    }

    private static string Key(Guid application) => application.ToString("N", CultureInfo.InvariantCulture);

    private static CvAnalysisResult Sample() => new(
        "Deneyimli B2B satış uzmanı.",
        "Sales Specialist",
        5.5m,
        CvSeniority.MidLevel,
        [new CvExperience("Sales Specialist", "Kuzey Lojistik", "2019", string.Empty, true, ["Marmara bölgesinde B2B satış"])],
        [],
        [new CvSkill("B2B satış", CvSkillCategory.Domain, CvSkillLevel.Advanced, "Beş yıl B2B satış")],
        [],
        [],
        new CvMatch(80, CvRecommendation.Match, "Uygun.", [new CvRequirementAssessment("5 yıl B2B satış", CvRequirementStatus.Met, "Beş yıl B2B satış")], ["B2B"], []),
        [],
        []);

    private HttpClient Recruiter() => _factory.CreateClientFor("screening-recruiter", RequisitionFactory.Tenant, Permissions);

    private async Task<OpeningScreeningView> ViewAsync(Guid opening)
    {
        var response = await Recruiter().GetAsync($"{Url}/{opening}/screening", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<OpeningScreeningView>(Ct))!;
    }

    /// <summary>The night run as the job does it: every waiting screening once, within the limit.</summary>
    private async Task SubmitAllAsync(int limit)
    {
        var done = new List<Guid>();
        while (await SendAsync(new SubmitNextScreeningQuery(limit, done)) is { } run)
        {
            done.Add(run.ScreeningId);
            limit -= run.Sent;
        }
    }

    private async Task CollectAllAsync()
    {
        var done = new List<Guid>();
        while (await SendAsync(new CollectNextScreeningQuery(done)) is { } run)
        {
            done.Add(run.ScreeningId);
        }
    }

    private async Task<ScreeningRun?> SendAsync(IQuery<ScreeningRun?> query)
    {
        var tenant = (await _factory.Services.GetRequiredService<ITenantStore>().FindByCodeAsync(RequisitionFactory.Tenant, Ct))!;
        await using var scope = _factory.Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        var result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(query, Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }

    /// <summary>An analysis against the opening's current requirements, as if made earlier.</summary>
    private Task<int> AnalyzedAsync(Guid application) =>
        WithDbAsync(async db =>
        {
            var row = await db.JobApplications.SingleAsync(a => a.Id == JobApplicationId.From(application), Ct);
            var opening = await db.JobOpenings.SingleAsync(o => o.Id == row.OpeningId, Ct);
            var analysis = ApplicationCvAnalysis.Of(row.Id);
            analysis.Succeed(
                row.CvFileId!.Value,
                JsonSerializer.Serialize(new CvAnalysisResponse(Sample(), [], false, "claude-sonnet-5-5", CvAnalysisPrompt.Version), JsonSerializerOptions.Web),
                "claude-sonnet-5-5",
                CvAnalysisPrompt.Version,
                null,
                DateTimeOffset.UtcNow.AddDays(-1),
                ApplicationCvAnalyzer.RequirementsHash(opening));
            db.CvAnalyses.Add(analysis);
            return await db.SaveChangesAsync(Ct);
        });

    private async Task<Guid> CompanyAsync()
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
        _managers[company] = await UserIdAsync($"manager-{Guid.NewGuid():N}");
        return company;
    }

    private async Task<Guid> UserIdAsync(string subject)
    {
        using var me = JsonDocument.Parse(await _factory.CreateClientFor(subject, RequisitionFactory.Tenant).GetStringAsync("/api/platform/me", Ct));
        return me.RootElement.GetProperty("user").GetProperty("id").GetGuid();
    }

    /// <summary>A published public opening, with requirements unless told otherwise.</summary>
    private Task<Guid> OpeningAsync(Guid company, string? qualifications = "<ul><li>5 yıl B2B satış</li></ul>") =>
        WithDbAsync(async db =>
        {
            var template = await db.StageTemplates.SingleAsync(t => t.IsDefault, Ct);
            var opening = JobOpening.Draft(
                new OpeningTerms(
                    company,
                    null,
                    "Satış Uzmanı",
                    "<p>Satış ekibimize katılacak arkadaş arıyoruz.</p>",
                    qualifications,
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

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return json.RootElement.GetProperty("code").GetString();
    }
}
