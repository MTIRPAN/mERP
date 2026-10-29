using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Infrastructure.MultiTenancy;
using Merp.Modules.Platform.Application.Approvals;
using Merp.Modules.Platform.Application.Roles;
using Merp.Modules.Platform.Contracts.Approvals;
using Merp.Modules.Platform.Contracts.Notifications;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Application.Applications;
using Merp.Modules.Recruitment.Application.Offers;
using Merp.Modules.Recruitment.Application.Settings;
using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Candidates;
using Merp.Modules.Recruitment.Domain.Offers;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.Modules.Recruitment.Infrastructure.Persistence;
using Merp.Modules.Recruitment.IntegrationTests.Requisitions;
using Merp.Testing.Data;
using Merp.Testing.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Recruitment.IntegrationTests.Offers;

/// <summary>
/// Job offers end to end (PLAN R6, D53): approval through <c>recruitment.offer</c> with the above-budget step, the
/// letter and the answer link, HR's record of the answer, the expiry job and hiring with an accepted offer.
/// </summary>
public sealed class OfferTests(RequisitionFactory factory) : IClassFixture<RequisitionFactory>
{
    private const string Url = "/api/recruitment/offers";

    private static readonly string[] Permissions =
    [
        RecruitmentPermissions.Offers.Read,
        RecruitmentPermissions.Offers.Manage,
        RecruitmentPermissions.Applications.Read,
        RecruitmentPermissions.Applications.Manage,
        "platform.companies.read",
        "platform.companies.manage",
    ];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnOfferWithinBudget_IsApproved_Sent_AndAcceptedWithTheLink_ThenTheApplicationIsHired()
    {
        // PLAN R6 acceptance: the accepted offer opens the hire step
        var setup = await SetupAsync(salaryMax: 100_000m);
        var hrManager = await HrManagerAsync();
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(40));

