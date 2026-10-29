using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Infrastructure.MultiTenancy;
using Merp.Modules.Platform.Contracts.Notifications;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Application.Applications;
using Merp.Modules.Recruitment.Application.Openings;
using Merp.Modules.Recruitment.Application.Settings;
using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.Modules.Recruitment.Infrastructure.Persistence;
using Merp.Modules.Recruitment.IntegrationTests.Requisitions;
using Merp.Testing.Data;
using Merp.Testing.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Recruitment.IntegrationTests.Applications;

/// <summary>
/// The pipeline (PLAN R4a, D50): applications are moved, rejected with the template e-mail now or later, withdrawn
/// and reopened, every step in the history; the hiring team reads its openings' applications from self service.
/// </summary>
public sealed class PipelineTests(RequisitionFactory factory) : IClassFixture<RequisitionFactory>
{
    private const string Url = "/api/recruitment/applications";

    private static readonly string[] Permissions =
    [
        RecruitmentPermissions.Applications.Read,
        RecruitmentPermissions.Applications.Manage,
        RecruitmentPermissions.Openings.Read,
        RecruitmentPermissions.Openings.Manage,
        "platform.companies.read",
        "platform.companies.manage",
    ];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AMove_IsWrittenToTheHistory_AndTheCountsFollow()
    {
        // PLAN R4 acceptance: a stage change is written to the history
        var (company, manager, _) = await SetupAsync();
        var opening = await OpeningAsync(company, manager);
        var application = await ApplyAsync(opening);
        await ApplyAsync(opening);
        var recruiter = Recruiter();

        var pipeline = (await recruiter.GetFromJsonAsync<OpeningPipeline>($"/api/recruitment/openings/{opening}/pipeline", Ct))!;
        pipeline.ShouldSatisfyAllConditions(
            p => p.CanManage.ShouldBeTrue(),
            p => p.Stages[0].Count.ShouldBe(2),
            p => p.CompanyName.ShouldBe("Acme"),
            p => p.RejectionReasons.ShouldContain(r => r.Code == "SKILLS" && r.Name == "Yetkinlikler uyuşmuyor"),
            p => p.WithdrawalReasons.ShouldContain(r => r.Code == "OTHER_OFFER"));
        var screening = pipeline.Stages.Single(s => s.Type == StageType.Screening);

        var details = await DetailsAsync(recruiter, application);
        var moved = await recruiter.PostAsJsonAsync($"{Url}/{application}/move", new { version = details.Version, stageId = screening.Id, note = "CV uygun" }, Ct);
        moved.StatusCode.ShouldBe(HttpStatusCode.NoContent, await moved.Content.ReadAsStringAsync(Ct));

        details = await DetailsAsync(recruiter, application);
        details.Application.ShouldSatisfyAllConditions(a => a.StageId.ShouldBe(screening.Id), a => a.StageName.ShouldBe(screening.Name), a => a.SlaDueAt.ShouldNotBeNull());
        details.History.Count.ShouldBe(2);
        details.History[1].ShouldSatisfyAllConditions(
            h => h.FromStageName.ShouldBe(pipeline.Stages[0].Name),
            h => h.ToStageId.ShouldBe(screening.Id),
            h => h.Note.ShouldBe("CV uygun"),
            h => h.UserId.ShouldNotBeNull());
        (await recruiter.GetFromJsonAsync<OpeningPipeline>($"/api/recruitment/openings/{opening}/pipeline", Ct))!.Stages
            .Single(s => s.Id == screening.Id).Count.ShouldBe(1);
        var list = (await recruiter.GetFromJsonAsync<PagedResult<ApplicationListItem>>($"{Url}?openingId={opening}&stageId={screening.Id}", Ct))!;
        list.Items.ShouldHaveSingleItem().Id.ShouldBe(application);
        var openings = (await recruiter.GetFromJsonAsync<PagedResult<OpeningListItem>>($"/api/recruitment/openings?companyId={company}", Ct))!;
        openings.Items.Single(o => o.Id == opening).StageCounts.Single(s => s.StageId == screening.Id).Count.ShouldBe(1);

        // A stale version conflicts; final stages need their own step; the stage must be the opening's
        (await recruiter.PostAsJsonAsync($"{Url}/{application}/move", new { version = details.Version - 1, stageId = pipeline.Stages[0].Id }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var rejectedStage = pipeline.Stages.Single(s => s.Type == StageType.Rejected).Id;
        (await ProblemAsync(await recruiter.PostAsJsonAsync($"{Url}/{application}/move", new { version = details.Version, stageId = rejectedStage }, Ct))).ShouldContainKey("stageId");
        (await recruiter.PostAsJsonAsync($"{Url}/{application}/move", new { version = details.Version, stageId = Guid.CreateVersion7() }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await recruiter.PostAsJsonAsync($"{Url}/{application}/move", new { version = details.Version, stageId = Guid.Empty }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ARejection_SendsTheTemplateEmail_NowOrLater()
    {
        // PLAN R4 acceptance: the rejection e-mail is sent with the template
        var (company, manager, _) = await SetupAsync();
        var opening = await OpeningAsync(company, manager);
        var recruiter = Recruiter();
        var now = await ApplyAsync(opening, "simdi");
        var later = await ApplyAsync(opening, "sonra");
        var reopened = await ApplyAsync(opening, "geri");

        var rejected = await RejectAsync(recruiter, now, new { reasonCode = "skills", note = "Excel bilgisi yok", sendEmail = true, emailDelayDays = 0 });
        rejected.StatusCode.ShouldBe(HttpStatusCode.NoContent, await rejected.Content.ReadAsStringAsync(Ct));
        var details = await DetailsAsync(recruiter, now);
        details.ShouldSatisfyAllConditions(
            d => d.Application.Status.ShouldBe(ApplicationStatus.Rejected),
            d => d.ReasonCode.ShouldBe("SKILLS"),
            d => d.ReasonName.ShouldBe("Yetkinlikler uyuşmuyor"),
            d => d.RejectionEmailSentAt.ShouldNotBeNull(),
            d => d.History[^1].ReasonName.ShouldBe("Yetkinlikler uyuşmuyor"));
        var email = EmailOf("simdi", opening);
        (await factory.OutboxAsync<ExternalEmailRequested>()).ShouldContain(e => e.To == email && e.Type == RecruitmentNotificationTypes.ApplicationRejected && e.Args["position"] == "Satış Uzmanı");
        await factory.DeliverMessagesAsync();
        (await PlatformSubjectsAsync(email)).ShouldBe(["Satış Uzmanı başvurunuz alındı", "Satış Uzmanı başvurunuz hakkında"], ignoreOrder: true);

        // Two days later: waits for the job; a reopened application drops its e-mail
        (await RejectAsync(recruiter, later, new { reasonCode = "EXPERIENCE", sendEmail = true, emailDelayDays = 2 })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await RejectAsync(recruiter, reopened, new { reasonCode = "EXPERIENCE", sendEmail = true, emailDelayDays = 2 })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await recruiter.PostAsJsonAsync($"{Url}/{reopened}/reopen", new { version = (await DetailsAsync(recruiter, reopened)).Version }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SendDueEmailsAsync()).ShouldBe(0);
        await WithDbAsync(db => db.JobApplications.Where(a => a.RejectionEmailDueAt != null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.RejectionEmailDueAt, DateTimeOffset.UtcNow.AddMinutes(-1)), Ct));
        (await SendDueEmailsAsync()).ShouldBe(1);
        var rejections = (await factory.OutboxAsync<ExternalEmailRequested>()).Where(e => e.Type == RecruitmentNotificationTypes.ApplicationRejected).ToList();
        rejections.ShouldContain(e => e.To == EmailOf("sonra", opening));
        rejections.ShouldNotContain(e => e.To == EmailOf("geri", opening));
        (await DetailsAsync(recruiter, reopened)).Application.Status.ShouldBe(ApplicationStatus.Active);

        // The reason must be an active rejection reason; the delay at most 14 days
        var other = await ApplyAsync(opening);
        (await ProblemAsync(await RejectAsync(recruiter, other, new { reasonCode = "OTHER_OFFER", sendEmail = false, emailDelayDays = 0 }))).ShouldContainKey("reasonCode");
        (await ProblemAsync(await RejectAsync(recruiter, other, new { reasonCode = "SKILLS", sendEmail = true, emailDelayDays = 15 }))).ShouldContainKey("emailDelayDays");
    }

    [Fact]
    public async Task AWithdrawal_IsReopenedToTheStageItLeftFrom()
    {
        var (company, manager, _) = await SetupAsync();
        var opening = await OpeningAsync(company, manager);
        var application = await ApplyAsync(opening);
        var recruiter = Recruiter();
        var pipeline = (await recruiter.GetFromJsonAsync<OpeningPipeline>($"/api/recruitment/openings/{opening}/pipeline", Ct))!;
        var interview = pipeline.Stages.First(s => s.Type == StageType.Interview).Id;
        await recruiter.PostAsJsonAsync($"{Url}/{application}/move", new { version = (await DetailsAsync(recruiter, application)).Version, stageId = interview }, Ct);

        var withdrawn = await recruiter.PostAsJsonAsync($"{Url}/{application}/withdraw", new { version = (await DetailsAsync(recruiter, application)).Version, reasonCode = "OTHER_OFFER" }, Ct);
        withdrawn.StatusCode.ShouldBe(HttpStatusCode.NoContent, await withdrawn.Content.ReadAsStringAsync(Ct));
        var details = await DetailsAsync(recruiter, application);
        details.ShouldSatisfyAllConditions(d => d.Application.Status.ShouldBe(ApplicationStatus.Withdrawn), d => d.ReasonName.ShouldBe("Başka bir teklifi kabul etti"));
        (await recruiter.PostAsJsonAsync($"{Url}/{application}/move", new { version = details.Version, stageId = interview }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        (await recruiter.PostAsJsonAsync($"{Url}/{application}/reopen", new { version = details.Version, note = "Teklifi geri çevirdi" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        details = await DetailsAsync(recruiter, application);
        details.Application.ShouldSatisfyAllConditions(a => a.Status.ShouldBe(ApplicationStatus.Active), a => a.StageId.ShouldBe(interview));
        details.History.Select(h => h.Status).ShouldBe([ApplicationStatus.Active, ApplicationStatus.Active, ApplicationStatus.Withdrawn, ApplicationStatus.Active]);
        (await recruiter.PostAsJsonAsync($"{Url}/{application}/reopen", new { version = details.Version }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await recruiter.PostAsJsonAsync($"{Url}/{application}/withdraw", new { version = details.Version, reasonCode = "" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ApplicationsOutsideTheScope_AreNotFound_AndReadersCannotMove()
    {
        var (company, manager, _) = await SetupAsync();
        var (otherCompany, _, _) = await SetupAsync();
        var opening = await OpeningAsync(company, manager);
        var application = await ApplyAsync(opening, "kapsam");

        var scoped = factory.CreateClientFor("scoped-recruiter", RequisitionFactory.Tenant, Permissions).WithScopes(AccessScope.Company(otherCompany));
        (await scoped.GetAsync($"{Url}/{application}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await scoped.GetAsync($"/api/recruitment/openings/{opening}/pipeline", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await scoped.GetAsync($"{Url}/{application}/cv", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await scoped.PostAsJsonAsync($"{Url}/{application}/move", new { version = 1u, stageId = Guid.CreateVersion7() }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await scoped.GetFromJsonAsync<PagedResult<ApplicationListItem>>(Url, Ct))!.Items.ShouldNotContain(a => a.Id == application);

        var reader = factory.CreateClientFor("application-reader", RequisitionFactory.Tenant, RecruitmentPermissions.Applications.Read);
        (await reader.GetFromJsonAsync<ApplicationDetails>($"{Url}/{application}", Ct))!.CanManage.ShouldBeFalse();
        (await reader.PostAsJsonAsync($"{Url}/{application}/move", new { version = 1u, stageId = Guid.CreateVersion7() }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await factory.CreateClientFor("nobody", RequisitionFactory.Tenant).GetAsync(Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Recruiter().GetAsync($"{Url}?sort=email", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TheHiringTeam_ReadsItsOpenings_FromSelfService()
    {
        var (company, manager, managerSubject) = await SetupAsync();
        var recruiterSubject = $"team-{Guid.NewGuid():N}";
        var teamRecruiter = await UserIdAsync(factory.CreateClientFor(recruiterSubject, RequisitionFactory.Tenant));
        var opening = await OpeningAsync(company, manager, teamRecruiter);
        var otherOpening = await OpeningAsync(company, (await SetupAsync()).Manager);
        var application = await ApplyAsync(opening, "ekip", withCv: true);
        await ApplyAsync(otherOpening);

        foreach (var member in new[] { managerSubject, recruiterSubject })
        {
            var client = factory.CreateClientFor(member, RequisitionFactory.Tenant, RecruitmentPermissions.Self.Hiring);
            var openings = (await client.GetFromJsonAsync<List<OpeningListItem>>("/api/recruitment/me/hiring/openings", Ct))!;
            openings.ShouldContain(o => o.Id == opening);
            openings.ShouldNotContain(o => o.Id == otherOpening);
            var pipeline = (await client.GetFromJsonAsync<OpeningPipeline>($"/api/recruitment/me/hiring/openings/{opening}/pipeline", Ct))!;
            pipeline.ShouldSatisfyAllConditions(p => p.CanManage.ShouldBeFalse(), p => p.Stages[0].Count.ShouldBe(1), p => p.RejectionReasons.ShouldBeEmpty());
            (await client.GetFromJsonAsync<PagedResult<ApplicationListItem>>($"/api/recruitment/me/hiring/applications?openingId={opening}", Ct))!
                .Items.ShouldHaveSingleItem().Id.ShouldBe(application);
            var details = (await client.GetFromJsonAsync<ApplicationDetails>($"/api/recruitment/me/hiring/applications/{application}", Ct))!;
            details.ShouldSatisfyAllConditions(d => d.CanManage.ShouldBeFalse(), d => d.Cv!.FileName.ShouldBe("ozgecmis.pdf"));
            var cv = await client.GetAsync($"/api/recruitment/me/hiring/applications/{application}/cv", Ct);
            cv.StatusCode.ShouldBe(HttpStatusCode.OK);
            cv.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");

            // Not the team's: not found; the list needs the opening
            (await client.GetAsync($"/api/recruitment/me/hiring/openings/{otherOpening}/pipeline", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await client.GetAsync($"/api/recruitment/me/hiring/applications?openingId={otherOpening}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await client.GetAsync("/api/recruitment/me/hiring/applications", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await client.GetAsync($"{Url}/{application}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        (await factory.CreateClientFor(managerSubject, RequisitionFactory.Tenant).GetAsync("/api/recruitment/me/hiring/openings", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnOpeningsStages_Change_ButStagesWithApplicationsStay()
    {
        var (company, manager, _) = await SetupAsync();
        var opening = await OpeningAsync(company, manager);
        var application = await ApplyAsync(opening);
        var recruiter = Recruiter();
        var details = (await recruiter.GetFromJsonAsync<OpeningDetails>($"/api/recruitment/openings/{opening}", Ct))!;
        var stages = details.Stages.Select(s => new StageInput(s.Id, new Dictionary<string, string?> { ["tr-TR"] = s.Name }, s.Type, s.SlaDays)).ToList();
        var screening = details.Stages.Single(s => s.Type == StageType.Screening).Id;
        await recruiter.PostAsJsonAsync($"{Url}/{application}/move", new { version = (await DetailsAsync(recruiter, application)).Version, stageId = screening }, Ct);

        var withoutScreening = stages.Where(s => s.Id != screening).ToList();
        var refused = await recruiter.PutAsJsonAsync($"/api/recruitment/openings/{opening}/stages", new { version = details.Version, stages = withoutScreening }, Ct);
        (await ProblemAsync(refused)).ShouldContainKey("stages");

        var withCase = stages.ToList();
        withCase.Insert(2, new StageInput(null, new Dictionary<string, string?> { ["tr-TR"] = "Vaka çalışması" }, StageType.Assessment, 4));
        var changed = await recruiter.PutAsJsonAsync($"/api/recruitment/openings/{opening}/stages", new { version = details.Version, stages = withCase }, Ct);
        changed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await changed.Content.ReadAsStringAsync(Ct));
        (await recruiter.GetFromJsonAsync<OpeningPipeline>($"/api/recruitment/openings/{opening}/pipeline", Ct))!.Stages[2].Name.ShouldBe("Vaka çalışması");

        (await recruiter.PutAsJsonAsync($"/api/recruitment/openings/{opening}/stages", new { version = details.Version, stages = Array.Empty<StageInput>() }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var (otherCompany, _, _) = await SetupAsync();
        var scoped = factory.CreateClientFor("scoped-recruiter", RequisitionFactory.Tenant, Permissions).WithScopes(AccessScope.Company(otherCompany));
        (await scoped.PutAsJsonAsync($"/api/recruitment/openings/{opening}/stages", new { version = details.Version, stages = withCase }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await factory.CreateClientFor("application-reader", RequisitionFactory.Tenant, RecruitmentPermissions.Applications.Read)
            .PutAsJsonAsync($"/api/recruitment/openings/{opening}/stages", new { version = details.Version, stages = withCase }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private HttpClient Recruiter() => factory.CreateClientFor("pipeline-recruiter", RequisitionFactory.Tenant, Permissions);

    /// <summary>A company and a user who hires for it.</summary>
    private async Task<(Guid Company, Guid Manager, string ManagerSubject)> SetupAsync()
    {
        var company = await RequisitionFactory.CreatedIdAsync(await Recruiter().PostAsJsonAsync(
            "/api/platform/companies",
            new
            {
                code = NewCode("C"),
                title = "Acme Holding Anonim Şirketi",
                shortName = "Acme",
                taxOffice = "Kadıköy",
                taxNumber = TurkishData.Vkn(),
                address = new { line1 = "Atatürk Cad. No: 1", provinceCode = 34, districtCode = 1421, postalCode = "34710" },
            },
            Ct));
        var subject = $"manager-{Guid.NewGuid():N}";
        return (company, await UserIdAsync(factory.CreateClientFor(subject, RequisitionFactory.Tenant)), subject);
    }

    /// <summary>A published public opening with the default stages, asking only name and e-mail.</summary>
    private Task<Guid> OpeningAsync(Guid company, Guid manager, params Guid[] recruiters) =>
        WithDbAsync(async db =>
        {
            var template = await db.StageTemplates.SingleAsync(t => t.IsDefault, Ct);
            var opening = JobOpening.Draft(
                new OpeningTerms(
                    company,
                    null,
                    "Satış Uzmanı",
                    null,
                    null,
                    null,
                    WorkModel.OnSite,
                    RequisitionEmploymentType.Indefinite,
                    1,
                    OpeningVisibility.Public,
                    null,
                    null,
                    new OpeningTeam(manager, recruiters, []),
                    new ApplicationForm(RequirePhone: false, RequireCv: false, AskCoverLetter: false, [])),
                template);
            opening.Publish(DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime.AddDays(-1)), DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();
            db.JobOpenings.Add(opening);
            await db.SaveChangesAsync(Ct);
            return opening.Id.Value;
        });

    private static string EmailOf(string name, Guid opening) => $"{name}.{opening:N}@ornek.test";

    /// <summary>Applies from the career page as <paramref name="name"/>; returns the application.</summary>
    private async Task<Guid> ApplyAsync(Guid opening, string? name = null, bool withCv = false)
    {
        var email = EmailOf(name ?? Guid.NewGuid().ToString("N")[..8], opening);
        var body = new
        {
            firstName = "Ayşe",
            lastName = "Yılmaz",
            email,
            disclosureAccepted = true,
            disclosureRevision = 0,
            poolConsent = false,
            culture = "tr-TR",
        };
        using var form = new MultipartFormDataContent { { new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), Encoding.UTF8), "application" } };
        if (withCv)
        {
            var cv = new ByteArrayContent("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF"u8.ToArray());
            cv.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(cv, "cv", "ozgecmis.pdf");
        }

        var response = await factory.CreateClient().PostAsync($"/api/public/{RequisitionFactory.Tenant}/careers/openings/{opening}/applications", form, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        return await WithDbAsync(db => db.JobApplications
            .Where(a => a.OpeningId == JobOpeningId.From(opening))
            .Join(db.Candidates.Where(c => c.Email == email), a => a.CandidateId, c => c.Id, (a, _) => a.Id.Value)
            .SingleAsync(Ct));
    }

    /// <summary>Rejects with <paramref name="body"/> and the version just read.</summary>
    private static async Task<HttpResponseMessage> RejectAsync(HttpClient client, Guid application, object body)
    {
        var json = JsonSerializer.SerializeToNode(body, JsonSerializerOptions.Web)!.AsObject();
        json["version"] = (await DetailsAsync(client, application)).Version;
        return await client.PostAsJsonAsync($"{Url}/{application}/reject", json, Ct);
    }

    private async Task<int> SendDueEmailsAsync()
    {
        var tenant = (await factory.Services.GetRequiredService<ITenantStore>().FindByCodeAsync(RequisitionFactory.Tenant, Ct))!;
        await using var scope = factory.Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        return (await scope.ServiceProvider.GetRequiredService<ISender>().Send(new SendDueRejectionEmailsCommand(), Ct)).Value;
    }

    /// <summary>The subjects of the e-mails queued for <paramref name="to"/>: the received e-mail and any later ones.</summary>
    private async Task<List<string>> PlatformSubjectsAsync(string to)
    {
        var tenant = (await factory.Services.GetRequiredService<ITenantStore>().FindByCodeAsync(RequisitionFactory.Tenant, Ct))!;
        await using var scope = factory.Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        var db = scope.ServiceProvider.GetRequiredService<Merp.Modules.Platform.Infrastructure.Persistence.PlatformDbContext>();
        return await db.EmailMessages.AsNoTracking().Where(m => m.To == to).Select(m => m.Subject).ToListAsync(Ct);
    }


    private static async Task<Guid> UserIdAsync(HttpClient client)
    {
        using var me = JsonDocument.Parse(await client.GetStringAsync("/api/platform/me", Ct));
        return me.RootElement.GetProperty("user").GetProperty("id").GetGuid();
    }

    private static async Task<ApplicationDetails> DetailsAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<ApplicationDetails>($"{Url}/{id}", Ct))!;

    private async Task<T> WithDbAsync<T>(Func<RecruitmentDbContext, Task<T>> work)
    {
        var tenant = (await factory.Services.GetRequiredService<ITenantStore>().FindByCodeAsync(RequisitionFactory.Tenant, Ct))!;
        await using var scope = factory.Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        return await work(scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>());
    }

    private static async Task<Dictionary<string, string[]>> ProblemAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync(Ct));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return json.RootElement.GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;
    }

    private static string NewCode(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
}
