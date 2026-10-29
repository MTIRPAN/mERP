using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Infrastructure.MultiTenancy;
using Merp.Modules.Platform.Contracts.Notifications;
using Merp.Modules.Platform.Infrastructure.Persistence;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Application.Applications;
using Merp.Modules.Recruitment.Application.Interviews;
using Merp.Modules.Recruitment.Application.Settings;
using Merp.Modules.Recruitment.Domain.Candidates;
using Merp.Modules.Recruitment.Domain.Interviews;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.Modules.Recruitment.Infrastructure.Persistence;
using Merp.Modules.Recruitment.IntegrationTests.Requisitions;
using Merp.Testing.Data;
using Merp.Testing.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Recruitment.IntegrationTests.Interviews;

/// <summary>
/// Interviews and evaluations (PLAN R5, D52): invitations with a calendar file, updates and cancellations with a raised
/// SEQUENCE, conflicts, reminders, the interviewers' forms under the blind rule and the weighted score.
/// </summary>
public sealed class InterviewTests(RequisitionFactory factory) : IClassFixture<RequisitionFactory>
{
    private const string Url = "/api/recruitment/interviews";

    private static readonly string[] Permissions =
    [
        RecruitmentPermissions.Interviews.Read,
        RecruitmentPermissions.Interviews.Manage,
        RecruitmentPermissions.Feedback.Submit,
        RecruitmentPermissions.Applications.Read,
        RecruitmentPermissions.Settings.Read,
        RecruitmentPermissions.Settings.Manage,
        "platform.companies.read",
        "platform.companies.manage",
    ];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task APlannedInterview_InvitesEveryone_AndAChangeSendsTheUpdate()
    {
        // PLAN R5 acceptance: invitations go for a planned interview; a new time sends the updated invitation
        var (company, manager, _) = await SetupAsync();
        var (application, email) = await ApplicationAsync(company, manager);
        var (ayse, _) = await UserAsync();
        var (mehmet, _) = await UserAsync();
        var start = NextWeek(10);

        var id = await RequisitionFactory.CreatedIdAsync(await Recruiter().PostAsJsonAsync(Url, Body(application, start, ayse, mehmet), Ct));

        var invitation = (await CandidateEmailsAsync(email)).ShouldHaveSingleItem();
        invitation.ShouldSatisfyAllConditions(
            e => e.Type.ShouldBe(RecruitmentNotificationTypes.InterviewInvitation),
            e => e.Args["position"].ShouldBe("Yazılım Geliştirici"),
            e => e.Args["location"].ShouldBe("https://meet.example/abc"));
        var ics = Calendar(invitation.Attachments.ShouldHaveSingleItem());
        ics.ShouldContain("METHOD:REQUEST");
        ics.ShouldContain($"UID:{id}@merp");
        ics.ShouldContain("SEQUENCE:0");
        ics.ShouldContain(email, Case.Insensitive);
        var invited = (await factory.OutboxAsync<NotificationRequested>()).Single(n => n.Type == InterviewNotificationTypes.Invited && n.RecipientUserIds.Contains(ayse));
        invited.ShouldSatisfyAllConditions(n => n.RecipientUserIds.ShouldBe([ayse, mehmet], ignoreOrder: true), n => n.Attachments.ShouldHaveSingleItem());

        await factory.DeliverMessagesAsync();
        // The candidate got the application e-mail too; only the invitation carries the calendar file
        var queued = await PlatformAsync(db => db.EmailMessages.AsNoTracking().Where(m => m.To == email).ToListAsync(Ct));
        queued.Count.ShouldBe(2);
        queued.SelectMany(m => m.Attachments).ShouldHaveSingleItem().FileName.ShouldBe("interview.ics");

        // A new time and one interviewer less: the update for all, the cancellation for the one taken off
        var details = await DetailsAsync(Recruiter(), id);
        var moved = await Recruiter().PutAsJsonAsync($"{Url}/{id}", Body(application, start.AddHours(2), ayse) with { Version = details.Version }, Ct);
        moved.StatusCode.ShouldBe(HttpStatusCode.NoContent, await moved.Content.ReadAsStringAsync(Ct));
        var update = (await CandidateEmailsAsync(email)).Single(e => e.Type == RecruitmentNotificationTypes.InterviewUpdated);
        Calendar(update.Attachments.Single()).ShouldContain("SEQUENCE:1");
        (await factory.OutboxAsync<NotificationRequested>()).ShouldContain(n => n.Type == InterviewNotificationTypes.Cancelled && n.RecipientUserIds.SequenceEqual(new[] { mehmet }));

        var cancelled = await Recruiter().PostAsJsonAsync($"{Url}/{id}/cancel", new { version = (await DetailsAsync(Recruiter(), id)).Version, reason = "Aday erteledi" }, Ct);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var cancellation = (await CandidateEmailsAsync(email)).Single(e => e.Type == RecruitmentNotificationTypes.InterviewCancelled);
        Calendar(cancellation.Attachments.Single()).ShouldSatisfyAllConditions(c => c.ShouldContain("METHOD:CANCEL"), c => c.ShouldContain("SEQUENCE:2"));
        (await Recruiter().PostAsJsonAsync($"{Url}/{id}/cancel", new { version = (await DetailsAsync(Recruiter(), id)).Version }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task APlan_IsChecked_AndOnlyThoseWhoManageInterviewsMakeIt()
    {
        var (company, manager, _) = await SetupAsync();
        var (otherCompany, _, _) = await SetupAsync();
        var (application, _) = await ApplicationAsync(company, manager);
        var (ayse, _) = await UserAsync();
        var start = NextWeek(14);

        (await ProblemAsync(await Recruiter().PostAsJsonAsync(Url, Body(application, start, ayse) with { EndsAt = start.AddHours(-1) }, Ct))).ShouldContainKey("endsAt");
        (await ProblemAsync(await Recruiter().PostAsJsonAsync(Url, Body(application, start, Guid.CreateVersion7()), Ct))).ShouldContainKey("interviewerUserIds");
        (await ProblemAsync(await Recruiter().PostAsJsonAsync(Url, Body(application, start, ayse) with { OnlineLink = "javascript:alert(1)" }, Ct))).ShouldContainKey("onlineLink");
        (await ProblemAsync(await Recruiter().PostAsJsonAsync(Url, Body(application, start, ayse) with { EvaluationTemplateId = Guid.CreateVersion7() }, Ct))).ShouldContainKey("evaluationTemplateId");
        var scoped = factory.CreateClientFor("scoped-recruiter", RequisitionFactory.Tenant, Permissions).WithScopes(AccessScope.Company(otherCompany));
        (await ProblemAsync(await scoped.PostAsJsonAsync(Url, Body(application, start, ayse), Ct))).ShouldContainKey("applicationId");
        (await factory.CreateClientFor("reader", RequisitionFactory.Tenant, RecruitmentPermissions.Interviews.Read).PostAsJsonAsync(Url, Body(application, start, ayse), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Another interview of the interviewer at the same time is shown, not refused
        var id = await RequisitionFactory.CreatedIdAsync(await Recruiter().PostAsJsonAsync(Url, Body(application, start, ayse), Ct));
        var range = $"startsAt={Uri.EscapeDataString(start.AddMinutes(30).ToString("O"))}&endsAt={Uri.EscapeDataString(start.AddMinutes(90).ToString("O"))}&interviewerUserIds={ayse}";
        (await Recruiter().GetFromJsonAsync<List<InterviewConflict>>($"{Url}/conflicts?{range}", Ct))!.ShouldHaveSingleItem().InterviewId.ShouldBe(id);
        (await Recruiter().GetFromJsonAsync<List<InterviewConflict>>($"{Url}/conflicts?{range}&exceptInterviewId={id}", Ct)).ShouldBeEmpty();

        var other = await RequisitionFactory.CreatedIdAsync(await Recruiter().PostAsJsonAsync(Url, Body(application, start, ayse), Ct));
        (await Recruiter().GetFromJsonAsync<ApplicationDetails>($"/api/recruitment/applications/{application}", Ct))!.ShouldSatisfyAllConditions(
            d => d.Interviews.Select(i => i.Id).ShouldBe([id, other], ignoreOrder: true),
            d => d.CanScheduleInterview.ShouldBeTrue());
    }

    [Fact]
    public async Task TheBlindRule_HidesOtherFormsUntilTheInterviewerSendsTheirs()
    {
        // PLAN R5 acceptance: blind feedback, checked by a test
        var (company, manager, managerSubject) = await SetupAsync();
        var (application, _) = await ApplicationAsync(company, manager);
        var (ayse, ayseSubject) = await UserAsync();
        var (mehmet, mehmetSubject) = await UserAsync();
        var (stranger, strangerSubject) = await UserAsync();
        var template = await TemplateAsync();
        var id = await RequisitionFactory.CreatedIdAsync(await Recruiter().PostAsJsonAsync(Url, Body(application, NextWeek(9), ayse, mehmet) with { EvaluationTemplateId = template }, Ct));
        var ayseClient = Interviewer(ayseSubject);
        var mehmetClient = Interviewer(mehmetSubject);

        var form = (await DetailsAsync(ayseClient, id)).Evaluation!;
        var criteria = form.Criteria.ToList();
        object Scores(int first, int second) => new
        {
            scores = new[] { new { criterionId = criteria[0].Id, score = first }, new { criterionId = criteria[1].Id, score = second } },
            answers = new[] { new { questionId = form.Questions[0].Id, text = "Analitik" } },
            recommendation = "Yes",
            summary = "Öneririm",
        };

        (await ayseClient.PutAsJsonAsync($"{Url}/{id}/feedback", Scores(5, 3), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var before = await DetailsAsync(mehmetClient, id);
        before.ShouldSatisfyAllConditions(d => d.FeedbackHidden.ShouldBeTrue(), d => d.Feedback.ShouldBeEmpty(), d => d.ApplicationScore.ShouldBeNull(), d => d.CanSubmitFeedback.ShouldBeTrue());
        (await mehmetClient.PutAsJsonAsync($"{Url}/{id}/feedback", Scores(1, 1), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = await DetailsAsync(mehmetClient, id);
        after.ShouldSatisfyAllConditions(d => d.FeedbackHidden.ShouldBeFalse(), d => d.Feedback.Count.ShouldBe(2), d => d.MyFeedback!.WeightedScore.ShouldBe(1m));

        // HR and the hiring manager see every form and the weighted score: (5×3 + 3×1) / 4 = 4.5 and 1, average 2.75
        (await DetailsAsync(Recruiter(), id)).ShouldSatisfyAllConditions(d => d.Feedback.Count.ShouldBe(2), d => d.ApplicationScore.ShouldBe(2.75m));
        var team = factory.CreateClientFor(managerSubject, RequisitionFactory.Tenant, RecruitmentPermissions.Self.Hiring);
        (await team.GetFromJsonAsync<ApplicationDetails>($"/api/recruitment/me/hiring/applications/{application}", Ct))!
            .ShouldSatisfyAllConditions(d => d.Feedback.Count.ShouldBe(2), d => d.Score.ShouldBe(2.75m), d => d.CanScheduleInterview.ShouldBeFalse());

        // Others are kept out; the form must score every criterion
        (await Interviewer(strangerSubject).GetAsync($"{Url}/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Interviewer(strangerSubject).PutAsJsonAsync($"{Url}/{id}/feedback", Scores(3, 3), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var partial = await ayseClient.PutAsJsonAsync($"{Url}/{id}/feedback", new { scores = new[] { new { criterionId = criteria[0].Id, score = 4 } }, recommendation = "No" }, Ct);
        (await ProblemAsync(partial)).ShouldContainKey("scores");
        stranger.ShouldNotBe(ayse);
    }

    [Fact]
    public async Task MyInterviews_ListsTheOnesIInterviewIn_AndRemindersGoADayAndAnHourBefore()
    {
        var (company, manager, _) = await SetupAsync();
        var (application, email) = await ApplicationAsync(company, manager);
        var (ayse, ayseSubject) = await UserAsync();
        var (mehmet, _) = await UserAsync();
        var mine = await RequisitionFactory.CreatedIdAsync(await Recruiter().PostAsJsonAsync(Url, Body(application, NextWeek(11), ayse), Ct));
        var notMine = await RequisitionFactory.CreatedIdAsync(await Recruiter().PostAsJsonAsync(Url, Body(application, NextWeek(15), mehmet), Ct));

        var list = (await Interviewer(ayseSubject).GetFromJsonAsync<PagedResult<InterviewListItem>>($"{Url}?mine=true", Ct))!.Items;
        list.ShouldContain(i => i.Id == mine);
        list.ShouldNotContain(i => i.Id == notMine);
        (await Interviewer(ayseSubject).GetFromJsonAsync<PagedResult<InterviewListItem>>(Url, Ct))!.Items.ShouldNotContain(i => i.Id == notMine);
        (await Interviewer(ayseSubject).GetAsync($"{Url}/{notMine}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Interviewer(ayseSubject).GetAsync($"{Url}/{mine}/cv", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A day before, then an hour before; each once
        (await RemindAsync()).ShouldBe(0);
        await MoveAsync(mine, DateTimeOffset.UtcNow.AddHours(20));
        (await RemindAsync()).ShouldBe(1);
        (await CandidateEmailsAsync(email)).ShouldContain(e => e.Type == RecruitmentNotificationTypes.InterviewReminder);
        (await RemindAsync()).ShouldBe(0);
        await MoveAsync(mine, DateTimeOffset.UtcNow.AddMinutes(30));
        (await RemindAsync()).ShouldBe(1);
        (await factory.OutboxAsync<NotificationRequested>()).Count(n => n.Type == InterviewNotificationTypes.Reminder && n.RecipientUserIds.Contains(ayse)).ShouldBe(2);
        (await RemindAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task EvaluationTemplates_AreChecked_AndKeptByTheTenant()
    {
        var invalid = await Recruiter().PostAsJsonAsync("/api/recruitment/settings/evaluation-templates", new { name = "Boş", form = new { sections = Array.Empty<object>(), questions = Array.Empty<object>() } }, Ct);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var id = await TemplateAsync();
        var templates = (await Recruiter().GetFromJsonAsync<List<EvaluationTemplateItem>>("/api/recruitment/settings/evaluation-templates", Ct))!;
        var template = templates.Single(t => t.Id == id);
        template.Form.Criteria.ShouldAllBe(c => c.Id != Guid.Empty);

        var renamed = await Recruiter().PutAsJsonAsync($"/api/recruitment/settings/evaluation-templates/{id}", new { version = template.Version, name = "Teknik mülakat v2", form = template.Form, isActive = false }, Ct);
        renamed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Recruiter().GetFromJsonAsync<List<InterviewTemplateOption>>($"{Url}/templates", Ct))!.ShouldNotContain(t => t.Id == id);

        var scoped = factory.CreateClientFor("scoped-recruiter", RequisitionFactory.Tenant, Permissions).WithScopes(AccessScope.Company(Guid.CreateVersion7()));
        (await scoped.PostAsJsonAsync("/api/recruitment/settings/evaluation-templates", TemplateBody(), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private HttpClient Recruiter() => factory.CreateClientFor("interview-recruiter", RequisitionFactory.Tenant, Permissions);

    /// <summary>A user with the Interviewer role: its permissions with the Self scope.</summary>
    private HttpClient Interviewer(string subject) =>
        factory.CreateClientFor(subject, RequisitionFactory.Tenant, RecruitmentPermissions.Interviews.Read, RecruitmentPermissions.Feedback.Submit)
            .WithScopes(new AccessScope(AccessScopeTypes.Self, null));

    private static DateTimeOffset NextWeek(int hour) => new DateTimeOffset(DateTimeOffset.UtcNow.Date.AddDays(7), TimeSpan.Zero).AddHours(hour);

    private static InterviewBody Body(Guid application, DateTimeOffset start, params Guid[] interviewers) =>
        new(application, "Technical", start, start.AddHours(1), null, "https://meet.example/abc", interviewers, "Projeleri sorun", null);

    private static object TemplateBody() => new
    {
        name = "Teknik mülakat",
        form = new
        {
            sections = new[]
            {
                new
                {
                    title = "Teknik",
                    criteria = new[]
                    {
                        new { id = Guid.Empty, name = "Problem çözme", scoreDescriptions = new[] { "1", "2", "3", "4", "5" }, weight = 3m },
                        new { id = Guid.Empty, name = "İletişim", scoreDescriptions = new[] { "", "", "", "", "" }, weight = 1m },
                    },
                },
            },
            questions = new[] { new { id = Guid.Empty, text = "En güçlü yanı ne?" } },
        },
    };

    private async Task<Guid> TemplateAsync() =>
        await RequisitionFactory.CreatedIdAsync(await Recruiter().PostAsJsonAsync("/api/recruitment/settings/evaluation-templates", TemplateBody(), Ct));

    private static string Calendar(EmailFile file)
    {
        file.ContentType.ShouldStartWith("text/calendar");
        // Folded lines joined (RFC 5545 §3.1)
        return Encoding.UTF8.GetString(file.Content).Replace("\r\n ", string.Empty, StringComparison.Ordinal);
    }

    private async Task<List<ExternalEmailRequested>> CandidateEmailsAsync(string email) =>
        [.. (await factory.OutboxAsync<ExternalEmailRequested>()).Where(e => e.To == email && e.Type != RecruitmentNotificationTypes.ApplicationReceived)];

    private static async Task<InterviewDetails> DetailsAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<InterviewDetails>($"{Url}/{id}", Ct))!;

    /// <summary>A company and a user who hires for it.</summary>
    private async Task<(Guid Company, Guid Manager, string ManagerSubject)> SetupAsync()
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
        var (manager, subject) = await UserAsync();
        return (company, manager, subject);
    }

    private async Task<(Guid Id, string Subject)> UserAsync()
    {
        var subject = $"user-{Guid.NewGuid():N}";
        using var me = JsonDocument.Parse(await factory.CreateClientFor(subject, RequisitionFactory.Tenant).GetStringAsync("/api/platform/me", Ct));
        return (me.RootElement.GetProperty("user").GetProperty("id").GetGuid(), subject);
    }

    /// <summary>A published opening and an application to it from the career page; returns the application and the candidate's e-mail.</summary>
    private async Task<(Guid Application, string Email)> ApplicationAsync(Guid company, Guid manager)
    {
        var opening = await WithDbAsync(async db =>
        {
            var template = await db.StageTemplates.SingleAsync(t => t.IsDefault, Ct);
            var draft = JobOpening.Draft(
                new OpeningTerms(company, null, "Yazılım Geliştirici", null, null, null, WorkModel.OnSite, RequisitionEmploymentType.Indefinite, 1, OpeningVisibility.Public, null, null, new OpeningTeam(manager, [], []), new ApplicationForm(false, false, false, [])),
                template);
            draft.Publish(DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime.AddDays(-1)), DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();
            db.JobOpenings.Add(draft);
            await db.SaveChangesAsync(Ct);
            return draft.Id.Value;
        });
        var email = $"aday.{Guid.NewGuid().ToString("N")[..12]}@ornek.test";
        var body = new { firstName = "Ayşe", lastName = "Yılmaz", email, disclosureAccepted = true, disclosureRevision = 0, poolConsent = false, culture = "tr-TR" };
        using var form = new MultipartFormDataContent { { new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), Encoding.UTF8), "application" } };
        var response = await factory.CreateClient().PostAsync($"/api/public/{RequisitionFactory.Tenant}/careers/openings/{opening}/applications", form, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        var normalized = Candidate.NormalizeEmail(email);
        var application = await WithDbAsync(db => db.JobApplications
            .Where(a => a.OpeningId == JobOpeningId.From(opening))
            .Join(db.Candidates.Where(c => c.Email == normalized), a => a.CandidateId, c => c.Id, (a, _) => a.Id.Value)
            .SingleAsync(Ct));
        return (application, email);
    }

    private Task<int> MoveAsync(Guid interview, DateTimeOffset start) =>
        WithDbAsync(db => db.Interviews.Where(i => i.Id == InterviewId.From(interview))
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.StartsAt, start).SetProperty(i => i.EndsAt, start.AddHours(1)), Ct));

    private async Task<int> RemindAsync()
    {
        var tenant = (await factory.Services.GetRequiredService<ITenantStore>().FindByCodeAsync(RequisitionFactory.Tenant, Ct))!;
        await using var scope = factory.Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        return (await scope.ServiceProvider.GetRequiredService<ISender>().Send(new SendInterviewRemindersCommand(), Ct)).Value;
    }

    private async Task<T> WithDbAsync<T>(Func<RecruitmentDbContext, Task<T>> work)
    {
        var tenant = (await factory.Services.GetRequiredService<ITenantStore>().FindByCodeAsync(RequisitionFactory.Tenant, Ct))!;
        await using var scope = factory.Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        return await work(scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>());
    }

    private async Task<T> PlatformAsync<T>(Func<PlatformDbContext, Task<T>> work)
    {
        var tenant = (await factory.Services.GetRequiredService<ITenantStore>().FindByCodeAsync(RequisitionFactory.Tenant, Ct))!;
        await using var scope = factory.Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        return await work(scope.ServiceProvider.GetRequiredService<PlatformDbContext>());
    }

    private static async Task<Dictionary<string, string[]>> ProblemAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync(Ct));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return json.RootElement.GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;
    }

    private sealed record InterviewBody(
        Guid ApplicationId,
        string Type,
        DateTimeOffset StartsAt,
        DateTimeOffset EndsAt,
        string? Location,
        string? OnlineLink,
        IReadOnlyList<Guid> InterviewerUserIds,
        string? Notes,
        Guid? EvaluationTemplateId)
    {
        public uint Version { get; init; }
    }
}
