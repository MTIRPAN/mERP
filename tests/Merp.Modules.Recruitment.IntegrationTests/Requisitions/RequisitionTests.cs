using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Modules.HR.Application;
using Merp.Modules.Platform.Api.Endpoints;
using Merp.Modules.Platform.Application.Approvals;
using Merp.Modules.Platform.Application.Roles;
using Merp.Modules.Platform.Contracts.Approvals;
using Merp.Modules.Platform.Contracts.Notifications;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Application.Requisitions;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.Testing.Data;
using Merp.Testing.Web;

namespace Merp.Modules.Recruitment.IntegrationTests.Requisitions;

/// <summary>
/// Requisitions end to end (PLAN R2, D48): a manager of requisitions asks, the line manager and the HR manager approve
/// through Platform's approval engine; seats are checked against the norm less the open requisitions.
/// </summary>
public sealed class RequisitionTests(RequisitionFactory factory) : IClassFixture<RequisitionFactory>
{
    private const string Url = "/api/recruitment/requisitions";

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
    public async Task ARequisition_IsApprovedByTheLineManagerAndTheHrManager_AndAnOpeningMayFollow()
    {
        // PLAN R2 acceptance: requisition → approval flow → approved, ready for an opening
        var org = await OrgAsync();
        var hrManager = await HrManagerAsync();
        var requester = Recruiter(org.Requester.Subject);

        var id = await RequisitionFactory.CreatedIdAsync(await requester.PostAsJsonAsync(Url, Body(org, org.OpenPosition, 2, submit: true), Ct));

        var asked = await DetailsAsync(requester, id);
        asked.ShouldSatisfyAllConditions(
            d => d.Requisition.Status.ShouldBe(RequisitionStatus.Pending),
            d => d.Requisition.Number.ShouldStartWith("PT-"),
            d => d.Requisition.OrgUnitName.ShouldBe("Satış Direktörlüğü"),
            d => d.CanCreateOpening.ShouldBeFalse());
        (await factory.OutboxAsync<ApprovalRequested>()).ShouldContain(e =>
            e.DocumentType == RecruitmentDocumentTypes.Requisition && e.DocumentId == id.ToString()
            && e.RequesterEmployeeId == org.Requester.Employee && e.Facts.GetProperty("headcount").GetInt32() == 2);
        await factory.DeliverMessagesAsync();

        await DecideAsync(Client(org.Manager.Subject), id, "Approve");
        await factory.DeliverMessagesAsync();
        (await DetailsAsync(requester, id)).Requisition.Status.ShouldBe(RequisitionStatus.Pending);

        await DecideAsync(hrManager, id, "Approve");
        await factory.DeliverMessagesAsync();
        var approved = await DetailsAsync(requester, id);
        approved.ShouldSatisfyAllConditions(
            d => d.Requisition.Status.ShouldBe(RequisitionStatus.Approved),
            d => d.DecidedAt.ShouldNotBeNull(),
            d => d.CanCreateOpening.ShouldBeTrue(),
            d => d.CanEdit.ShouldBeFalse());
        (await factory.OutboxAsync<NotificationRequested>()).ShouldContain(n =>
            n.Type == RequisitionNotificationTypes.Completed && n.RecipientUserIds.Contains(org.Requester.UserId) && n.Args["number"] == approved.Requisition.Number);
    }

    [Fact]
    public async Task AFilledPosition_AndSeatsHeldByOpenRequisitions_AreRefused()
    {
        // PLAN R2 acceptance: no requisition for a filled position; open requisitions hold their seats (user decision)
        var org = await OrgAsync();
        var requester = Recruiter(org.Requester.Subject);

        var filled = await requester.PostAsJsonAsync(Url, Body(org, org.ManagerPosition, 1), Ct);
        filled.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await filled.Content.ReadAsStringAsync(Ct)).ShouldContain("Pozisyonda bu kadar boş kadro yok");

