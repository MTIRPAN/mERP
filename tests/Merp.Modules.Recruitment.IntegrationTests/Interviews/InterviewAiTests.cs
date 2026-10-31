using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Merp.Application.Abstractions.Security;
using Merp.Infrastructure.AI;
using Merp.Infrastructure.MultiTenancy;
using Merp.Modules.Platform.Contracts.Notifications;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Application.Applications;
using Merp.Modules.Recruitment.Application.Interviews;
using Merp.Modules.Recruitment.Application.Settings;
using Merp.Modules.Recruitment.Domain.Candidates;
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
/// AI around interviews (PLAN A5, D58): questions from the opening and the form, a summary of the forms only after
/// every interviewer sent theirs, and candidate e-mails personalized without the candidate's name reaching the
/// provider and sent only with the rejection or the interview.
/// </summary>
public sealed class InterviewAiTests : IClassFixture<RequisitionFactory>
{
    private const string Url = "/api/recruitment/interviews";

    private static readonly string[] Permissions =
    [
        RecruitmentPermissions.Interviews.Read,
        RecruitmentPermissions.Interviews.Manage,
        RecruitmentPermissions.Feedback.Submit,
        RecruitmentPermissions.Applications.Read,
        RecruitmentPermissions.Applications.Manage,
        RecruitmentPermissions.Settings.Read,
        RecruitmentPermissions.Settings.Manage,
        "platform.companies.read",
        "platform.companies.manage",
    ];

    private readonly RequisitionFactory _factory;

