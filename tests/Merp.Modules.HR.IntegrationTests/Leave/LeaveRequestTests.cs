using System.Net;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.Documents;
using Merp.Modules.HR.Application.Leave;
using Merp.Modules.HR.Application.SelfService;
using Merp.Modules.HR.Domain.Leave;
using Merp.Modules.HR.IntegrationTests.Employees;
using Merp.Modules.HR.IntegrationTests.Organization;
using Merp.Modules.Platform.Api.Endpoints;
using Merp.Modules.Platform.Application.Approvals;
using Merp.Modules.Platform.Application.Roles;
using Merp.Modules.Platform.Contracts.Approvals;
using Merp.Modules.Platform.Contracts.Notifications;
using Merp.Testing.Data;
using Merp.Testing.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.HR.IntegrationTests.Leave;

/// <summary>
/// Leave requests end to end (PLAN L4, D44): the employee asks, the manager (and HR for long leaves) approves through
/// Platform's approval engine, the balance moves; returns, cancellations, HR's records, early returns and bulk leaves.
/// The fake clock stands on Monday 5 January 2026.
/// </summary>
public sealed class LeaveRequestTests(HrClockFactory factory) : IClassFixture<HrClockFactory>
{
    private const string Mine = "/api/hr/me/leave-requests";
    private const string Hr = "/api/hr/leave-requests";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheEmployeeAsks_TheManagerApproves_TheBalanceDrops_AndTheyAreNotified()
    {
        // PLAN L4 acceptance: employee → manager → balance → notifications
        var team = await TeamAsync();
        var employee = Self(team.Employee);
        var annual = await TypeIdAsync(team.Admin, "YILLIK");

        var id = await HrFactory.CreatedIdAsync(await employee.PostAsJsonAsync(
            Mine,
            new { leaveTypeId = annual, startDate = "2026-02-02", endDate = "2026-02-04", note = "Aile ziyareti", submit = true },
            Ct));

        var asked = await MyRequestAsync(employee, id);
        asked.Request.ShouldSatisfyAllConditions(
            r => r.Status.ShouldBe(LeaveRequestStatus.Pending),
            r => r.Number.ShouldStartWith("IZN-2026-"),
            r => r.Days.ShouldBe(3),
            r => r.Origin.ShouldBe(LeaveRequestOrigin.Self));
        (await factory.OutboxAsync<ApprovalRequested>()).ShouldContain(e =>
            e.DocumentType == HrApprovalDocumentTypes.LeaveRequest && e.DocumentId == id.ToString()
            && e.RequesterEmployeeId == team.Employee.Employee && e.Facts.GetProperty("days").GetDecimal() == 3);
        var waiting = (await MyBalancesAsync(employee)).Single(b => b.LeaveTypeCode == "YILLIK");
        waiting.ShouldSatisfyAllConditions(b => b.Pending.ShouldBe(3), b => b.Available.ShouldBe(11));

        await factory.DeliverMessagesAsync();
        await DecideAsync(Client(team.Manager), HrApprovalDocumentTypes.LeaveRequest, id, "Approve");
        await factory.DeliverMessagesAsync();

        (await MyRequestAsync(employee, id)).Request.Status.ShouldBe(LeaveRequestStatus.Approved);
        var taken = (await MyBalancesAsync(employee)).Single(b => b.LeaveTypeCode == "YILLIK");
        taken.ShouldSatisfyAllConditions(b => b.Balance.ShouldBe(11), b => b.Used.ShouldBe(3), b => b.Pending.ShouldBe(0));
        taken.Entries[0].Source.ShouldBe(LeaveLedgerSource.Request);
        var notifications = await factory.OutboxAsync<NotificationRequested>();
        notifications.ShouldContain(n => n.Type == HrNotificationTypes.LeaveRequestSubmitted && n.RecipientUserIds.Contains(team.Employee.UserId) && n.Args["number"] == asked.Request.Number);
        notifications.ShouldContain(n => n.Type == HrNotificationTypes.LeaveRequestCompleted && n.RecipientUserIds.Contains(team.Employee.UserId) && n.Args["number"] == asked.Request.Number);
    }