        await RequisitionFactory.CreatedIdAsync(await requester.PostAsJsonAsync(Url, Body(org, org.OpenPosition, 3, submit: true), Ct));
        var positions = (await requester.GetFromJsonAsync<List<RequisitionPositionItem>>($"{Url}/positions?companyId={org.CompanyId}", Ct))!;
        positions.Single(p => p.PositionId == org.OpenPosition).ShouldSatisfyAllConditions(p => p.Norm.ShouldBe(3), p => p.Available.ShouldBe(0));
        positions.Single(p => p.PositionId == org.ManagerPosition).Available.ShouldBe(0);
        (await requester.PostAsJsonAsync(Url, Body(org, org.OpenPosition, 1), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // A new position needs no seats
        var proposed = new { companyId = org.CompanyId, orgUnitId = org.UnitId, newPositionTitle = "Veri Analisti", newPositionLevel = "U2", headcount = 1, reason = "NewHeadcount", employmentType = "Indefinite", justification = "Raporlama ihtiyacı", hiringManagerUserId = org.Manager.UserId };
        await RequisitionFactory.CreatedIdAsync(await requester.PostAsJsonAsync(Url, proposed, Ct));
    }

    [Fact]
    public async Task AReturnedRequisition_IsCorrectedAndSentAgain_AndCancellingClosesItsApproval()
    {
        var org = await OrgAsync();
        var requester = Recruiter(org.Requester.Subject);
        var id = await RequisitionFactory.CreatedIdAsync(await requester.PostAsJsonAsync(Url, Body(org, org.OpenPosition, 2, submit: true), Ct));
        await factory.DeliverMessagesAsync();

        await DecideAsync(Client(org.Manager.Subject), id, "Return", "Tek kişiyle başlayalım");
        await factory.DeliverMessagesAsync();
        var returned = await DetailsAsync(requester, id);
        returned.ShouldSatisfyAllConditions(
            d => d.Requisition.Status.ShouldBe(RequisitionStatus.Returned),
            d => d.ReturnComment.ShouldBe("Tek kişiyle başlayalım"),
            d => d.CanEdit.ShouldBeTrue());
        (await factory.OutboxAsync<NotificationRequested>()).ShouldContain(n => n.Type == RequisitionNotificationTypes.Returned && n.RecipientUserIds.Contains(org.Requester.UserId));

        var correction = Body(org, org.OpenPosition, 1, submit: true, version: returned.Version);
        (await requester.PutAsJsonAsync($"{Url}/{id}", correction, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var resent = await DetailsAsync(requester, id);
        resent.Requisition.ShouldSatisfyAllConditions(r => r.Status.ShouldBe(RequisitionStatus.Pending), r => r.Headcount.ShouldBe(1));

        (await requester.PostAsJsonAsync($"{Url}/{id}/cancel", new { reason = "Bütçe donduruldu" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await DetailsAsync(requester, id)).Requisition.Status.ShouldBe(RequisitionStatus.Cancelled);
        (await factory.OutboxAsync<ApprovalCancellationRequested>()).ShouldContain(e => e.DocumentType == RecruitmentDocumentTypes.Requisition && e.DocumentId == id.ToString());
        (await requester.PostAsJsonAsync($"{Url}/{id}/cancel", new { reason = "Tekrar" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task TheForm_IsValidated_AndTheSalaryIsShownOnlyToThoseWhoManage()
    {
        var org = await OrgAsync();
        var requester = Recruiter(org.Requester.Subject);

        var invalid = await requester.PostAsJsonAsync(
            Url,
            new { companyId = org.CompanyId, positionId = org.OpenPosition, headcount = 0, reason = "Replacement", employmentType = "Indefinite", salaryMin = 50000, salaryMax = 40000, justification = "", hiringManagerUserId = org.Manager.UserId },
            Ct);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await invalid.Content.ReadAsStringAsync(Ct);
        problem.ShouldSatisfyAllConditions(
            p => p.ShouldContain("headcount"),
            p => p.ShouldContain("replacedEmployeeId"),
            p => p.ShouldContain("salaryMax"),
            p => p.ShouldContain("currency"),
            p => p.ShouldContain("justification"));

        var replaced = await requester.PostAsJsonAsync(
            Url,
            new { companyId = org.CompanyId, positionId = org.OpenPosition, headcount = 1, reason = "Replacement", replacedEmployeeId = Guid.NewGuid(), employmentType = "Indefinite", justification = "Ayrılan çalışan", hiringManagerUserId = org.Manager.UserId },
            Ct);
        replaced.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var id = await RequisitionFactory.CreatedIdAsync(await requester.PostAsJsonAsync(
            Url,
            new { companyId = org.CompanyId, positionId = org.OpenPosition, headcount = 1, reason = "Replacement", replacedEmployeeId = org.Manager.Employee, employmentType = "Indefinite", salaryMin = 40000, salaryMax = 55000, currency = "try", justification = "Ayrılan çalışan", hiringManagerUserId = org.Manager.UserId },
            Ct));
        var managed = await DetailsAsync(requester, id);
        managed.ShouldSatisfyAllConditions(
            d => d.SalaryVisible.ShouldBeTrue(),
            d => d.SalaryMax.ShouldBe(55000),
            d => d.Currency.ShouldBe("TRY"),
            d => d.ReplacedEmployeeName.ShouldNotBeNull(),
            d => d.Requisition.Status.ShouldBe(RequisitionStatus.Draft));

        var reader = factory.CreateClientFor("okuyucu", RequisitionFactory.Tenant, RecruitmentPermissions.Requisitions.Read);
        var read = await DetailsAsync(reader, id);
        read.ShouldSatisfyAllConditions(d => d.SalaryVisible.ShouldBeFalse(), d => d.SalaryMin.ShouldBeNull(), d => d.SalaryMax.ShouldBeNull(), d => d.CanCancel.ShouldBeFalse());
    }

    [Fact]
    public async Task WithoutThePermission_OrOutsideTheCompany_RequisitionsAreNotReached()
    {
        var org = await OrgAsync();
        var requester = Recruiter(org.Requester.Subject);
        var id = await RequisitionFactory.CreatedIdAsync(await requester.PostAsJsonAsync(Url, Body(org, org.OpenPosition, 1), Ct));

        var reader = factory.CreateClientFor("okuyucu", RequisitionFactory.Tenant, RecruitmentPermissions.Requisitions.Read);
        (await reader.PostAsJsonAsync(Url, Body(org, org.OpenPosition, 1), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Client("izinsiz").GetAsync(Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var otherCompany = Guid.CreateVersion7();
        var outsider = factory.CreateClientFor("baska-sirket", RequisitionFactory.Tenant, RecruitmentPermissions.Requisitions.Read, RecruitmentPermissions.Requisitions.Manage)
            .WithScopes(AccessScope.Company(otherCompany));
        (await outsider.GetAsync($"{Url}/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.GetFromJsonAsync<PagedResult<RequisitionListItem>>(Url, Ct))!.Items.ShouldNotContain(r => r.Id == id);
        (await outsider.PostAsJsonAsync(Url, Body(org, org.OpenPosition, 1), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await outsider.PostAsJsonAsync($"{Url}/{id}/cancel", new { reason = "x" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.GetAsync($"{Url}/positions?companyId={org.CompanyId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var inside = factory.CreateClientFor("ayni-sirket", RequisitionFactory.Tenant, RecruitmentPermissions.Requisitions.Read)
            .WithScopes(AccessScope.Company(org.CompanyId));
        (await inside.GetFromJsonAsync<PagedResult<RequisitionListItem>>(Url, Ct))!.Items.ShouldContain(r => r.Id == id);
    }

    private static object Body(Org org, Guid positionId, int headcount, bool submit = false, uint? version = null) => new
    {
        version,
        companyId = org.CompanyId,
        positionId,
        headcount,
        reason = "NewHeadcount",
        employmentType = "Indefinite",
        desiredStartDate = "2027-01-04",
        justification = "Yeni bayi kanalı",
        hiringManagerUserId = org.Manager.UserId,
        submit,
    };

    private HttpClient Client(string subject) => factory.CreateClientFor(subject, RequisitionFactory.Tenant);

    private HttpClient Recruiter(string subject) =>
        factory.CreateClientFor(subject, RequisitionFactory.Tenant, RecruitmentPermissions.Requisitions.Read, RecruitmentPermissions.Requisitions.Manage);

    /// <summary>A company with a manager position, the requester's position below it (both held) and an open position of three seats.</summary>
    private async Task<Org> OrgAsync()
    {
        var admin = factory.CreateClientFor("admin", RequisitionFactory.Tenant, OrgPermissions);
        var company = await RequisitionFactory.CreatedIdAsync(await admin.PostAsJsonAsync(
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
        var unit = await RequisitionFactory.CreatedIdAsync(await admin.PostAsJsonAsync(
            "/api/hr/org-units",
            new { companyId = company, code = NewCode("U"), name = "Satış Direktörlüğü", type = "Directorate" },
            Ct));
        var job = await RequisitionFactory.CreatedIdAsync(await admin.PostAsJsonAsync("/api/hr/jobs", new { code = NewCode("J"), name = "Satış Uzmanı", family = "Satış", level = "U2" }, Ct));
        var managerPosition = await PositionAsync(admin, unit, job, null, null);
        var requesterPosition = await PositionAsync(admin, unit, job, managerPosition, null);
        var openPosition = await PositionAsync(admin, unit, job, managerPosition, 3);
        var manager = await LinkedAsync(admin, company, managerPosition);
        var requester = await LinkedAsync(admin, company, requesterPosition);
        return new Org(company, unit, managerPosition, openPosition, manager, requester);
    }

    private static async Task<Guid> PositionAsync(HttpClient admin, Guid unit, Guid job, Guid? reportsTo, int? norm) =>
        await RequisitionFactory.CreatedIdAsync(await admin.PostAsJsonAsync(
            "/api/hr/positions",
            new { orgUnitId = unit, jobId = job, code = NewCode("P"), reportsToPositionId = reportsTo, norm },
            Ct));

    /// <summary>Hires a person into the position and gives them a user.</summary>
    private async Task<Person> LinkedAsync(HttpClient admin, Guid companyId, Guid positionId)
    {
        var employee = await RequisitionFactory.CreatedIdAsync(await admin.PostAsJsonAsync(
            "/api/hr/employees",
            new
            {
                tckn = TurkishData.Tckn(),
                firstName = "Ayşe",
                lastName = "Yılmaz",
                companyId,
                hireDate = new DateOnly(2025, 1, 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                employmentType = "Indefinite",
                positionId,
            },
            Ct));
        var email = $"personel.{Guid.NewGuid().ToString("N")[..10]}@acme.test";
        (await admin.PostAsJsonAsync($"/api/hr/employees/{employee}/user", new { email }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await factory.DeliverMessagesAsync();
        var subject = factory.Identity.Identities.Single(i => i.Value.Email == email).Key;
        var userId = (await Client(subject).GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!.User.Id!.Value;
        return new Person(employee, userId, subject);
    }

    /// <summary>A user holding the HR manager system role, the approver of the second step.</summary>
    private async Task<HttpClient> HrManagerAsync()
    {
        var client = Client("ik-yoneticisi" + Guid.NewGuid().ToString("N")[..8]);
        var userId = (await client.GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!.User.Id!.Value;
        var users = factory.CreateClientFor("kullanici-yoneticisi", RequisitionFactory.Tenant, "platform.roles.read", "platform.users.manage");
        var role = (await users.GetFromJsonAsync<PagedResult<RoleListItem>>("/api/platform/roles?pageSize=200", Ct))!
            .Items.Single(r => string.Equals(r.Code, "hr-manager", StringComparison.OrdinalIgnoreCase));
        await RequisitionFactory.CreatedIdAsync(await users.PostAsJsonAsync(
            $"/api/platform/users/{userId}/assignments",
            new { roleId = role.Id, scopeType = AccessScopeTypes.Tenant, includeSubUnits = false },
            Ct));
        return client;
    }

    private static async Task DecideAsync(HttpClient approver, Guid requisitionId, string decision, string? comment = null)
    {
        var task = (await approver.GetFromJsonAsync<PagedResult<MyApprovalTaskItem>>("/api/platform/approvals/my-tasks?pageSize=200", Ct))!
            .Items.Single(t => t.DocumentType == RecruitmentDocumentTypes.Requisition && t.DocumentId == requisitionId.ToString());
        var response = await approver.PostAsJsonAsync($"/api/platform/approvals/tasks/{task.TaskId}/decision", new { decision, comment }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<RequisitionDetails> DetailsAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"{Url}/{id}", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<RequisitionDetails>(Ct))!;
    }

    private static string NewCode(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private sealed record Person(Guid Employee, Guid UserId, string Subject);

    private sealed record Org(Guid CompanyId, Guid UnitId, Guid ManagerPosition, Guid OpenPosition, Person Manager, Person Requester);
}
