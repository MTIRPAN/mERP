using System.Net.Http.Json;
using Merp.Modules.HR.Application;
using Merp.Modules.Platform.Api.Endpoints;

namespace Merp.Modules.HR.IntegrationTests.Organization;

/// <summary>The HR screens in the web shell's menu (PLAN H1b, H2c, H3c, L4b).</summary>
public sealed class HrMenuTests(HrFactory factory) : IClassFixture<HrFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Me_WithTheReadPermission_ListsTheOrganizationScreens()
    {
        var me = (await factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Org.Read)
            .GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!;

        var hr = me.Menu.Single(m => m.Key == "hr");
        hr.Children!.Select(c => c.Path).ShouldBe(["/hr/organization", "/hr/positions", "/hr/job-titles"]);
    }

    [Fact]
    public async Task Me_WithTheEmployeeReadPermission_ListsTheEmployeesFirst()
    {
        var me = (await factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Employees.Read, HrPermissions.Org.Read)
            .GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!;

        me.Menu.Single(m => m.Key == "hr").Children!.Select(c => c.Path).ShouldBe(["/hr/employees", "/hr/organization", "/hr/positions", "/hr/job-titles"]);
    }

    [Fact]
    public async Task Me_WithTheSelfServicePermission_ListsOnlyTheOwnCard()
    {
        var me = (await factory.CreateClientFor("calisan", HrFactory.Tenant, HrPermissions.Self.ProfileRead, HrPermissions.Self.ProfileRequest)
            .GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!;

        me.Menu.Single(m => m.Key == "hr").Children!.Select(c => c.Path).ShouldBe(["/hr/me"]);
    }

    [Fact]
    public async Task Me_WithTheLeaveSettingsPermission_ListsTheLeaveSettings()
    {
        var me = (await factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Leave.Settings.Read, HrPermissions.Org.Read)
            .GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!;

        me.Menu.Single(m => m.Key == "hr").Children!.Select(c => c.Path)
            .ShouldBe(["/hr/organization", "/hr/positions", "/hr/job-titles", "/hr/leave-settings"]);
    }

    [Fact]
    public async Task Me_WithTheLeavePermissions_ListsTheOwnLeaveAndTheRequests()
    {
        var me = (await factory.CreateClientFor("yonetici", HrFactory.Tenant, HrPermissions.Self.ProfileRead, HrPermissions.Self.LeaveRead, HrPermissions.Leave.Requests.Read, HrPermissions.Leave.Calendar.Read, HrPermissions.Leave.Reports.Read)
            .GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!;

        me.Menu.Single(m => m.Key == "hr").Children!.Select(c => c.Path).ShouldBe(["/hr/me", "/hr/me/leave", "/hr/leave-requests", "/hr/leave-calendar", "/hr/leave-reports"]);
    }

    [Fact]
    public async Task Me_WithoutHrPermissions_HasNoHrMenu()
    {
        var me = (await factory.CreateClientFor("calisan", HrFactory.Tenant).GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!;

        me.Menu.ShouldNotContain(m => m.Key == "hr");
    }
}
