using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Merp.Application.Abstractions.Paging;
using Merp.Infrastructure.MultiTenancy;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.Employees;
using Merp.Modules.HR.Application.PreHires;
using Merp.Modules.HR.Contracts.PreHires;
using Merp.Modules.HR.Domain.PreHires;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Application.Applications;
using Merp.Modules.Recruitment.Application.Hiring;
using Merp.Modules.Recruitment.Contracts.Hiring;
using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Candidates;
using Merp.Modules.Recruitment.Domain.Offers;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.Modules.Recruitment.Infrastructure.Persistence;
using Merp.Modules.Recruitment.IntegrationTests.Requisitions;
using Merp.Testing.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Recruitment.IntegrationTests.Hiring;

/// <summary>
/// From an accepted offer to an employee (PLAN R7, D54): "İşe al" opens a pre-hire in HR, HR completes it with the hire
/// form, and Recruitment hires the application, fills the requisition's seat and the opening.
/// </summary>
public sealed class HiringTests(RequisitionFactory factory) : IClassFixture<RequisitionFactory>
{
    private static readonly string[] HrPermissionList =
    [
        HrPermissions.Org.Read,
        HrPermissions.Org.Manage,
        HrPermissions.Employees.Read,
        HrPermissions.Employees.Create,
        HrPermissions.Employees.Update,
        HrPermissions.Employees.Sensitive.Read,
        HrPermissions.Employees.Sensitive.Update,
        "platform.companies.read",
        "platform.companies.manage",
    ];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAcceptedCandidate_BecomesAPreHire_AndOnceHrCompletesItTheSeatsAreFilled()
    {
        // PLAN R7 acceptance: one click makes a pre-hire; once HR completes it the seat is filled and the requisition is Filled
        var setup = await SetupAsync();
        var hr = Hr();

        (await Recruiter().PostAsJsonAsync($"/api/recruitment/applications/{setup.Application}/hire", new { version = await VersionAsync(setup.Application) }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await DetailsAsync(setup.Application)).ShouldSatisfyAllConditions(
            d => d.Application.Status.ShouldBe(ApplicationStatus.Active),
            d => d.Application.HireRequestedAt.ShouldNotBeNull(),
            d => d.CanHire.ShouldBeFalse());
        var hired = (await factory.OutboxAsync<CandidateHired>()).Single(e => e.ApplicationId == setup.Application);
        hired.ShouldSatisfyAllConditions(
            e => e.CompanyId.ShouldBe(setup.Company),
            e => e.PositionId.ShouldBe(setup.Position),
            e => e.OrgUnitId.ShouldBe(setup.Unit),
            e => e.EmploymentType.ShouldBe("Indefinite"),
            e => e.StartDate.ShouldBe(setup.StartDate));
        await factory.DeliverMessagesAsync();

        var preHire = (await hr.GetFromJsonAsync<PagedResult<PreHireItem>>("/api/hr/pre-hires?status=Pending&pageSize=200", Ct))!
            .Items.Single(p => p.ApplicationId == setup.Application);
        preHire.ShouldSatisfyAllConditions(
            p => p.FirstName.ShouldBe("Ayşe"),
            p => p.PositionId.ShouldBe(setup.Position),
            p => p.OrgUnitName.ShouldBe("Satış Direktörlüğü"),
            p => p.ExistingEmployeeId.ShouldBeNull());
        (await factory.CreateClientFor("ik-okur", RequisitionFactory.Tenant, HrPermissions.Employees.Read).GetAsync("/api/hr/pre-hires", Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // HR completes it with the hire form; a wrong pre-hire is refused
        var body = HireBody(setup, preHire.StartDate);
        (await ProblemAsync(await hr.PostAsJsonAsync("/api/hr/employees", body with { PreHireId = Guid.CreateVersion7() }, Ct))).ShouldContainKey("preHireId");
        var employee = await RequisitionFactory.CreatedIdAsync(await hr.PostAsJsonAsync("/api/hr/employees", body with { PreHireId = preHire.Id }, Ct));
        (await hr.GetFromJsonAsync<PreHireItem>($"/api/hr/pre-hires/{preHire.Id}", Ct))!.ShouldSatisfyAllConditions(
            p => p.Status.ShouldBe(PreHireStatus.Completed),
            p => p.EmployeeId.ShouldBe(employee));
        (await factory.OutboxAsync<PreHireCompleted>()).ShouldContain(e => e.ApplicationId == setup.Application && e.EmployeeId == employee);
        (await ProblemAsync(await hr.PostAsJsonAsync("/api/hr/employees", HireBody(setup, preHire.StartDate) with { PreHireId = preHire.Id }, Ct))).ShouldContainKey("preHireId");
        await factory.DeliverMessagesAsync();

        (await DetailsAsync(setup.Application)).Application.ShouldSatisfyAllConditions(a => a.Status.ShouldBe(ApplicationStatus.Hired), a => a.HireRequestedAt.ShouldBeNull());
        var (requisitionStatus, filled, openingStatus) = await WithDbAsync(async db =>
        {
            var requisition = await db.JobRequisitions.AsNoTracking().SingleAsync(r => r.Id == JobRequisitionId.From(setup.Requisition), Ct);
            var opening = await db.JobOpenings.AsNoTracking().SingleAsync(o => o.Id == JobOpeningId.From(setup.Opening), Ct);
            return (requisition.Status, requisition.FilledCount, opening.Status);
        });
        (requisitionStatus, filled, openingStatus).ShouldBe((RequisitionStatus.Filled, 1, OpeningStatus.Filled));

        // The others still in the pipeline are rejected at once
        var rejected = await Recruiter().PostAsJsonAsync($"/api/recruitment/openings/{setup.Opening}/reject-remaining", new { reasonCode = "SKILLS", sendEmail = false, emailDelayDays = 0 }, Ct);
        rejected.StatusCode.ShouldBe(HttpStatusCode.OK, await rejected.Content.ReadAsStringAsync(Ct));
        (await rejected.Content.ReadFromJsonAsync<RemainingRejected>(Ct))!.Count.ShouldBe(1);
        (await DetailsAsync(setup.Other)).Application.Status.ShouldBe(ApplicationStatus.Rejected);
        (await DetailsAsync(setup.Application)).Application.Status.ShouldBe(ApplicationStatus.Hired);
    }

    [Fact]
    public async Task ACancelledPreHire_PutsTheApplicationBack_AndAnEmployeeIsFoundByEmail()
    {
        var setup = await SetupAsync();
        var hr = Hr();

        // The candidate's address is an employee's: the pre-hire names that person
        var person = await RequisitionFactory.CreatedIdAsync(await hr.PostAsJsonAsync("/api/hr/employees", HireBody(setup, DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1))), Ct));
        var card = (await hr.GetFromJsonAsync<EmployeeDetails>($"/api/hr/employees/{person}", Ct))!;
        var contacts = await hr.PutAsJsonAsync(
            $"/api/hr/employees/{person}/contacts",
            new { version = card.Version, emails = new[] { new { kind = "Personal", address = setup.Email.ToUpperInvariant(), isPrimary = true } }, phones = Array.Empty<object>(), addresses = Array.Empty<object>(), emergencyContacts = Array.Empty<object>() },
            Ct);
        contacts.StatusCode.ShouldBe(HttpStatusCode.NoContent, await contacts.Content.ReadAsStringAsync(Ct));

        (await Recruiter().PostAsJsonAsync($"/api/recruitment/applications/{setup.Application}/hire", new { version = await VersionAsync(setup.Application) }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Recruiter().PostAsJsonAsync($"/api/recruitment/applications/{setup.Application}/hire", new { version = await VersionAsync(setup.Application) }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await factory.DeliverMessagesAsync();
        var preHire = (await hr.GetFromJsonAsync<PagedResult<PreHireItem>>("/api/hr/pre-hires?pageSize=200", Ct))!.Items.Single(p => p.ApplicationId == setup.Application);
        preHire.ShouldSatisfyAllConditions(p => p.ExistingEmployeeId.ShouldBe(person), p => p.OpenEmploymentId.ShouldNotBeNull());

        (await hr.PostAsJsonAsync($"/api/hr/pre-hires/{preHire.Id}/cancel", new { reason = "Aday işe başlamadı" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await hr.PostAsJsonAsync($"/api/hr/pre-hires/{preHire.Id}/cancel", new { reason = (string?)null }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await factory.OutboxAsync<PreHireCancelled>()).ShouldContain(e => e.ApplicationId == setup.Application && e.Reason == "Aday işe başlamadı");
        await factory.DeliverMessagesAsync();

        (await DetailsAsync(setup.Application)).ShouldSatisfyAllConditions(
            d => d.Application.Status.ShouldBe(ApplicationStatus.Active),
            d => d.Application.HireRequestedAt.ShouldBeNull(),
            d => d.CanHire.ShouldBeTrue());
    }

    private HttpClient Recruiter() => factory.CreateClientFor(
        "ise-alim-uzmani",
        RequisitionFactory.Tenant,
        RecruitmentPermissions.Applications.Read,
        RecruitmentPermissions.Applications.Manage,
        RecruitmentPermissions.Offers.Read,
        RecruitmentPermissions.Offers.Manage);

    private HttpClient Hr() => factory.CreateClientFor("ik-uzmani", RequisitionFactory.Tenant, HrPermissionList);

    private static HireRequest HireBody(Setup setup, DateOnly hireDate) =>
        new(TurkishData.Tckn(), "Ayşe", "Yılmaz", setup.Company, hireDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "Indefinite", setup.Position);

    /// <summary>
    /// A company with a unit, a job and a position of two seats; an approved requisition of one seat for it, its
    /// published opening, two applications from the career page and an offer the first candidate accepted.
    /// </summary>
    private async Task<Setup> SetupAsync()
    {
        var hr = Hr();
        var company = await RequisitionFactory.CreatedIdAsync(await hr.PostAsJsonAsync(
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
        var unit = await RequisitionFactory.CreatedIdAsync(await hr.PostAsJsonAsync("/api/hr/org-units", new { companyId = company, code = NewCode("U"), name = "Satış Direktörlüğü", type = "Directorate" }, Ct));
        var job = await RequisitionFactory.CreatedIdAsync(await hr.PostAsJsonAsync("/api/hr/jobs", new { code = NewCode("J"), name = "Satış Uzmanı", family = "Satış", level = "U2" }, Ct));
        var position = await RequisitionFactory.CreatedIdAsync(await hr.PostAsJsonAsync("/api/hr/positions", new { orgUnitId = unit, jobId = job, code = NewCode("P"), norm = 2 }, Ct));
        var manager = Guid.CreateVersion7();
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));

        var (requisition, opening) = await WithDbAsync(async db =>
        {
            var draft = JobRequisition.Draft(
                "PT-TEST-" + Guid.NewGuid().ToString("N")[..8],
                manager,
                new RequisitionTerms(company, unit, position, null, null, 1, RequisitionReason.NewHeadcount, null, RequisitionEmploymentType.Indefinite, null, null, null, null, "Yeni bayi kanalı", manager));
            draft.Submit(DateTimeOffset.UtcNow);
            draft.Complete(true, DateTimeOffset.UtcNow);
            db.JobRequisitions.Add(draft);
            var template = await db.StageTemplates.SingleAsync(t => t.IsDefault, Ct);
            var published = JobOpening.Draft(
                new OpeningTerms(company, draft.Id, "Satış Uzmanı", null, null, null, WorkModel.OnSite, RequisitionEmploymentType.Indefinite, 1, OpeningVisibility.Public, null, null, new OpeningTeam(manager, [], []), new ApplicationForm(false, false, false, [])),
                template);
            published.Publish(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();
            db.JobOpenings.Add(published);
            await db.SaveChangesAsync(Ct);
            return (draft.Id.Value, published.Id.Value);
        });

        var (application, email) = await ApplyAsync(opening);
        var (other, _) = await ApplyAsync(opening);
        await WithDbAsync(async db =>
        {
            var applied = await db.JobApplications.AsNoTracking().SingleAsync(a => a.Id == JobApplicationId.From(application), Ct);
            var published = await db.JobOpenings.AsNoTracking().SingleAsync(o => o.Id == JobOpeningId.From(opening), Ct);
            var offer = JobOffer.Draft("TKL-TEST-" + Guid.NewGuid().ToString("N")[..8], applied, published, unit, manager, new OfferTerms("Satış Uzmanı", null, start, 90_000m, "TRY", SalaryPeriod.Monthly, null, start.AddDays(-20))).Value;
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            offer.Submit(false, today, DateTimeOffset.UtcNow);
            offer.Complete(true, DateTimeOffset.UtcNow);
            offer.Send(Guid.CreateVersion7(), Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)), today, DateTimeOffset.UtcNow);
            offer.Respond(true, null, OfferResponseChannel.Hr, manager, today, DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();
            db.JobOffers.Add(offer);
            return await db.SaveChangesAsync(Ct);
        });
        return new Setup(company, unit, position, requisition, opening, application, other, email, start);
    }

    private async Task<(Guid Application, string Email)> ApplyAsync(Guid opening)
    {
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

    private async Task<ApplicationDetails> DetailsAsync(Guid application) =>
        (await Recruiter().GetFromJsonAsync<ApplicationDetails>($"/api/recruitment/applications/{application}", Ct))!;

    private async Task<uint> VersionAsync(Guid application) => (await DetailsAsync(application)).Version;

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

    private sealed record Setup(Guid Company, Guid Unit, Guid Position, Guid Requisition, Guid Opening, Guid Application, Guid Other, string Email, DateOnly StartDate);

    private sealed record HireRequest(string Tckn, string FirstName, string LastName, Guid CompanyId, string HireDate, string EmploymentType, Guid PositionId)
    {
        public Guid? PreHireId { get; init; }
    }
}