        var id = await RequisitionFactory.CreatedIdAsync(await Recruiter().PostAsJsonAsync(Url, Body(setup.Application, 90_000m, start), Ct));
        var draft = await DetailsAsync(Recruiter(), id);
        draft.ShouldSatisfyAllConditions(
            d => d.Offer.Number.ShouldStartWith("TKL-"),
            d => d.Offer.Status.ShouldBe(OfferStatus.Draft),
            d => d.Offer.PositionTitle.ShouldBe("Satış Uzmanı"),
            d => d.Salary.ShouldBe(90_000m),
            d => d.BudgetMax.ShouldBe(100_000m),
            d => d.HiringManagerUserId.ShouldBe(setup.Manager),
            d => d.CanEdit.ShouldBeTrue());
        (await Recruiter().PostAsJsonAsync(Url, Body(setup.Application, 80_000m, start), Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Hired needs an accepted offer
        var hired = await StageAsync(setup.Opening, StageType.Hired);
        var move = await Recruiter().PostAsJsonAsync($"/api/recruitment/applications/{setup.Application}/move", new { version = await ApplicationVersionAsync(setup.Application), stageId = hired }, Ct);
        move.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        (await Recruiter().PostAsJsonAsync($"{Url}/{id}/submit", new { version = draft.Version }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await factory.OutboxAsync<ApprovalRequested>()).Single(e => e.DocumentType == RecruitmentDocumentTypes.Offer && e.DocumentId == id.ToString()).Facts.ShouldSatisfyAllConditions(
            f => f.GetProperty("isAboveBudget").GetBoolean().ShouldBeFalse(),
            f => f.GetProperty("salary").GetDecimal().ShouldBe(90_000m),
            f => f.GetProperty("hiringManagerUserId").GetGuid().ShouldBe(setup.Manager),
            f => f.GetProperty("orgUnitId").GetGuid().ShouldBe(setup.OrgUnit));
        await factory.DeliverMessagesAsync();

        // The hiring manager reads it from self service, with the salary as its approver
        var manager = factory.CreateClientFor(setup.ManagerSubject, RequisitionFactory.Tenant, RecruitmentPermissions.Self.Hiring);
        (await manager.GetFromJsonAsync<OfferDetails>($"/api/recruitment/me/hiring/offers/{id}", Ct))!.Salary.ShouldBe(90_000m);
        await DecideAsync(manager, id, "Approve");
        await factory.DeliverMessagesAsync();
        await DecideAsync(hrManager, id, "Approve");
        await factory.DeliverMessagesAsync();
        var approved = await DetailsAsync(Recruiter(), id);
        approved.ShouldSatisfyAllConditions(d => d.Offer.Status.ShouldBe(OfferStatus.Approved), d => d.CanSend.ShouldBeTrue(), d => d.DecidedAt.ShouldNotBeNull());

        // The letter goes with the answer link
        (await Recruiter().PostAsJsonAsync($"{Url}/{id}/send", new { version = approved.Version }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var email = (await factory.OutboxAsync<ExternalEmailRequested>()).Single(e => e.Type == RecruitmentNotificationTypes.OfferSent && e.To == setup.Email);
        var letter = Encoding.UTF8.GetString(email.Attachments.ShouldHaveSingleItem().Content);
        letter.ShouldSatisfyAllConditions(l => l.ShouldStartWith(HtmlPdfConverter.Header), l => l.ShouldContain("90.000,00 TRY"), l => l.ShouldContain("Satış Uzmanı"));
        email.Link.ShouldNotBeNull().ShouldStartWith($"/careers/{RequisitionFactory.Tenant}/offers/");
        var token = email.Link!.Split('/')[^1];
        var letterFile = await Recruiter().GetAsync($"{Url}/{id}/letter", Ct);
        letterFile.StatusCode.ShouldBe(HttpStatusCode.OK);
        letterFile.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");

        var candidate = factory.CreateClient();
        var publicUrl = $"/api/public/{RequisitionFactory.Tenant}/offers/{token}";
        (await candidate.GetFromJsonAsync<OfferResponsePage>(publicUrl, Ct))!.ShouldSatisfyAllConditions(
            p => p.Salary.ShouldBe(90_000m),
            p => p.Status.ShouldBe(OfferStatus.Sent),
            p => p.CanRespond.ShouldBeTrue());
        (await candidate.GetAsync($"/api/public/{RequisitionFactory.Tenant}/offers/{token}x", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await candidate.PostAsJsonAsync(publicUrl, new { accepted = true, note = "Teşekkürler" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await candidate.GetFromJsonAsync<OfferResponsePage>(publicUrl, Ct))!.ShouldSatisfyAllConditions(p => p.Status.ShouldBe(OfferStatus.Accepted), p => p.CanRespond.ShouldBeFalse());
        (await candidate.PostAsJsonAsync(publicUrl, new { accepted = false }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await factory.OutboxAsync<NotificationRequested>()).ShouldContain(n => n.Type == OfferNotificationTypes.Answered && n.RecipientUserIds.Contains(setup.Manager));

        var details = (await Recruiter().GetFromJsonAsync<ApplicationDetails>($"/api/recruitment/applications/{setup.Application}", Ct))!;
        details.ShouldSatisfyAllConditions(d => d.CanHire.ShouldBeTrue(), d => d.CanCreateOffer.ShouldBeFalse(), d => d.Offers.ShouldHaveSingleItem().Status.ShouldBe(OfferStatus.Accepted));
        (await Recruiter().PostAsJsonAsync($"/api/recruitment/applications/{setup.Application}/hire", new { version = details.Version }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Recruiter().GetFromJsonAsync<ApplicationDetails>($"/api/recruitment/applications/{setup.Application}", Ct))!.Application.Status.ShouldBe(ApplicationStatus.Hired);
    }

    [Fact]
    public async Task AnOfferAboveBudget_GoesToTheExtraApprovalStep()
    {
        // PLAN R6 acceptance: an offer above budget goes to an extra approval step
        var setup = await SetupAsync(salaryMax: 100_000m);
        var hrManager = await HrManagerAsync();
        var id = await RequisitionFactory.CreatedIdAsync(await Recruiter().PostAsJsonAsync(Url, Body(setup.Application, 1_500_000m, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(40))) with { Period = "Yearly" }, Ct));

        (await Recruiter().PostAsJsonAsync($"{Url}/{id}/submit", new { version = (await DetailsAsync(Recruiter(), id)).Version }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await factory.OutboxAsync<ApprovalRequested>()).Single(e => e.DocumentId == id.ToString()).Facts.ShouldSatisfyAllConditions(
            f => f.GetProperty("isAboveBudget").GetBoolean().ShouldBeTrue(),
            f => f.GetProperty("salary").GetDecimal().ShouldBe(125_000m));
        await factory.DeliverMessagesAsync();
        await DecideAsync(factory.CreateClientFor(setup.ManagerSubject, RequisitionFactory.Tenant), id, "Approve");
        await factory.DeliverMessagesAsync();
        await DecideAsync(hrManager, id, "Approve");
        await factory.DeliverMessagesAsync();

        // Two approvals are not enough: the unit manager's step applies
        (await DetailsAsync(Recruiter(), id)).ShouldSatisfyAllConditions(d => d.Offer.Status.ShouldBe(OfferStatus.Pending), d => d.Offer.IsAboveBudget.ShouldBeTrue());
        var admin = factory.CreateClientFor("onay-yoneticisi", RequisitionFactory.Tenant, "platform.approvals.admin");
        var facts = new { salary = 125_000m, grade = "M2", companyId = setup.Company, orgUnitId = setup.OrgUnit, hiringManagerUserId = setup.Manager };
        var above = await PreviewAsync(admin, setup.Company, new { facts.salary, facts.grade, facts.companyId, facts.orgUnitId, facts.hiringManagerUserId, isAboveBudget = true });
        var within = await PreviewAsync(admin, setup.Company, new { facts.salary, facts.grade, facts.companyId, facts.orgUnitId, facts.hiringManagerUserId, isAboveBudget = false });
        above.Steps.Count.ShouldBe(3);
        above.Steps[2].SkipReason.ShouldNotBe("condition");
        within.Steps[2].SkipReason.ShouldBe("condition");
    }

    [Fact]
    public async Task HrRecordsTheAnswer_TheJobClosesLateOffers_AndAWithdrawalClosesTheApproval()
    {
        var setup = await SetupAsync(salaryMax: null);
        var hrManager = await HrManagerAsync();
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(40));
        var id = await SentOfferAsync(setup, hrManager, start);

        var sent = await DetailsAsync(Recruiter(), id);
        (await Recruiter().PostAsJsonAsync($"{Url}/{id}/response", new { version = sent.Version, accepted = false, note = "Başka teklifi kabul etti" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await DetailsAsync(Recruiter(), id)).ShouldSatisfyAllConditions(
            d => d.Offer.Status.ShouldBe(OfferStatus.Declined),
            d => d.RespondedVia.ShouldBe(OfferResponseChannel.Hr),
            d => d.ResponseNote.ShouldBe("Başka teklifi kabul etti"));

        // A declined offer makes room for a new one, which the job closes once its last day has passed
        var second = await SentOfferAsync(setup, hrManager, start);
        await WithDbAsync(db => db.JobOffers.Where(o => o.Id == JobOfferId.From(second))
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.ExpiresOn, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2))), Ct));
        (await ExpireAsync()).ShouldBeGreaterThanOrEqualTo(1);
        (await DetailsAsync(Recruiter(), second)).Offer.Status.ShouldBe(OfferStatus.Expired);

        // Withdrawing an offer waiting for approval closes its approval
        var third = await RequisitionFactory.CreatedIdAsync(await Recruiter().PostAsJsonAsync(Url, Body(setup.Application, 70_000m, start), Ct));
        (await Recruiter().PostAsJsonAsync($"{Url}/{third}/submit", new { version = (await DetailsAsync(Recruiter(), third)).Version }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Recruiter().PostAsJsonAsync($"{Url}/{third}/withdraw", new { version = (await DetailsAsync(Recruiter(), third)).Version, reason = "Pozisyon donduruldu" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await factory.OutboxAsync<ApprovalCancellationRequested>()).ShouldContain(e => e.DocumentType == RecruitmentDocumentTypes.Offer && e.DocumentId == third.ToString());
        (await Recruiter().GetFromJsonAsync<PagedResult<OfferListItem>>($"{Url}?applicationId={setup.Application}", Ct))!.Items.Select(o => o.Status)
            .ShouldBe([OfferStatus.Withdrawn, OfferStatus.Expired, OfferStatus.Declined]);
    }

    [Fact]
    public async Task AnOffer_IsChecked_TheSalaryIsMasked_AndOtherCompaniesAreKeptOut()
    {
        var setup = await SetupAsync(salaryMax: 100_000m);
        var (otherCompany, _, _) = await CompanyAsync();
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(40));

        var invalid = await Recruiter().PostAsJsonAsync(Url, Body(setup.Application, 0m, start) with { Currency = "TL", ExpiresOn = start.AddDays(1) }, Ct);
        (await ProblemAsync(invalid)).Keys.ShouldBe(["salary", "currency"], ignoreOrder: true);
        (await ProblemAsync(await Recruiter().PostAsJsonAsync(Url, Body(setup.Application, 90_000m, start) with { ExpiresOn = start.AddDays(1) }, Ct))).ShouldContainKey("expiresOn");
        var scoped = factory.CreateClientFor("teklif-baska-sirket", RequisitionFactory.Tenant, Permissions).WithScopes(AccessScope.Company(otherCompany));
        (await ProblemAsync(await scoped.PostAsJsonAsync(Url, Body(setup.Application, 90_000m, start), Ct))).ShouldContainKey("applicationId");
        var reader = factory.CreateClientFor("teklif-okuyucu", RequisitionFactory.Tenant, RecruitmentPermissions.Offers.Read);
        (await reader.PostAsJsonAsync(Url, Body(setup.Application, 90_000m, start), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var id = await RequisitionFactory.CreatedIdAsync(await Recruiter().PostAsJsonAsync(Url, Body(setup.Application, 90_000m, start), Ct));
        (await DetailsAsync(reader, id)).ShouldSatisfyAllConditions(
            d => d.SalaryVisible.ShouldBeFalse(),
            d => d.Salary.ShouldBeNull(),
            d => d.Currency.ShouldBeNull(),
            d => d.BudgetMax.ShouldBeNull(),
            d => d.CanEdit.ShouldBeFalse());
        (await reader.GetAsync($"{Url}/{id}/letter", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await scoped.GetAsync($"{Url}/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await scoped.GetFromJsonAsync<PagedResult<OfferListItem>>(Url, Ct))!.Items.ShouldNotContain(o => o.Id == id);

        // Someone else from self service is not the hiring manager
        (await factory.CreateClientFor("baska-calisan", RequisitionFactory.Tenant, RecruitmentPermissions.Self.Hiring).GetAsync($"/api/recruitment/me/hiring/offers/{id}", Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await factory.CreateClientFor(setup.ManagerSubject, RequisitionFactory.Tenant, RecruitmentPermissions.Self.Hiring).GetFromJsonAsync<OfferDetails>($"/api/recruitment/me/hiring/offers/{id}", Ct))!
            .SalaryVisible.ShouldBeFalse();
    }

    private HttpClient Recruiter() => factory.CreateClientFor("teklif-uzmani", RequisitionFactory.Tenant, Permissions);

    private static OfferBody Body(Guid application, decimal salary, DateOnly start) =>
        new(application, null, "M2", start, salary, "TRY", "Monthly", "Özel sağlık sigortası", start.AddDays(-20));

    /// <summary>An offer approved by the hiring manager and the HR manager and sent; returns its id.</summary>
    private async Task<Guid> SentOfferAsync(Setup setup, HttpClient hrManager, DateOnly start)
    {
        var id = await RequisitionFactory.CreatedIdAsync(await Recruiter().PostAsJsonAsync(Url, Body(setup.Application, 85_000m, start), Ct));
        (await Recruiter().PostAsJsonAsync($"{Url}/{id}/submit", new { version = (await DetailsAsync(Recruiter(), id)).Version }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DeliverMessagesAsync();
        await DecideAsync(factory.CreateClientFor(setup.ManagerSubject, RequisitionFactory.Tenant), id, "Approve");
        await factory.DeliverMessagesAsync();
        await DecideAsync(hrManager, id, "Approve");
        await factory.DeliverMessagesAsync();
        var send = await Recruiter().PostAsJsonAsync($"{Url}/{id}/send", new { version = (await DetailsAsync(Recruiter(), id)).Version }, Ct);
        send.StatusCode.ShouldBe(HttpStatusCode.NoContent, await send.Content.ReadAsStringAsync(Ct));
        return id;
    }

    private async Task<(Guid Company, Guid Manager, string ManagerSubject)> CompanyAsync()
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
        var subject = $"yonetici-{Guid.NewGuid():N}";
        using var me = JsonDocument.Parse(await factory.CreateClientFor(subject, RequisitionFactory.Tenant).GetStringAsync("/api/platform/me", Ct));
        return (company, me.RootElement.GetProperty("user").GetProperty("id").GetGuid(), subject);
    }

    /// <summary>
    /// A company, its hiring manager, an approved requisition with the salary range, its published opening and an
    /// application to it from the career page.
    /// </summary>
    private async Task<Setup> SetupAsync(decimal? salaryMax)
    {
        var (company, manager, managerSubject) = await CompanyAsync();
        var orgUnit = Guid.CreateVersion7();
        var opening = await WithDbAsync(async db =>
        {
            var requisition = JobRequisition.Draft(
                "PT-TEST-" + Guid.NewGuid().ToString("N")[..8],
                manager,
                new RequisitionTerms(company, orgUnit, null, "Satış Uzmanı", "M2", 1, RequisitionReason.NewHeadcount, null, RequisitionEmploymentType.Indefinite, null, salaryMax is null ? null : 70_000m, salaryMax, salaryMax is null ? null : "TRY", "Yeni bayi kanalı", manager));
            requisition.Submit(DateTimeOffset.UtcNow);
            requisition.Complete(true, DateTimeOffset.UtcNow);
            db.JobRequisitions.Add(requisition);
            var template = await db.StageTemplates.SingleAsync(t => t.IsDefault, Ct);
            var draft = JobOpening.Draft(
                new OpeningTerms(company, requisition.Id, "Satış Uzmanı", null, null, null, WorkModel.OnSite, RequisitionEmploymentType.Indefinite, 1, OpeningVisibility.Public, null, null, new OpeningTeam(manager, [], []), new ApplicationForm(false, false, false, [])),
                template);
            draft.Publish(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();
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
        return new Setup(company, orgUnit, manager, managerSubject, opening, application, email);
    }

    /// <summary>A user holding the HR manager system role over the tenant, the approver of the second step.</summary>
    private async Task<HttpClient> HrManagerAsync()
    {
        var client = factory.CreateClientFor("ik-yoneticisi" + Guid.NewGuid().ToString("N")[..8], RequisitionFactory.Tenant);
        using var me = JsonDocument.Parse(await client.GetStringAsync("/api/platform/me", Ct));
        var userId = me.RootElement.GetProperty("user").GetProperty("id").GetGuid();
        var users = factory.CreateClientFor("kullanici-yoneticisi", RequisitionFactory.Tenant, "platform.roles.read", "platform.users.manage");
        var role = (await users.GetFromJsonAsync<PagedResult<RoleListItem>>("/api/platform/roles?pageSize=200", Ct))!
            .Items.Single(r => string.Equals(r.Code, "hr-manager", StringComparison.OrdinalIgnoreCase));
        await RequisitionFactory.CreatedIdAsync(await users.PostAsJsonAsync(
            $"/api/platform/users/{userId}/assignments",
            new { roleId = role.Id, scopeType = AccessScopeTypes.Tenant, includeSubUnits = false },
            Ct));
        return client;
    }

    private static async Task DecideAsync(HttpClient approver, Guid offerId, string decision)
    {
        var task = (await approver.GetFromJsonAsync<PagedResult<MyApprovalTaskItem>>("/api/platform/approvals/my-tasks?pageSize=200", Ct))!
            .Items.Single(t => t.DocumentType == RecruitmentDocumentTypes.Offer && t.DocumentId == offerId.ToString());
        var response = await approver.PostAsJsonAsync($"/api/platform/approvals/tasks/{task.TaskId}/decision", new { decision, comment = (string?)null }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<ApprovalPreview> PreviewAsync(HttpClient client, Guid companyId, object facts)
    {
        var response = await client.PostAsJsonAsync("/api/platform/approvals/preview", new { documentType = RecruitmentDocumentTypes.Offer, companyId, facts }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<ApprovalPreview>(Ct))!;
    }

    private static async Task<OfferDetails> DetailsAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"{Url}/{id}", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<OfferDetails>(Ct))!;
    }

    private Task<Guid> StageAsync(Guid opening, StageType type) =>
        WithDbAsync(async db => (await db.JobOpenings.AsNoTracking().SingleAsync(o => o.Id == JobOpeningId.From(opening), Ct)).Stages.First(s => s.Type == type).Id);

    private async Task<uint> ApplicationVersionAsync(Guid application) =>
        (await Recruiter().GetFromJsonAsync<ApplicationDetails>($"/api/recruitment/applications/{application}", Ct))!.Version;

    private async Task<int> ExpireAsync()
    {
        var tenant = (await factory.Services.GetRequiredService<ITenantStore>().FindByCodeAsync(RequisitionFactory.Tenant, Ct))!;
        await using var scope = factory.Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        return (await scope.ServiceProvider.GetRequiredService<ISender>().Send(new ExpireOffersCommand(), Ct)).Value;
    }

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

    private sealed record Setup(Guid Company, Guid OrgUnit, Guid Manager, string ManagerSubject, Guid Opening, Guid Application, string Email);

    private sealed record OfferBody(
        Guid ApplicationId,
        string? PositionTitle,
        string? Grade,
        DateOnly StartDate,
        decimal Salary,
        string? Currency,
        string Period,
        string? Benefits,
        DateOnly ExpiresOn);
}
