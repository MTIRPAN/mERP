using System.Net;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Security;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.Leave;
using Merp.Modules.HR.IntegrationTests.Employees;
using Merp.Modules.HR.IntegrationTests.Organization;
using Merp.Modules.Platform.Api.Endpoints;
using Merp.Modules.Platform.Application.Assistant;
using Merp.Testing.Web;

namespace Merp.Modules.HR.IntegrationTests.Leave;

/// <summary>
/// HR's assistant actions (PLAN A12, D65): the employee's leave requests and balances, the manager's approval tasks
/// and the employee search, each run as the user within their permissions and data scope. The fake clock stands on
/// Monday 5 January 2026.
/// </summary>
public sealed class LeaveAssistantTests : IClassFixture<HrClockFactory>
{
    private const string Url = "/api/platform/assistant/ask";

    private readonly HrClockFactory _factory;

    public LeaveAssistantTests(HrClockFactory factory)
    {
        _factory = factory;
        _factory.Ai.Reset();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AssistantPlan Action(string key, params AssistantPlanArgument[] arguments) =>
        new(AssistantPlanKind.Action, key, arguments, string.Empty, "Başlık", string.Empty);

    [Fact]
    public async Task TheEmployee_SeesTheirRequestsAndBalances_AndTheManager_TheTaskWaitingForThem()
    {
        var team = await TeamAsync();
        var employee = Self(team.Employee.Subject);
        var annual = (await employee.GetFromJsonAsync<List<LeaveTypeOption>>("/api/hr/me/leave-types", Ct))!.Single(t => t.Code == "YILLIK");
        var id = await HrFactory.CreatedIdAsync(await employee.PostAsJsonAsync(
            "/api/hr/me/leave-requests",
            new { leaveTypeId = annual.Id, startDate = "2026-02-02", endDate = "2026-02-04", submit = true },
            Ct));
        await _factory.DeliverMessagesAsync();
        _factory.Ai.Returns(Action("hr.my_leave_requests", new AssistantPlanArgument("status", "Pending")));
        _factory.Ai.Returns(Action("hr.my_leave_requests", new AssistantPlanArgument("status", "Approved")));
        _factory.Ai.Returns(Action("hr.my_leave_balances"));
        _factory.Ai.Returns(Action("platform.my_approval_tasks"));

        var pending = (await AskAsync(employee, "Onay bekleyen izin taleplerim")).Answer!;
        var approved = (await AskAsync(employee, "Onaylanan izinlerim")).Answer!;
        var balances = (await AskAsync(employee, "Kaç gün iznim kaldı?")).Answer!;
        var tasks = (await AskAsync(Client(team.Manager.Subject), "Onayımı bekleyenler")).Answer!;

        pending.Total.ShouldBe(1);
        pending.Items.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            i => i.Title.ShouldStartWith("Yıllık ücretli izin · IZN-2026-"),
            i => i.Detail.ShouldBe("2 Şubat 2026 – 4 Şubat 2026 · Onay bekliyor"),
            i => i.Link.ShouldBe($"/hr/me/leave?request={id}"));
        pending.Link.ShouldBe("/hr/me/leave");
        approved.Total.ShouldBe(0);
        balances.Items.ShouldContain(i => i.Title == "Yıllık ücretli izin" && i.Detail == "Kullanılabilir: 11 · Kullanılan: 0 · Onay bekleyen: 3");
        tasks.Total.ShouldBe(1);
        tasks.Items.ShouldHaveSingleItem().Link.ShouldStartWith("/approvals/requests/");
        var inputs = _factory.Ai.Requests.Select(r => r.Input).ToList();
        inputs[0].ShouldSatisfyAllConditions(
            i => i.ShouldContain("<action key=\"hr.my_leave_requests\">"),
            i => i.ShouldContain("values=\"Draft, Pending, Returned, Approved, Rejected, Cancelled, CancellationPending\""),
            i => i.ShouldNotContain("<action key=\"hr.employees\">"),
            i => i.ShouldNotContain("IZN-2026-"));
    }

