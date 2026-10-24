using System.Net;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Security;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.Leave;
using Merp.Modules.HR.Domain.Leave;
using Merp.Modules.HR.IntegrationTests.Employees;
using Merp.Modules.HR.IntegrationTests.Organization;
using Merp.Testing.Web;

namespace Merp.Modules.HR.IntegrationTests.Leave;

/// <summary>The leave calendar (PLAN L5, D45): scope, team, pending requests, holidays and crowded days.</summary>
public sealed class LeaveCalendarTests(HrClockFactory factory) : IClassFixture<HrClockFactory>
{
    private const string Url = "/api/hr/leave/calendar";
    private const string May = "from=2026-05-01&to=2026-05-31";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HrSeesEveryoneInScope_WithTheHolidays_AndThePendingRequestsUnlessLeftOut()
    {
        var unit = await UnitAsync();

        var calendar = await CalendarAsync(unit.Admin, $"{Url}?{May}&companyId={unit.Setup.CompanyId}");

        calendar.Events.Select(e => e.EmployeeId).ShouldBe([unit.First, unit.Second, unit.Outsider, unit.First], ignoreOrder: true);
        calendar.Events.Single(e => e.Status == LeaveRequestStatus.Pending).EmployeeId.ShouldBe(unit.First);
        calendar.Events.ShouldAllBe(e => e.OrgUnitId == unit.Setup.UnitId && e.LeaveTypeCode == "IDARI" && e.Category == LeaveCategory.Administrative);
        calendar.Holidays.Select(h => h.Date).ShouldContain(new DateOnly(2026, 5, 19));
        calendar.Holidays.ShouldContain(h => h.Date == new DateOnly(2026, 5, 26) && h.HalfDay);

        var approvedOnly = await CalendarAsync(unit.Admin, $"{Url}?{May}&companyId={unit.Setup.CompanyId}&includePending=false");
        approvedOnly.Events.ShouldAllBe(e => e.Status == LeaveRequestStatus.Approved);
        approvedOnly.Events.Count.ShouldBe(3);
    }

    [Fact]
    public async Task AManager_SeesOnlyTheTeam()
    {
        // PLAN L5 acceptance: the manager role's ManagedTeam scope shows the subordinates only
        var unit = await UnitAsync();
        var manager = factory.CreateClientFor(unit.ManagerSubject, HrFactory.Tenant, HrPermissions.Leave.Calendar.Read)
            .WithScopes(new AccessScope(AccessScopeTypes.ManagedTeam));

        var calendar = await CalendarAsync(manager, $"{Url}?{May}");

        calendar.Events.Select(e => e.EmployeeId).Distinct().ShouldBe([unit.First, unit.Second], ignoreOrder: true);

        // With a wider scope, "my team" still narrows to the people below the caller
        var wide = factory.CreateClientFor(unit.ManagerSubject, HrFactory.Tenant, HrPermissions.Leave.Calendar.Read)
            .WithScopes(AccessScope.Company(unit.Setup.CompanyId));
        (await CalendarAsync(wide, $"{Url}?{May}")).Events.ShouldContain(e => e.EmployeeId == unit.Outsider);
        (await CalendarAsync(wide, $"{Url}?{May}&myTeam=true")).Events.ShouldNotContain(e => e.EmployeeId == unit.Outsider);
    }

    [Fact]
    public async Task ACrowdedDay_IsFlagged_AboveTheThreshold()
    {
        var unit = await UnitAsync();
        var settings = factory.CreateClientFor("admin", HrFactory.Tenant, "platform.settings.manage");
        (await settings.PutAsJsonAsync("/api/platform/settings", new { values = new Dictionary<string, object?> { ["hr.leave.calendar_threshold"] = 2 } }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var calendar = await CalendarAsync(unit.Admin, $"{Url}?{May}&companyId={unit.Setup.CompanyId}");

        var alert = calendar.Alerts.ShouldHaveSingleItem();
        alert.ShouldSatisfyAllConditions(a => a.Date.ShouldBe(new DateOnly(2026, 5, 5)), a => a.Count.ShouldBe(3), a => a.Threshold.ShouldBe(2), a => a.OrgUnitId.ShouldBe(unit.Setup.UnitId));
    }

    [Fact]
    public async Task TheRange_IsLimited_AndThePermissionRequired()
    {
        var admin = factory.Admin();

        var tooLong = await admin.GetAsync($"{Url}?from=2026-01-01&to=2026-06-30", Ct);
        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.GetAsync($"{Url}?from=2026-05-31&to=2026-05-01", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Leave.Requests.Read).GetAsync($"{Url}?{May}", Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private sealed record Unit(HttpClient Admin, EmployeeSetup Setup, string ManagerSubject, Guid First, Guid Second, Guid Outsider);

    /// <summary>
    /// A manager with two people below them and a colleague outside the team, all in one unit: administrative leaves
    /// of the two on 4–6 May and of the colleague on 5 May, approved, and one of the first waiting on 12 May.
    /// </summary>
    private async Task<Unit> UnitAsync()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var managerPosition = await HrFactory.CreatePositionAsync(admin, setup.UnitId, setup.JobId);
        var staffPosition = await HrFactory.CreatePositionAsync(admin, setup.UnitId, setup.JobId, reportsTo: managerPosition, norm: 5);
        var manager = await setup.HireAsync(admin, new DateOnly(2025, 12, 1), new { positionId = managerPosition });
        var email = $"yonetici.{Guid.NewGuid().ToString("N")[..10]}@acme.test";
        (await admin.PostAsJsonAsync($"/api/hr/employees/{manager}/user", new { email }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await factory.DeliverMessagesAsync();
        var subject = factory.Identity.Identities.Single(i => i.Value.Email == email).Key;

        var first = await setup.HireAsync(admin, new DateOnly(2025, 12, 1), new { positionId = staffPosition });
        var second = await setup.HireAsync(admin, new DateOnly(2025, 12, 1), new { positionId = staffPosition });
        var outsider = await setup.HireAsync(admin, new DateOnly(2025, 12, 1));
        var administrative = (await admin.GetFromJsonAsync<List<LeaveTypeItem>>("/api/hr/leave/types", Ct))!.Single(t => t.Code == "IDARI").Id;

        async Task<Guid> EmploymentAsync(Guid employee) => (await EmployeeSetup.LoadAsync(factory, employee)).Employments.Single().Id.Value;
        async Task RecordAsync(Guid employee, string start, string end) =>
            await HrFactory.CreatedIdAsync(await admin.PostAsJsonAsync(
                "/api/hr/leave-requests/records",
                new { employmentId = await EmploymentAsync(employee), leaveTypeId = administrative, startDate = start, endDate = end },
                Ct));

        await RecordAsync(first, "2026-05-04", "2026-05-06");
        await RecordAsync(second, "2026-05-04", "2026-05-06");
        await RecordAsync(outsider, "2026-05-05", "2026-05-05");
        await HrFactory.CreatedIdAsync(await admin.PostAsJsonAsync(
            "/api/hr/leave-requests",
            new { employmentId = await EmploymentAsync(first), leaveTypeId = administrative, startDate = "2026-05-12", endDate = "2026-05-12" },
            Ct));
        return new Unit(admin, setup, subject, first, second, outsider);
    }

    private static async Task<LeaveCalendar> CalendarAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<LeaveCalendar>(Ct))!;
    }
}
