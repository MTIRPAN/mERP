using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Merp.Infrastructure.MultiTenancy;
using Merp.Modules.HR.Application;
using Merp.Modules.Platform.Api.Endpoints;
using Merp.Modules.Platform.Contracts.Notifications;
using Merp.Modules.Platform.Infrastructure.Persistence;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Application.Careers;
using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Candidates;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.Modules.Recruitment.Infrastructure.Persistence;
using Merp.Modules.Recruitment.IntegrationTests.Requisitions;
using Merp.Testing.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Recruitment.IntegrationTests.Careers;

/// <summary>
/// The career page and applications (PLAN R3b, D49): anonymous candidates apply with a CV and the KVKK consent, the
/// candidate is one per e-mail address, knockout answers mark the application; employees apply from self service.
/// </summary>
public sealed class CareerTests(RequisitionFactory factory) : IClassFixture<RequisitionFactory>
{
    private const string Careers = "/api/public/acme/careers";

    private static readonly Guid MilitaryQuestion = Guid.CreateVersion7();
    private static readonly Guid CityQuestion = Guid.CreateVersion7();

    private static readonly string[] OrgPermissions =
    [
        HrPermissions.Org.Read,
        HrPermissions.Org.Manage,
        HrPermissions.Employees.Read,
        HrPermissions.Employees.Create,
        HrPermissions.Employees.Update,
        HrPermissions.Employees.User,
        HrPermissions.Employees.Sensitive.Read,
        HrPermissions.Employees.Sensitive.Update,
        "platform.companies.read",
        "platform.companies.manage",
    ];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCandidate_AppliesWithACv_AndGetsTheReceivedEmail()
    {
        // PLAN R3 acceptance: candidate and application are created and "başvurunuz alındı" is queued
        var company = await CompanyAsync();
        var opening = await OpeningAsync(company, OpeningVisibility.Public);
        var visitor = factory.CreateClient();

        var page = (await visitor.GetFromJsonAsync<CareerPage>(Careers, Ct))!;
        page.Openings.ShouldContain(o => o.Id == opening && o.CompanyName == "Acme" && o.WorkModel == WorkModel.Hybrid);
        page.Companies.ShouldContain(c => c.Id == company && !c.HasLogo);
        var details = (await visitor.GetFromJsonAsync<CareerOpeningDetails>($"{Careers}/openings/{opening}", Ct))!;
        details.ShouldSatisfyAllConditions(
            d => d.Form.Questions.Count.ShouldBe(2),
            d => d.Form.RequireCv.ShouldBeTrue(),
            d => d.Form.CvExtensions.ShouldBe(["docx", "pdf"]),
            d => d.Disclosure.Revision.ShouldBe(0),
            d => d.Disclosure.Body.ShouldNotBeNullOrWhiteSpace());
        (await visitor.GetStringAsync($"{Careers}/openings/{opening}", Ct)).ShouldNotContain("acceptedAnswers", Case.Insensitive);

        var email = NewEmail();
        var response = await ApplyAsync(visitor, opening, Application(email) with { PoolConsent = true, PoolConsentRevision = 0 });
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));

        var (candidate, application) = await StoredAsync(email, opening);
        candidate.ShouldSatisfyAllConditions(
            c => c.FirstName.ShouldBe("Ayşe"),
            c => c.Phone.ShouldBe("05321234567"),
            c => c.Source.ShouldBe("CAREER_PAGE"),
            c => c.CvFileId.ShouldNotBeNull(),
            c => c.PoolConsentRevision.ShouldBe(0));
        application.ShouldSatisfyAllConditions(
            a => a.Status.ShouldBe(ApplicationStatus.Active),
            a => a.KnockedOut.ShouldBeFalse(),
            a => a.IsInternal.ShouldBeFalse(),
            a => a.CvFileId.ShouldBe(candidate.CvFileId),
            a => a.Consent.DisclosureRevision.ShouldBe(0),
            a => a.Answers.Count.ShouldBe(2));
        (await factory.OutboxAsync<ExternalEmailRequested>()).ShouldContain(e =>
            e.To == email && e.Type == "recruitment.application_received" && e.Args["position"] == "Satış Uzmanı" && e.Args["company"] == "Acme");

        await factory.DeliverMessagesAsync();
        var queued = await PlatformAsync(db => db.EmailMessages.AsNoTracking().SingleAsync(m => m.To == email, Ct));
        queued.Subject.ShouldBe("Satış Uzmanı başvurunuz alındı");
    }

    [Fact]
    public async Task AnApplicationWithoutConsent_OrAgainstAnOldText_IsRefused()
    {
        // PLAN R3 acceptance: an application without the consent box is refused
        var opening = await OpeningAsync(await CompanyAsync(), OpeningVisibility.Public);
        var visitor = factory.CreateClient();
        var email = NewEmail();

        var withoutConsent = await ApplyAsync(visitor, opening, Application(email) with { DisclosureAccepted = false });
        withoutConsent.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemAsync(withoutConsent)).ShouldContainKey("disclosureAccepted");

        var oldText = await ApplyAsync(visitor, opening, Application(email) with { DisclosureRevision = 3 });
        oldText.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemAsync(oldText)).ShouldContainKey("disclosureRevision");

        (await CandidateAsync(email)).ShouldBeNull();
    }

    [Fact]
    public async Task TheSameEmail_AppliesOncePerOpening_AndIsOneCandidate()
    {
        var company = await CompanyAsync();
        var first = await OpeningAsync(company, OpeningVisibility.Public);
        var second = await OpeningAsync(company, OpeningVisibility.Public);
        var visitor = factory.CreateClient();
        var email = NewEmail();

        (await ApplyAsync(visitor, first, Application(email))).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var again = await ApplyAsync(visitor, first, Application(email.ToUpperInvariant()));
        again.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemAsync(again)).ShouldContainKey("email");

        (await ApplyAsync(visitor, second, Application(email) with { Phone = "05559876543" })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var candidate = (await CandidateAsync(email))!;
        candidate.Phone.ShouldBe("05559876543");
        (await RecruitmentAsync(db => db.JobApplications.CountAsync(a => a.CandidateId == candidate.Id, Ct))).ShouldBe(2);
    }

    [Fact]
    public async Task AKnockoutAnswer_MarksTheApplication_AndTheFormIsChecked()
    {
        var opening = await OpeningAsync(await CompanyAsync(), OpeningVisibility.Public);
        var visitor = factory.CreateClient();

        var missing = await ApplyAsync(visitor, opening, Application(NewEmail()) with { Answers = [new(CityQuestion, ["Ankara"])] }, cv: null);
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = await ProblemAsync(missing);
        errors.ShouldContainKey($"answers.{MilitaryQuestion}");
        errors.ShouldContainKey("cv");

        var invalid = await ApplyAsync(visitor, opening, Application(NewEmail()) with { Answers = [new(MilitaryQuestion, ["yes"]), new(CityQuestion, ["İzmir"])] });
        (await ProblemAsync(invalid)).ShouldContainKey($"answers.{CityQuestion}");

        var email = NewEmail();
        (await ApplyAsync(visitor, opening, Application(email) with { Answers = [new(MilitaryQuestion, ["no"])] })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await StoredAsync(email, opening)).Application.KnockedOut.ShouldBeTrue();

        var text = await ApplyAsync(visitor, opening, Application(NewEmail()), ("cv.txt", "text/plain", "Özgeçmiş"u8.ToArray()));
        text.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemAsync(text)).ShouldContainKey("cv");
    }

    [Fact]
    public async Task OnlyOpenPublicOpenings_TakeAnonymousApplications_AndBotsAreDropped()
    {
        var company = await CompanyAsync();
        var internalOnly = await OpeningAsync(company, OpeningVisibility.Internal);
        var draft = await OpeningAsync(company, OpeningVisibility.Public, publish: false);
        var open = await OpeningAsync(company, OpeningVisibility.Public);
        var visitor = factory.CreateClient();

        var page = (await visitor.GetFromJsonAsync<CareerPage>(Careers, Ct))!;
        page.Openings.ShouldNotContain(o => o.Id == internalOnly || o.Id == draft);
        (await visitor.GetAsync($"{Careers}/openings/{internalOnly}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ApplyAsync(visitor, draft, Application(NewEmail()))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await visitor.GetAsync("/api/public/nosuchtenant/careers", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await visitor.GetAsync($"{Careers}/companies/{company}/logo", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var bot = NewEmail();
        (await ApplyAsync(visitor, open, Application(bot) with { Website = "http://spam.example" })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await CandidateAsync(bot)).ShouldBeNull();
    }

    [Fact]
    public async Task AnEmployee_AppliesFromSelfService_AsAnInternalCandidate()
    {
        var company = await CompanyAsync();
        var internalOpening = await OpeningAsync(company, OpeningVisibility.Internal);
        var person = await EmployeeAsync(company);
        var employee = factory.CreateClientFor(person.Subject, RequisitionFactory.Tenant, RecruitmentPermissions.Self.Apply);

        var openings = (await employee.GetFromJsonAsync<List<CareerOpeningItem>>("/api/recruitment/me/openings", Ct))!;
        openings.ShouldContain(o => o.Id == internalOpening && o.Visibility == OpeningVisibility.Internal);
        var details = (await employee.GetFromJsonAsync<CareerOpeningDetails>($"/api/recruitment/me/openings/{internalOpening}", Ct))!;

        var body = new
        {
            phone = "05321112233",
            answers = new[] { new ApplicationAnswerInput(MilitaryQuestion, ["yes"]) },
            disclosureAccepted = true,
            disclosureRevision = details.Disclosure.Revision,
            poolConsent = false,
        };
        var response = await PostFormAsync(employee, $"/api/recruitment/me/openings/{internalOpening}/applications", body, Pdf());
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));

        var mine = (await employee.GetFromJsonAsync<List<MyApplicationItem>>("/api/recruitment/me/applications", Ct))!;
        mine.ShouldHaveSingleItem().ShouldSatisfyAllConditions(a => a.OpeningId.ShouldBe(internalOpening), a => a.CompanyName.ShouldBe("Acme"));
        var candidate = (await RecruitmentAsync(db => db.Candidates.AsNoTracking().SingleAsync(c => c.EmployeeId == person.Employee, Ct)))!;
        candidate.ShouldSatisfyAllConditions(c => c.FirstName.ShouldBe("Ayşe"), c => c.Source.ShouldBe("INTERNAL"), c => c.Email.ShouldBe(person.Email));
        (await StoredAsync(person.Email, internalOpening)).Application.IsInternal.ShouldBeTrue();

        // A user without a personnel record, and a user without the permission
        var stranger = factory.CreateClientFor("kayitsiz", RequisitionFactory.Tenant, RecruitmentPermissions.Self.Apply);
        var notEmployee = await PostFormAsync(stranger, $"/api/recruitment/me/openings/{internalOpening}/applications", body, Pdf());
        notEmployee.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await factory.CreateClientFor(person.Subject, RequisitionFactory.Tenant).GetAsync("/api/recruitment/me/openings", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static string NewEmail() => $"aday.{Guid.NewGuid().ToString("N")[..10]}@ornek.test";

    private static ApplicationBody Application(string email) => new(
        "Ayşe",
        "Yılmaz",
        email,
        "05321234567",
        null,
        [new(MilitaryQuestion, ["yes"]), new(CityQuestion, ["İstanbul"])],
        DisclosureAccepted: true,
        DisclosureRevision: 0,
        PoolConsent: false,
        PoolConsentRevision: null,
        Culture: "tr-TR",
        Website: null);

    private static (string Name, string ContentType, byte[] Content) Pdf() => ("ozgecmis.pdf", "application/pdf", "%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF"u8.ToArray());

    private static Task<HttpResponseMessage> ApplyAsync(HttpClient client, Guid opening, ApplicationBody body) => ApplyAsync(client, opening, body, Pdf());

    private static Task<HttpResponseMessage> ApplyAsync(HttpClient client, Guid opening, ApplicationBody body, (string Name, string ContentType, byte[] Content)? cv) =>
        PostFormAsync(client, $"{Careers}/openings/{opening}/applications", body, cv);

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string url, object body, (string Name, string ContentType, byte[] Content)? cv)
    {
        using var form = new MultipartFormDataContent { { new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), Encoding.UTF8), "application" } };
        if (cv is { } file)
        {
            var content = new ByteArrayContent(file.Content);
            content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
            form.Add(content, "cv", file.Name);
        }

        return await client.PostAsync(url, form, Ct);
    }

    private async Task<Guid> CompanyAsync()
    {
        var admin = factory.CreateClientFor("admin", RequisitionFactory.Tenant, OrgPermissions);
        return await RequisitionFactory.CreatedIdAsync(await admin.PostAsJsonAsync(
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
    }

    /// <summary>An opening with a knockout question (military service: yes) and a city choice, published unless asked otherwise.</summary>
    private Task<Guid> OpeningAsync(Guid company, OpeningVisibility visibility, bool publish = true) =>
        RecruitmentAsync(async db =>
        {
            var template = await db.StageTemplates.SingleAsync(t => t.IsDefault, Ct);
            var form = new ApplicationForm(
                RequirePhone: true,
                RequireCv: true,
                AskCoverLetter: false,
                [
                    new OpeningQuestion(MilitaryQuestion, "Askerlik görevinizi tamamladınız mı?", QuestionType.YesNo, true, [], [OpeningQuestion.Yes]),
                    new OpeningQuestion(CityQuestion, "Hangi şehirde çalışmak istersiniz?", QuestionType.SingleChoice, false, ["İstanbul", "Ankara"], null),
                ]);
            var opening = JobOpening.Draft(
                new OpeningTerms(
                    company,
                    null,
                    "Satış Uzmanı",
                    "<p>Satış ekibimize katılacak arkadaş arıyoruz.</p>",
                    null,
                    null,
                    WorkModel.Hybrid,
                    RequisitionEmploymentType.Indefinite,
                    1,
                    visibility,
                    null,
                    null,
                    new OpeningTeam(Guid.CreateVersion7(), [], []),
                    form),
                template);
            if (publish)
            {
                opening.Publish(DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime.AddDays(-1)), DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();
            }

            db.JobOpenings.Add(opening);
            await db.SaveChangesAsync(Ct);
            return opening.Id.Value;
        });

    /// <summary>A person hired into the company, with a user linked to the personnel record.</summary>
    private async Task<Person> EmployeeAsync(Guid company)
    {
        var admin = factory.CreateClientFor("admin", RequisitionFactory.Tenant, OrgPermissions);
        var unit = await RequisitionFactory.CreatedIdAsync(await admin.PostAsJsonAsync(
            "/api/hr/org-units",
            new { companyId = company, code = NewCode("U"), name = "Satış Direktörlüğü", type = "Directorate" },
            Ct));
        var job = await RequisitionFactory.CreatedIdAsync(await admin.PostAsJsonAsync("/api/hr/jobs", new { code = NewCode("J"), name = "Satış Uzmanı", family = "Satış", level = "U2" }, Ct));
        var position = await RequisitionFactory.CreatedIdAsync(await admin.PostAsJsonAsync("/api/hr/positions", new { orgUnitId = unit, jobId = job, code = NewCode("P") }, Ct));
        var employee = await RequisitionFactory.CreatedIdAsync(await admin.PostAsJsonAsync(
            "/api/hr/employees",
            new
            {
                tckn = TurkishData.Tckn(),
                firstName = "Ayşe",
                lastName = "Yılmaz",
                companyId = company,
                hireDate = new DateOnly(2025, 1, 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                employmentType = "Indefinite",
                positionId = position,
            },
            Ct));
        var email = $"personel.{Guid.NewGuid().ToString("N")[..10]}@acme.test";
        (await admin.PostAsJsonAsync($"/api/hr/employees/{employee}/user", new { email }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await factory.DeliverMessagesAsync();
        var subject = factory.Identity.Identities.Single(i => i.Value.Email == email).Key;
        (await factory.CreateClientFor(subject, RequisitionFactory.Tenant).GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!.User.Id.ShouldNotBeNull();
        return new Person(employee, subject, email);
    }

    private async Task<Candidate?> CandidateAsync(string email)
    {
        var normalized = Candidate.NormalizeEmail(email);
        return await RecruitmentAsync(db => db.Candidates.AsNoTracking().SingleOrDefaultAsync(c => c.Email == normalized, Ct));
    }

    private async Task<(Candidate Candidate, JobApplication Application)> StoredAsync(string email, Guid opening)
    {
        var candidate = (await CandidateAsync(email)).ShouldNotBeNull();
        var openingId = JobOpeningId.From(opening);
        var application = await RecruitmentAsync(db => db.JobApplications.AsNoTracking().SingleAsync(a => a.CandidateId == candidate.Id && a.OpeningId == openingId, Ct));
        return (candidate, application);
    }

    private async Task<T> RecruitmentAsync<T>(Func<RecruitmentDbContext, Task<T>> work)
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
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return json.RootElement.GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;
    }

    private static string NewCode(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private sealed record Person(Guid Employee, string Subject, string Email);

    private sealed record ApplicationBody(
        string FirstName,
        string LastName,
        string Email,
        string? Phone,
        string? CoverLetter,
        IReadOnlyList<ApplicationAnswerInput> Answers,
        bool DisclosureAccepted,
        int DisclosureRevision,
        bool PoolConsent,
        int? PoolConsentRevision,
        string? Culture,
        string? Website);
}