    [Fact]
    public async Task TheEmployeeSearch_StaysInTheUsersDataScope()
    {
        var team = await TeamAsync();
        var elsewhere = await HrFactory.CreateCompanyAsync(team.Admin);
        var everywhere = _factory.CreateClientFor("ik-asistan", HrFactory.Tenant, HrPermissions.Employees.Read);
        var otherCompany = _factory.CreateClientFor("ik-asistan-diger", HrFactory.Tenant, HrPermissions.Employees.Read).WithScopes(AccessScope.Company(elsewhere));
        _factory.Ai.Returns(Action("hr.employees", new AssistantPlanArgument("search", "Ayşe")));
        _factory.Ai.Returns(Action("hr.employees", new AssistantPlanArgument("search", "Ayşe")));

        var found = (await AskAsync(everywhere, "Ayşe kim?")).Answer!;
        var hidden = (await AskAsync(otherCompany, "Ayşe kim?")).Answer!;

        found.Total.ShouldBeGreaterThan(0);
        found.Items.ShouldAllBe(i => i.Title == "Ayşe Yılmaz" && i.Link!.StartsWith("/hr/employees/", StringComparison.Ordinal));
        found.Link.ShouldBe("/hr/employees?search=Ay%C5%9Fe");
        hidden.ShouldSatisfyAllConditions(h => h.Total.ShouldBe(0), h => h.Items.ShouldBeEmpty());
        _factory.Ai.Requests.First().Input.ShouldContain("<action key=\"hr.employees\">");
    }

    private sealed record Person(Guid EmployeeId, string Subject);

    private sealed record Team(HttpClient Admin, Person Manager, Person Employee);

    /// <summary>A manager and an employee reporting to them, both with users; the employee has fourteen days.</summary>
    private async Task<Team> TeamAsync()
    {
        var admin = _factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var managerPosition = await HrFactory.CreatePositionAsync(admin, setup.UnitId, setup.JobId);
        var staffPosition = await HrFactory.CreatePositionAsync(admin, setup.UnitId, setup.JobId, reportsTo: managerPosition, norm: 5);
        var manager = await LinkedAsync(admin, setup, managerPosition);
        var employee = await LinkedAsync(admin, setup, staffPosition);
        var employmentId = (await EmployeeSetup.LoadAsync(_factory, employee.EmployeeId)).Employments.Single().Id.Value;
        (await admin.PostAsJsonAsync(
            $"/api/hr/employees/{employee.EmployeeId}/employments/{employmentId}/leave-balances/adjustments",
            new { leaveTypeCode = "YILLIK", date = "2025-12-31", quantity = 14, note = "Devir" },
            Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return new Team(admin, manager, employee);
    }

    private async Task<Person> LinkedAsync(HttpClient admin, EmployeeSetup setup, Guid positionId)
    {
        var employee = await setup.HireAsync(admin, new DateOnly(2025, 12, 1), new { positionId });
        var email = $"personel.{Guid.NewGuid().ToString("N")[..10]}@acme.test";
        (await admin.PostAsJsonAsync($"/api/hr/employees/{employee}/user", new { email }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await _factory.DeliverMessagesAsync();
        var subject = _factory.Identity.Identities.Single(i => i.Value.Email == email).Key;
        (await Client(subject).GetFromJsonAsync<MeResponse>("/api/platform/me", Ct)).ShouldNotBeNull();
        return new Person(employee, subject);
    }

    private HttpClient Client(string subject) => _factory.CreateClientFor(subject, HrFactory.Tenant);

    private HttpClient Self(string subject) =>
        _factory.CreateClientFor(subject, HrFactory.Tenant, HrPermissions.Self.LeaveRead, HrPermissions.Self.LeaveRequest);

    private static async Task<AssistantResponse> AskAsync(HttpClient client, string text)
    {
        var response = await client.PostAsJsonAsync(Url, new { text }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var answer = (await response.Content.ReadFromJsonAsync<AssistantResponse>(Ct))!;
        answer.Kind.ShouldBe(AssistantResponseKind.Answer);
        return answer;
    }
}