    [Fact]
    public async Task ASixDayLeave_GoesToHrInTheSecondStep()
    {
        // PLAN L4 acceptance: more than five days needs the HR specialist after the manager
        var team = await TeamAsync();
        var specialist = await SpecialistAsync();
        var employee = Self(team.Employee);
        var id = await HrFactory.CreatedIdAsync(await employee.PostAsJsonAsync(
            Mine,
            new { leaveTypeId = await TypeIdAsync(team.Admin, "YILLIK"), startDate = "2026-02-09", endDate = "2026-02-16", submit = true },
            Ct));
        (await MyRequestAsync(employee, id)).Request.Days.ShouldBe(6);
        await factory.DeliverMessagesAsync();

        await DecideAsync(Client(team.Manager), HrApprovalDocumentTypes.LeaveRequest, id, "Approve");
        await factory.DeliverMessagesAsync();
        (await MyRequestAsync(employee, id)).Request.Status.ShouldBe(LeaveRequestStatus.Pending);

        await DecideAsync(specialist, HrApprovalDocumentTypes.LeaveRequest, id, "Approve");
        await factory.DeliverMessagesAsync();
        (await MyRequestAsync(employee, id)).Request.Status.ShouldBe(LeaveRequestStatus.Approved);
    }

    [Fact]
    public async Task AReturnedRequest_IsCorrectedAndSentAgain()
    {
        // PLAN L4 acceptance: a returned request is corrected and approved in a new round
        var team = await TeamAsync();
        var employee = Self(team.Employee);
        var annual = await TypeIdAsync(team.Admin, "YILLIK");
        var id = await HrFactory.CreatedIdAsync(await employee.PostAsJsonAsync(Mine, new { leaveTypeId = annual, startDate = "2026-03-02", endDate = "2026-03-04", submit = true }, Ct));
        await factory.DeliverMessagesAsync();

        await DecideAsync(Client(team.Manager), HrApprovalDocumentTypes.LeaveRequest, id, "Return", "Ekip o hafta denetimde, bir hafta sonraya alın");
        await factory.DeliverMessagesAsync();
        var returned = await MyRequestAsync(employee, id);
        returned.ShouldSatisfyAllConditions(
            r => r.Request.Status.ShouldBe(LeaveRequestStatus.Returned),
            r => r.ReturnComment.ShouldBe("Ekip o hafta denetimde, bir hafta sonraya alın"),
            r => r.Actions.Edit.ShouldBeTrue());

        (await employee.PutAsJsonAsync($"{Mine}/{id}", new { leaveTypeId = annual, startDate = "2026-03-09", endDate = "2026-03-11", submit = true }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var resent = await MyRequestAsync(employee, id);
        resent.Request.ShouldSatisfyAllConditions(r => r.Status.ShouldBe(LeaveRequestStatus.Pending), r => r.StartDate.ShouldBe(new DateOnly(2026, 3, 9)));
        await factory.DeliverMessagesAsync();

        await DecideAsync(Client(team.Manager), HrApprovalDocumentTypes.LeaveRequest, id, "Approve");
        await factory.DeliverMessagesAsync();
        (await MyRequestAsync(employee, id)).Request.Status.ShouldBe(LeaveRequestStatus.Approved);
    }

    [Fact]
    public async Task CancellingAnApprovedLeave_RestoresTheBalance()
    {
        // PLAN L4 acceptance and E12: −3 Request, +3 RequestCancel
        var team = await TeamAsync();
        var employee = Self(team.Employee);
        var id = await ApprovedAsync(team, "2026-04-06", "2026-04-08");

        (await employee.PostAsJsonAsync($"{Mine}/{id}/cancellation", new { reason = "Seyahat iptal oldu" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await MyRequestAsync(employee, id)).Request.Status.ShouldBe(LeaveRequestStatus.CancellationPending);
        (await factory.OutboxAsync<ApprovalRequested>()).ShouldContain(e => e.DocumentType == HrApprovalDocumentTypes.LeaveCancellation && e.DocumentId == id.ToString());
        await factory.DeliverMessagesAsync();

        await DecideAsync(Client(team.Manager), HrApprovalDocumentTypes.LeaveCancellation, id, "Approve");
        await factory.DeliverMessagesAsync();

        (await MyRequestAsync(employee, id)).Request.Status.ShouldBe(LeaveRequestStatus.Cancelled);
        var annual = (await MyBalancesAsync(employee)).Single(b => b.LeaveTypeCode == "YILLIK");
        annual.Balance.ShouldBe(14);
        annual.Entries.Select(e => (e.Source, e.Quantity)).ShouldContain((LeaveLedgerSource.Request, -3m));
        annual.Entries.Select(e => (e.Source, e.Quantity)).ShouldContain((LeaveLedgerSource.RequestCancel, 3m));
        // A cancellation is asked once
        (await employee.PostAsJsonAsync($"{Mine}/{id}/cancellation", new { reason = "Tekrar" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task E13_AnOverlappingRequest_IsRefused_AndAPendingOneCanBeCancelled()
    {
        var team = await TeamAsync();
        var employee = Self(team.Employee);
        var annual = await TypeIdAsync(team.Admin, "YILLIK");
        var first = await HrFactory.CreatedIdAsync(await employee.PostAsJsonAsync(Mine, new { leaveTypeId = annual, startDate = "2026-05-04", endDate = "2026-05-06", submit = true }, Ct));

        var clash = await employee.PostAsJsonAsync(Mine, new { leaveTypeId = annual, startDate = "2026-05-06", endDate = "2026-05-08", submit = true }, Ct);

        clash.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(clash)).Errors!.Keys.ShouldBe(["startDate"]);
        (await employee.PostAsJsonAsync($"{Mine}/{first}/cancel", new { reason = "Vazgeçtim" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await factory.OutboxAsync<ApprovalCancellationRequested>()).ShouldContain(e => e.DocumentType == HrApprovalDocumentTypes.LeaveRequest && e.DocumentId == first.ToString());
        (await employee.PostAsJsonAsync(Mine, new { leaveTypeId = annual, startDate = "2026-05-06", endDate = "2026-05-08", submit = true }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ARequest_IsValidated_AndNeedsThePermission()
    {
        var team = await TeamAsync();
        var employee = Self(team.Employee);
        var annual = await TypeIdAsync(team.Admin, "YILLIK");

        var inverted = await employee.PostAsJsonAsync(Mine, new { leaveTypeId = annual, startDate = "2026-02-10", endDate = "2026-02-02", submit = true }, Ct);
        inverted.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(inverted)).Errors!.Keys.ShouldBe(["endDate"]);

        // Twenty working days do not fit fourteen
        var tooLong = await employee.PostAsJsonAsync(Mine, new { leaveTypeId = annual, startDate = "2026-06-01", endDate = "2026-06-26", submit = true }, Ct);
        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(tooLong)).Errors!.Keys.ShouldBe(["days"]);

        var past = await employee.PostAsJsonAsync(Mine, new { leaveTypeId = annual, startDate = "2025-12-15", endDate = "2025-12-16", submit = true }, Ct);
        (await Problem.ReadAsync(past)).Errors!.Keys.ShouldBe(["startDate"]);

        var readOnly = factory.CreateClientFor(team.Employee.Subject, HrFactory.Tenant, HrPermissions.Self.LeaveRead);
        (await readOnly.PostAsJsonAsync(Mine, new { leaveTypeId = annual, startDate = "2026-02-02", endDate = "2026-02-04" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var unlinked = factory.CreateClientFor("u" + Guid.NewGuid().ToString("N")[..10], HrFactory.Tenant, HrPermissions.Self.LeaveRead, HrPermissions.Self.LeaveRequest);
        (await unlinked.PostAsJsonAsync(Mine, new { leaveTypeId = annual, startDate = "2026-02-02", endDate = "2026-02-04" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ThePreview_GivesTheLength_TheBalanceAfter_TheWarningsAndTheApprovers()
    {
        var team = await TeamAsync();
        var employee = Self(team.Employee);

        var response = await employee.PostAsJsonAsync(
            $"{Mine}/preview",
            new { leaveTypeId = await TypeIdAsync(team.Admin, "YILLIK"), startDate = "2026-02-09", endDate = "2026-02-16" },
            Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var preview = (await response.Content.ReadFromJsonAsync<LeaveRequestPreview>(Ct))!;

        preview.ShouldSatisfyAllConditions(
            p => p.Problem.ShouldBeNull(),
            p => p.Days.ShouldBe(6),
            p => p.Available.ShouldBe(14),
            p => p.AvailableAfter.ShouldBe(8),
            p => p.Warnings.ShouldContain(w => w.Code == LeaveRequestCheckErrors.TenDayBlockCode),
            p => p.Approval.ShouldNotBeNull());
        preview.Approval!.Steps[0].Approvers.ShouldHaveSingleItem().UserId.ShouldBe(team.Manager.UserId);
    }

    [Fact]
    public async Task ADocumentLeave_IsSentOnlyWithItsDocument()
    {
        var team = await TeamAsync();
        var employee = Self(team.Employee);
        var marriage = await TypeIdAsync(team.Admin, "EVLILIK");

        var without = await employee.PostAsJsonAsync(Mine, new { leaveTypeId = marriage, startDate = "2026-07-03", submit = true }, Ct);
        without.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(without)).Errors!.Keys.ShouldBe(["attachments"]);

        var id = await HrFactory.CreatedIdAsync(await employee.PostAsJsonAsync(Mine, new { leaveTypeId = marriage, startDate = "2026-07-03" }, Ct));
        (await employee.PostAsync($"{Mine}/{id}/submit", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var file = new ByteArrayContent(SampleFiles.Pdf());
        file.Headers.ContentType = new("application/pdf");
        await HrFactory.CreatedIdAsync(await employee.PostAsync($"{Mine}/{id}/files", new MultipartFormDataContent { { file, "file", "nikah.pdf" } }, Ct));

        (await employee.PostAsync($"{Mine}/{id}/submit", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var sent = await MyRequestAsync(employee, id);
        sent.ShouldSatisfyAllConditions(
            r => r.Request.Status.ShouldBe(LeaveRequestStatus.Pending),
            r => r.Request.EndDate.ShouldBe(new DateOnly(2026, 7, 5)),
            r => r.Files.ShouldHaveSingleItem().FileName.ShouldBe("nikah.pdf"));
    }

    [Fact]
    public async Task HrSeesTheRequestsInScope_AndTheManagerOnlyThoseOfTheTeam()
    {
        var team = await TeamAsync();
        var id = await HrFactory.CreatedIdAsync(await Self(team.Employee).PostAsJsonAsync(
            Mine,
            new { leaveTypeId = await TypeIdAsync(team.Admin, "YILLIK"), startDate = "2026-08-03", endDate = "2026-08-05", submit = true },
            Ct));
        var outsider = await TeamAsync();
        var otherId = await HrFactory.CreatedIdAsync(await Self(outsider.Employee).PostAsJsonAsync(
            Mine,
            new { leaveTypeId = await TypeIdAsync(team.Admin, "YILLIK"), startDate = "2026-08-03", endDate = "2026-08-05", submit = true },
            Ct));

        var page = (await team.Admin.GetFromJsonAsync<PagedResult<LeaveRequestListItem>>($"{Hr}?employeeId={team.Employee.Employee}&status=Pending", Ct))!;
        page.Items.ShouldHaveSingleItem().Id.ShouldBe(id);

        var manager = factory.CreateClientFor(team.Manager.Subject, HrFactory.Tenant, HrPermissions.Leave.Requests.Read)
            .WithScopes(new AccessScope(AccessScopeTypes.ManagedTeam));
        (await manager.GetAsync($"{Hr}/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await manager.GetAsync($"{Hr}/{otherId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var scoped = factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Leave.Requests.Read).WithScopes(AccessScope.Company(outsider.Setup.CompanyId));
        (await scoped.GetAsync($"{Hr}/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Leave.Balances.Read).GetAsync(Hr, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task HrRecordsAPastLeave_AndEndsAStartedOneEarly()
    {
        var team = await TeamAsync();
        var annual = await TypeIdAsync(team.Admin, "YILLIK");

        var past = await HrFactory.CreatedIdAsync(await team.Admin.PostAsJsonAsync(
            $"{Hr}/records",
            new { employmentId = team.Employee.Employment, leaveTypeId = annual, startDate = "2025-12-15", endDate = "2025-12-17" },
            Ct));
        var started = await HrFactory.CreatedIdAsync(await team.Admin.PostAsJsonAsync(
            $"{Hr}/records",
            new { employmentId = team.Employee.Employment, leaveTypeId = annual, startDate = "2026-01-05", endDate = "2026-01-09" },
            Ct));
        (await HrRequestAsync(team.Admin, past)).Request.ShouldSatisfyAllConditions(r => r.Status.ShouldBe(LeaveRequestStatus.Approved), r => r.Origin.ShouldBe(LeaveRequestOrigin.Record));
        (await BalanceAsync(team)).Balance.ShouldBe(6);

        (await team.Admin.PostAsJsonAsync($"{Hr}/{started}/early-return", new { endDate = "2026-01-06" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var shortened = await HrRequestAsync(team.Admin, started);
        shortened.ShouldSatisfyAllConditions(r => r.Request.EndDate.ShouldBe(new DateOnly(2026, 1, 6)), r => r.OriginalEndDate.ShouldBe(new DateOnly(2026, 1, 9)));
        (await BalanceAsync(team)).Balance.ShouldBe(9);
        // Recording needs its own permission
        var manager = factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Leave.Requests.Manage);
        (await manager.PostAsJsonAsync($"{Hr}/records", new { employmentId = team.Employee.Employment, leaveTypeId = annual, startDate = "2025-12-22" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task HrAsksOnBehalf_AndCancelsAnApprovedLeave()
    {
        var team = await TeamAsync();
        var annual = await TypeIdAsync(team.Admin, "YILLIK");
        var id = await HrFactory.CreatedIdAsync(await team.Admin.PostAsJsonAsync(
            Hr,
            new { employmentId = team.Employee.Employment, leaveTypeId = annual, startDate = "2026-09-07", endDate = "2026-09-09" },
            Ct));
        (await HrRequestAsync(team.Admin, id)).Request.ShouldSatisfyAllConditions(r => r.Status.ShouldBe(LeaveRequestStatus.Pending), r => r.Origin.ShouldBe(LeaveRequestOrigin.OnBehalf));
        await factory.DeliverMessagesAsync();
        await DecideAsync(Client(team.Manager), HrApprovalDocumentTypes.LeaveRequest, id, "Approve");
        await factory.DeliverMessagesAsync();
        (await BalanceAsync(team)).Balance.ShouldBe(11);

        (await team.Admin.PostAsJsonAsync($"{Hr}/{id}/cancel", new { reason = string.Empty }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await team.Admin.PostAsJsonAsync($"{Hr}/{id}/cancel", new { reason = "Proje takvimi değişti" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await HrRequestAsync(team.Admin, id)).Request.Status.ShouldBe(LeaveRequestStatus.Cancelled);
        (await BalanceAsync(team)).Balance.ShouldBe(14);
    }

    [Fact]
    public async Task ABulkLeave_IsRecordedForThoseItFits()
    {
        var team = await TeamAsync();
        var colleague = await LinkedAsync(team.Admin, team.Setup, team.Setup.PositionB);
        var body = new
        {
            companyId = team.Setup.CompanyId,
            employmentIds = new[] { team.Employee.Employment, colleague.Employment },
            includeSubUnits = false,
            leaveTypeId = await TypeIdAsync(team.Admin, "YILLIK"),
            startDate = "2026-10-26",
            endDate = "2026-10-30",
            note = "Bayram köprüsü",
        };

        var preview = (await (await team.Admin.PostAsJsonAsync($"{Hr}/bulk/preview", body, Ct)).Content.ReadFromJsonAsync<List<BulkLeaveRow>>(Ct))!;
        preview.Single(r => r.EmploymentId == team.Employee.Employment).ShouldSatisfyAllConditions(r => r.Problem.ShouldBeNull(), r => r.Days.ShouldBe(3.5m));
        preview.Single(r => r.EmploymentId == colleague.Employment).Problem.ShouldNotBeNull().Field.ShouldBe("days");

        var response = await team.Admin.PostAsJsonAsync($"{Hr}/bulk", body, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var rows = (await response.Content.ReadFromJsonAsync<List<BulkLeaveRow>>(Ct))!;

        var recorded = rows.Single(r => r.EmploymentId == team.Employee.Employment).RequestId.ShouldNotBeNull();
        (await HrRequestAsync(team.Admin, recorded)).Request.ShouldSatisfyAllConditions(r => r.Origin.ShouldBe(LeaveRequestOrigin.Bulk), r => r.Status.ShouldBe(LeaveRequestStatus.Approved));
        rows.Single(r => r.EmploymentId == colleague.Employment).RequestId.ShouldBeNull();
        (await factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Leave.Requests.Manage).PostAsJsonAsync($"{Hr}/bulk/preview", body, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TheDayBefore_TheEmployeeAndTheManagerAreReminded_Once()
    {
        var team = await TeamAsync();
        var id = await HrFactory.CreatedIdAsync(await team.Admin.PostAsJsonAsync(
            $"{Hr}/records",
            new { employmentId = team.Employee.Employment, leaveTypeId = await TypeIdAsync(team.Admin, "YILLIK"), startDate = "2026-01-06", endDate = "2026-01-07" },
            Ct));
        var number = (await HrRequestAsync(team.Admin, id)).Request.Number;

        (await RemindAsync()).ShouldBeGreaterThanOrEqualTo(1);
        (await RemindAsync()).ShouldBe(0);

        var reminder = (await factory.OutboxAsync<NotificationRequested>()).Where(n => n.Type == HrNotificationTypes.LeaveReminder && n.Args["number"] == number).ToList();
        reminder.SelectMany(n => n.RecipientUserIds).ShouldBe([team.Employee.UserId, team.Manager.UserId], ignoreOrder: true);
    }

    private sealed record Person(Guid Employee, Guid Employment, Guid UserId, string Subject);

    private sealed record Team(HttpClient Admin, EmployeeSetup Setup, Person Manager, Person Employee);

    /// <summary>A manager and an employee reporting to them, both with users; the employee has fourteen days.</summary>
    private async Task<Team> TeamAsync()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var managerPosition = await HrFactory.CreatePositionAsync(admin, setup.UnitId, setup.JobId);
        var staffPosition = await HrFactory.CreatePositionAsync(admin, setup.UnitId, setup.JobId, reportsTo: managerPosition, norm: 5);
        var manager = await LinkedAsync(admin, setup, managerPosition);
        var employee = await LinkedAsync(admin, setup, staffPosition);
        (await admin.PostAsJsonAsync(
            $"/api/hr/employees/{employee.Employee}/employments/{employee.Employment}/leave-balances/adjustments",
            new { leaveTypeCode = "YILLIK", date = "2025-12-31", quantity = 14, note = "Devir" },
            Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return new Team(admin, setup with { PositionB = staffPosition }, manager, employee);
    }

    private async Task<Person> LinkedAsync(HttpClient admin, EmployeeSetup setup, Guid positionId)
    {
        var employee = await setup.HireAsync(admin, new DateOnly(2025, 12, 1), new { positionId });
        var email = $"personel.{Guid.NewGuid().ToString("N")[..10]}@acme.test";
        (await admin.PostAsJsonAsync($"/api/hr/employees/{employee}/user", new { email }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await factory.DeliverMessagesAsync();
        var subject = factory.Identity.Identities.Single(i => i.Value.Email == email).Key;
        var userId = (await Client(subject).GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!.User.Id!.Value;
        var employmentId = (await EmployeeSetup.LoadAsync(factory, employee)).Employments.Single().Id.Value;
        return new Person(employee, employmentId, userId, subject);
    }

    private HttpClient Client(Person person) => Client(person.Subject);

    private HttpClient Client(string subject) => factory.CreateClientFor(subject, HrFactory.Tenant);

    private HttpClient Self(Person person) =>
        factory.CreateClientFor(person.Subject, HrFactory.Tenant, HrPermissions.Self.LeaveRead, HrPermissions.Self.LeaveRequest);

    private async Task<Guid> ApprovedAsync(Team team, string start, string end)
    {
        var id = await HrFactory.CreatedIdAsync(await Self(team.Employee).PostAsJsonAsync(
            Mine,
            new { leaveTypeId = await TypeIdAsync(team.Admin, "YILLIK"), startDate = start, endDate = end, submit = true },
            Ct));
        await factory.DeliverMessagesAsync();
        await DecideAsync(Client(team.Manager), HrApprovalDocumentTypes.LeaveRequest, id, "Approve");
        await factory.DeliverMessagesAsync();
        return id;
    }

    /// <summary>A user holding the HR specialist system role, the approver of the second step.</summary>
    private async Task<HttpClient> SpecialistAsync()
    {
        var client = factory.CreateClientFor("uzman" + Guid.NewGuid().ToString("N")[..8], HrFactory.Tenant);
        var userId = (await client.GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!.User.Id!.Value;
        var users = factory.CreateClientFor("kullanici-yoneticisi", HrFactory.Tenant, "platform.roles.read", "platform.users.manage");
        var role = (await users.GetFromJsonAsync<PagedResult<RoleListItem>>("/api/platform/roles?pageSize=200", Ct))!
            .Items.Single(r => string.Equals(r.Code, "hr-specialist", StringComparison.OrdinalIgnoreCase));
        await HrFactory.CreatedIdAsync(await users.PostAsJsonAsync(
            $"/api/platform/users/{userId}/assignments",
            new { roleId = role.Id, scopeType = AccessScopeTypes.Tenant, includeSubUnits = false },
            Ct));
        return client;
    }

    private static async Task DecideAsync(HttpClient approver, string documentType, Guid requestId, string decision, string? comment = null)
    {
        var task = (await approver.GetFromJsonAsync<PagedResult<MyApprovalTaskItem>>("/api/platform/approvals/my-tasks?pageSize=200", Ct))!
            .Items.Single(t => t.DocumentType == documentType && t.DocumentId == requestId.ToString());
        var response = await approver.PostAsJsonAsync($"/api/platform/approvals/tasks/{task.TaskId}/decision", new { decision, comment }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<Guid> TypeIdAsync(HttpClient client, string code) =>
        (await client.GetFromJsonAsync<List<LeaveTypeItem>>("/api/hr/leave/types", Ct))!.Single(t => t.Code == code).Id;

    private static async Task<LeaveRequestDetails> MyRequestAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"{Mine}/{id}", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<LeaveRequestDetails>(Ct))!;
    }

    private static async Task<LeaveRequestDetails> HrRequestAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"{Hr}/{id}", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<LeaveRequestDetails>(Ct))!;
    }

    private static async Task<List<LeaveBalanceItem>> MyBalancesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<LeaveBalanceItem>>("/api/hr/me/leave-balances", Ct))!;

    private static async Task<LeaveBalanceItem> BalanceAsync(Team team) =>
        (await team.Admin.GetFromJsonAsync<List<LeaveBalanceItem>>(
            $"/api/hr/employees/{team.Employee.Employee}/employments/{team.Employee.Employment}/leave-balances",
            Ct))!.Single(b => b.LeaveTypeCode == "YILLIK");

    private Task<int> RemindAsync() =>
        factory.InTenantAsync(async services => (await services.GetRequiredService<ISender>().Send(new SendLeaveRemindersCommand(), Ct)).Value);
}