    public InterviewAiTests(RequisitionFactory factory)
    {
        _factory = factory;
        _factory.Ai.Reset();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Questions_ComeFromTheOpeningAndTheForm_WithoutTheCandidate()
    {
        var (company, manager, _) = await SetupAsync();
        var (application, email) = await ApplicationAsync(company, manager);
        var (ayse, ayseSubject) = await UserAsync();
        var (_, strangerSubject) = await UserAsync();
        var id = await ScheduleAsync(application, await TemplateAsync(), ayse);
        var criteria = (await DetailsAsync(Recruiter(), id)).Evaluation!.Criteria.ToList();
        _factory.Ai.Returns(new InterviewQuestionSet(
        [
            new InterviewQuestionDraft("", "Bu rolü neden istiyorsunuz?", "", []),
            new InterviewQuestionDraft("C1", "Zor bir hatayı nasıl çözdüğünüzü anlatır mısınız?", "Sorunu adım adım ayırır.", ["Sonuç ne oldu?", "Neyi farklı yapardınız?", "Fazladan"]),
        ]));

        var response = await Interviewer(ayseSubject).PostAsync($"{Url}/{id}/question-suggestions", null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var suggested = (await response.Content.ReadFromJsonAsync<InterviewQuestionsResponse>(AiSchema.SerializerOptions, Ct))!;
        suggested.Questions.ShouldSatisfyAllConditions(
            q => q.Count.ShouldBe(2),
            q => q[0].ShouldSatisfyAllConditions(i => i.CriterionId.ShouldBe(criteria[0].Id), i => i.Criterion.ShouldBe("Problem çözme"), i => i.FollowUps.Count.ShouldBe(2)),
            q => q[1].ShouldSatisfyAllConditions(i => i.CriterionId.ShouldBeNull(), i => i.LookFor.ShouldBeNull()));
        var sent = _factory.Ai.Requests.ShouldHaveSingleItem();
        sent.Feature.ShouldBe(RecruitmentAiFeatures.InterviewQuestions);
        sent.Instructions.ShouldBe(InterviewQuestionPrompt.Instructions);
        sent.Input.ShouldSatisfyAllConditions(
            i => i.ShouldContain("<title>Yazılım Geliştirici</title>"),
            i => i.ShouldContain("<criterion key=\"C1\">\n<name>Problem çözme</name>"),
            i => i.ShouldNotContain("Ayşe"),
            i => i.ShouldNotContain("Yılmaz"),
            i => i.ShouldNotContain(email));

        // Who does not see the interview gets nothing; without AI the feature is refused
        (await Interviewer(strangerSubject).PostAsync($"{Url}/{id}/question-suggestions", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _factory.CreateClientFor("questions-outsider", RequisitionFactory.Tenant, RecruitmentPermissions.Openings.Read).PostAsync($"{Url}/{id}/question-suggestions", null, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        _factory.Ai.IsConfigured = false;
        var disabled = await Interviewer(ayseSubject).PostAsync($"{Url}/{id}/question-suggestions", null, Ct);
        disabled.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(disabled)).ShouldBe("common.errors.ai_disabled");
        _factory.Ai.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task TheSummary_WaitsForEveryForm_AndLeavesNamesOut()
    {
        // PLAN A5: the summary follows the blind rule, it shows after everyone sent their form
        var (company, manager, managerSubject) = await SetupAsync();
        var (application, _) = await ApplicationAsync(company, manager);
        var (ayse, ayseSubject) = await UserAsync();
        var (mehmet, mehmetSubject) = await UserAsync();
        var (_, strangerSubject) = await UserAsync();
        var id = await ScheduleAsync(application, await TemplateAsync(), ayse, mehmet);
        var summaryUrl = $"/api/recruitment/applications/{application}/feedback-summary";

        var none = await Recruiter().PostAsync(summaryUrl, null, Ct);
        none.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(none)).ShouldBe("recruitment.interviews.errors.feedback_pending");

        await SendFormAsync(ayseSubject, id, 5, "Ayşe Yılmaz problemi hızla çözdü, ayse.yilmaz@ornek.test");
        (await DetailsAsync(Recruiter(), id)).FeedbackPending.ShouldBe(1);
        (await Recruiter().PostAsync(summaryUrl, null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await SendFormAsync(mehmetSubject, id, 2, "İletişimi zayıftı");
        var details = await DetailsAsync(Recruiter(), id);
        details.FeedbackPending.ShouldBe(0);
        var names = details.Interview.Interviewers.ToDictionary(p => p.UserId, p => p.Name);
        _factory.Ai.Returns(Summary());

        var response = await Recruiter().PostAsync(summaryUrl, null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var summary = (await response.Content.ReadFromJsonAsync<FeedbackSummaryResponse>(AiSchema.SerializerOptions, Ct))!;
        summary.ShouldSatisfyAllConditions(
            s => s.FormCount.ShouldBe(2),
            s => s.Strengths.ShouldHaveSingleItem().Interviewers.ShouldBe([names[ayse]!, names[mehmet]!]),
            s => s.Concerns.ShouldHaveSingleItem().Interviewers.ShouldBe([names[mehmet]!]),
            s => s.Disagreements.ShouldBe(["İletişim puanlarında ayrışma var."]),
            s => s.OpenQuestions.ShouldBeEmpty(),
            s => s.PromptVersion.ShouldBe(FeedbackSummaryPrompt.Version));
        var sent = _factory.Ai.Requests.ShouldHaveSingleItem();
        sent.Instructions.ShouldBe(FeedbackSummaryPrompt.Instructions);
        sent.Input.ShouldSatisfyAllConditions(
            i => i.ShouldContain("<form interviewer=\"I1\""),
            i => i.ShouldContain("[NAME] [NAME] problemi hızla çözdü"),
            i => i.ShouldContain("<form interviewer=\"I2\""),
            i => i.ShouldNotContain("Ayşe"),
            i => i.ShouldNotContain("ayse.yilmaz@ornek.test"),
            i => i.ShouldNotContain(names[ayse]!),
            i => i.ShouldNotContain(names[mehmet]!));

        // The interviewers and the hiring manager see it too; others do not
        _factory.Ai.Returns(Summary());
        (await Interviewer(ayseSubject).PostAsync($"{Url}/{id}/feedback-summary", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        _factory.Ai.Returns(Summary());
        var team = _factory.CreateClientFor(managerSubject, RequisitionFactory.Tenant, RecruitmentPermissions.Self.Hiring);
        (await team.PostAsync($"/api/recruitment/me/hiring/applications/{application}/feedback-summary", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Interviewer(strangerSubject).PostAsync($"{Url}/{id}/feedback-summary", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var scoped = _factory.CreateClientFor("summary-scoped", RequisitionFactory.Tenant, Permissions).WithScopes(AccessScope.Company(Guid.CreateVersion7()));
        (await scoped.PostAsync(summaryUrl, null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _factory.CreateClientFor("summary-outsider", RequisitionFactory.Tenant, RecruitmentPermissions.Openings.Read).PostAsync(summaryUrl, null, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        _factory.Ai.IsConfigured = false;
        (await Recruiter().PostAsync(summaryUrl, null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        _factory.Ai.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task ARejectionEmail_IsPersonalizedWithoutTheCandidate_AndGoesWithTheRejection()
    {
        var (company, manager, _) = await SetupAsync();
        var (application, email) = await ApplicationAsync(company, manager);
        _factory.Ai.Returns(new CandidateEmailDraftResult("{position} başvurunuz hakkında", "Sayın {candidate},\n\nSunumunuz için teşekkür ederiz."));

        var response = await Recruiter().PostAsJsonAsync(
            $"/api/recruitment/applications/{application}/rejection-email-draft",
            new { instructions = "Ayşe Yılmaz'ın sunumunu takdir ettiğimizi yazın; dönüş için ik@acme.example" },
            Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var draft = (await response.Content.ReadFromJsonAsync<CandidateEmailDraft>(Ct))!;
        draft.ShouldSatisfyAllConditions(
            d => d.Subject.ShouldBe("{position} başvurunuz hakkında"),
            d => d.Placeholders.ShouldBe(["candidate", "position", "company"]));
        var sent = _factory.Ai.Requests.ShouldHaveSingleItem();
        sent.Feature.ShouldBe(RecruitmentAiFeatures.CandidateEmail);
        sent.Input.ShouldSatisfyAllConditions(
            i => i.ShouldContain("<email_kind>Rejection</email_kind>"),
            i => i.ShouldContain("Sayın {candidate}, {position} pozisyonuna"),
            i => i.ShouldContain("sunumunu takdir ettiğimizi yazın"),
            i => i.ShouldNotContain("Ayşe"),
            i => i.ShouldNotContain("ik@acme.example"),
            i => i.ShouldNotContain(email));
        (await CandidateEmailsAsync(email)).ShouldBeEmpty();

        // The edited text goes with the rejection, with its placeholders for Platform to fill
        var version = (await ApplicationDetailsAsync(application)).Version;
        var rejected = await Recruiter().PostAsJsonAsync(
            $"/api/recruitment/applications/{application}/reject",
            new { version, reasonCode = "EXPERIENCE", sendEmail = true, emailDelayDays = 0, emailSubject = draft.Subject, emailBody = draft.Body + " Başarılar." },
            Ct);
        rejected.StatusCode.ShouldBe(HttpStatusCode.NoContent, await rejected.Content.ReadAsStringAsync(Ct));
        var rejection = (await CandidateEmailsAsync(email)).ShouldHaveSingleItem();
        rejection.ShouldSatisfyAllConditions(
            e => e.Type.ShouldBe(RecruitmentNotificationTypes.ApplicationRejected),
            e => e.Subject.ShouldBe("{position} başvurunuz hakkında"),
            e => e.Body.ShouldBe("Sayın {candidate},\n\nSunumunuz için teşekkür ederiz. Başarılar."),
            e => e.Args["candidate"].ShouldBe("Ayşe Yılmaz"));

        // A later e-mail keeps its text until it goes
        var (later, _) = await ApplicationAsync(company, manager);
        var laterVersion = (await ApplicationDetailsAsync(later)).Version;
        (await Recruiter().PostAsJsonAsync(
                $"/api/recruitment/applications/{later}/reject",
                new { version = laterVersion, reasonCode = "EXPERIENCE", sendEmail = true, emailDelayDays = 3, emailSubject = "Başvurunuz", emailBody = "Sayın {candidate}, teşekkürler." },
                Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ApplicationDetailsAsync(later)).RejectionEmailPersonalized.ShouldBeTrue();
    }

    [Fact]
    public async Task AnInvitation_IsPersonalized_AndGoesWithTheInterview()
    {
        var (company, manager, _) = await SetupAsync();
        var (application, email) = await ApplicationAsync(company, manager);
        var (ayse, _) = await UserAsync();
        _factory.Ai.Returns(new CandidateEmailDraftResult("{position} mülakat daveti", "Sayın {candidate},\n\nSizi {date} {time} tarihinde görüntülü mülakata bekliyoruz: {location}"));

        var response = await Recruiter().PostAsJsonAsync($"/api/recruitment/applications/{application}/invitation-email-draft", new { interviewType = "Video" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var draft = (await response.Content.ReadFromJsonAsync<CandidateEmailDraft>(Ct))!;
        draft.Placeholders.ShouldBe(["candidate", "position", "company", "date", "time", "location"]);
        _factory.Ai.Requests.ShouldHaveSingleItem().Input.ShouldSatisfyAllConditions(
            i => i.ShouldContain("<email_kind>InterviewInvitation</email_kind>"),
            i => i.ShouldContain("<interview_type>Video</interview_type>"),
            i => i.ShouldNotContain("Ayşe"));

        var start = NextWeek(10);
        var unknown = await Recruiter().PostAsJsonAsync(Url, Body(application, start, ayse) with { InvitationSubject = draft.Subject, InvitationBody = "Sayın {interviewer}" }, Ct);
        (await ProblemAsync(unknown)).ShouldContainKey("invitationBody");

        (await Recruiter().PostAsJsonAsync(Url, Body(application, start, ayse) with { InvitationSubject = draft.Subject, InvitationBody = draft.Body }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        var invitation = (await CandidateEmailsAsync(email)).ShouldHaveSingleItem();
        invitation.ShouldSatisfyAllConditions(
            e => e.Type.ShouldBe(RecruitmentNotificationTypes.InterviewInvitation),
            e => e.Subject.ShouldBe(draft.Subject),
            e => e.Body.ShouldBe(draft.Body),
            e => e.Attachments.ShouldHaveSingleItem().FileName.ShouldBe("interview.ics"));
    }

    [Fact]
    public async Task EmailDrafts_AreChecked_AndOnlyForThoseWhoManage()
    {
        var (company, manager, _) = await SetupAsync();
        var (application, _) = await ApplicationAsync(company, manager);
        var draftUrl = $"/api/recruitment/applications/{application}/rejection-email-draft";

        (await ProblemAsync(await Recruiter().PostAsJsonAsync(draftUrl, new { instructions = new string('a', DraftCandidateEmailQuery.InstructionsMaxLength + 1) }, Ct)))
            .ShouldContainKey("instructions");
        (await ProblemAsync(await Recruiter().PostAsJsonAsync($"/api/recruitment/applications/{application}/invitation-email-draft", new { }, Ct)))
            .ShouldContainKey("interviewType");
        var version = (await ApplicationDetailsAsync(application)).Version;
        var unknown = await Recruiter().PostAsJsonAsync(
            $"/api/recruitment/applications/{application}/reject",
            new { version, reasonCode = "EXPERIENCE", sendEmail = true, emailDelayDays = 0, emailSubject = "Başvurunuz", emailBody = "Mülakatınız {date} tarihindeydi." },
            Ct);
        (await ProblemAsync(unknown)).ShouldContainKey("emailBody");

        var reader = _factory.CreateClientFor("email-reader", RequisitionFactory.Tenant, RecruitmentPermissions.Applications.Read);
        (await reader.PostAsJsonAsync(draftUrl, new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var scoped = _factory.CreateClientFor("email-scoped", RequisitionFactory.Tenant, Permissions).WithScopes(AccessScope.Company(Guid.CreateVersion7()));
        (await scoped.PostAsJsonAsync(draftUrl, new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        _factory.Ai.IsConfigured = false;
        var disabled = await Recruiter().PostAsJsonAsync(draftUrl, new { }, Ct);
        disabled.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(disabled)).ShouldBe("common.errors.ai_disabled");
        _factory.Ai.Requests.ShouldBeEmpty();
    }

    private static FeedbackSummaryResult Summary() => new(
        "Mülakatçılar analitik beceride hemfikir, iletişimde ayrışıyor.",
        [new FeedbackSummaryPointDraft("Analitik düşünce güçlü.", ["I1", "I2", "I9"])],
        [new FeedbackSummaryPointDraft("İletişim gelişmeli.", ["I2"])],
        ["İletişim puanlarında ayrışma var."],
        []);

    private HttpClient Recruiter() => _factory.CreateClientFor("interview-ai-recruiter", RequisitionFactory.Tenant, Permissions);

    /// <summary>A user with the Interviewer role: its permissions with the Self scope.</summary>
    private HttpClient Interviewer(string subject) =>
        _factory.CreateClientFor(subject, RequisitionFactory.Tenant, RecruitmentPermissions.Interviews.Read, RecruitmentPermissions.Feedback.Submit)
            .WithScopes(new AccessScope(AccessScopeTypes.Self, null));

    private static DateTimeOffset NextWeek(int hour) => new DateTimeOffset(DateTimeOffset.UtcNow.Date.AddDays(7), TimeSpan.Zero).AddHours(hour);

    private static InterviewBody Body(Guid application, DateTimeOffset start, params Guid[] interviewers) =>
        new(application, "Video", start, start.AddHours(1), null, "https://meet.example/abc", interviewers, null, null);

    private async Task<Guid> ScheduleAsync(Guid application, Guid template, params Guid[] interviewers) =>
        await RequisitionFactory.CreatedIdAsync(await Recruiter().PostAsJsonAsync(Url, Body(application, NextWeek(9), interviewers) with { Type = "Technical", EvaluationTemplateId = template }, Ct));

    /// <summary>An interviewer's form: both criteria with the score, the comment on the first.</summary>
    private async Task SendFormAsync(string subject, Guid interview, int score, string comment)
    {
        var client = Interviewer(subject);
        var form = (await DetailsAsync(client, interview)).Evaluation!;
        var criteria = form.Criteria.ToList();
        var response = await client.PutAsJsonAsync(
            $"{Url}/{interview}/feedback",
            new
            {
                scores = new[] { new { criterionId = criteria[0].Id, score, comment = (string?)comment }, new { criterionId = criteria[1].Id, score, comment = (string?)null } },
                recommendation = score >= 3 ? "Yes" : "No",
            },
            Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    private async Task<Guid> TemplateAsync() =>
        await RequisitionFactory.CreatedIdAsync(await Recruiter().PostAsJsonAsync(
            "/api/recruitment/settings/evaluation-templates",
            new
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
                                new { id = Guid.Empty, name = "Problem çözme", scoreDescriptions = new[] { "Yetersiz", "Zayıf", "Yeterli", "İyi", "Çok iyi" }, weight = 3m },
                                new { id = Guid.Empty, name = "İletişim", scoreDescriptions = new[] { "", "", "", "", "" }, weight = 1m },
                            },
                        },
                    },
                    questions = new[] { new { id = Guid.Empty, text = "En güçlü yanı ne?" } },
                },
            },
            Ct));

    private async Task<List<ExternalEmailRequested>> CandidateEmailsAsync(string email) =>
        [.. (await _factory.OutboxAsync<ExternalEmailRequested>()).Where(e => e.To == email && e.Type != RecruitmentNotificationTypes.ApplicationReceived)];

    private static async Task<InterviewDetails> DetailsAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<InterviewDetails>($"{Url}/{id}", Ct))!;

    private async Task<ApplicationDetails> ApplicationDetailsAsync(Guid id) =>
        (await Recruiter().GetFromJsonAsync<ApplicationDetails>($"/api/recruitment/applications/{id}", Ct))!;

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
        using var me = JsonDocument.Parse(await _factory.CreateClientFor(subject, RequisitionFactory.Tenant).GetStringAsync("/api/platform/me", Ct));
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
        var response = await _factory.CreateClient().PostAsync($"/api/public/{RequisitionFactory.Tenant}/careers/openings/{opening}/applications", form, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        var normalized = Candidate.NormalizeEmail(email);
        var application = await WithDbAsync(db => db.JobApplications
            .Where(a => a.OpeningId == JobOpeningId.From(opening))
            .Join(db.Candidates.Where(c => c.Email == normalized), a => a.CandidateId, c => c.Id, (a, _) => a.Id.Value)
            .SingleAsync(Ct));
        return (application, email);
    }

    private async Task<T> WithDbAsync<T>(Func<RecruitmentDbContext, Task<T>> work)
    {
        var tenant = (await _factory.Services.GetRequiredService<ITenantStore>().FindByCodeAsync(RequisitionFactory.Tenant, Ct))!;
        await using var scope = _factory.Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        return await work(scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>());
    }

    private static async Task<Dictionary<string, string[]>> ProblemAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync(Ct));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return json.RootElement.GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return json.RootElement.GetProperty("code").GetString();
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
        public string? InvitationSubject { get; init; }

        public string? InvitationBody { get; init; }
    }
}
