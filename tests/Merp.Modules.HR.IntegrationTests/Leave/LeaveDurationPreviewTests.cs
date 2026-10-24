using System.Net;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Security;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.Leave;
using Merp.Modules.HR.IntegrationTests.Employees;
using Merp.Modules.HR.IntegrationTests.Organization;
using Merp.Modules.Platform.Application.Calendars;
using Merp.Testing.Web;

namespace Merp.Modules.HR.IntegrationTests.Leave;

/// <summary>
/// Leave durations on the seeded work schedules and holidays (PLAN L2, D42): the §8.1 scenarios end to end, the
/// schedule of the employment, of its branch or of the company.
/// </summary>
public sealed class LeaveDurationPreviewTests(HrFactory factory) : IClassFixture<HrFactory>
{
    private const string Url = "/api/hr/leave/duration-preview";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task S1_OnTheDefaultSchedule_TheEveCountsHalf_AndTheHolidayNothing()
    {
        var (admin, employmentId) = await HireAsync();

        var preview = await PreviewAsync(admin, new { employmentId, leaveTypeId = await TypeIdAsync(admin, "YILLIK"), startDate = "2026-10-26", endDate = "2026-10-30" });

        preview.Quantity.ShouldBe(3.5m);
        preview.Days.Select(d => d.Value).ShouldBe([1m, 1m, 0.5m, 0m, 1m]);
        preview.Days.Single(d => d.Date == new DateOnly(2026, 10, 29)).HolidayName.ShouldBe("Cumhuriyet Bayramı");
    }

    [Fact]
    public async Task S3_OnTheEmploymentsSixDaySchedule_TheSaturdayCounts()
    {
        var calendars = factory.CreateClientFor("ik", HrFactory.Tenant, [.. HrFactory.AllPermissions, "platform.calendars.read"]);
        var sixDays = (await calendars.GetFromJsonAsync<List<WorkScheduleItem>>("/api/platform/work-schedules", Ct))!.Single(s => s.Code == "ALTIGUN").Id;
        var (admin, employmentId) = await HireAsync(new { workScheduleId = sixDays });

        var preview = await PreviewAsync(admin, new { employmentId, leaveTypeId = await TypeIdAsync(admin, "YILLIK"), startDate = "2027-03-08", endDate = "2027-03-13" });

        preview.Quantity.ShouldBe(2.5m);
    }

    [Fact]
    public async Task S6_TheAfternoonOfAHalfDayHoliday_IsRefused()
    {
        var (admin, employmentId) = await HireAsync();

        var response = await admin.PostAsJsonAsync(
            Url,
            new { employmentId, leaveTypeId = await TypeIdAsync(admin, "YILLIK"), startDate = "2026-10-28", endDate = "2026-10-28", startPart = "Afternoon" },
            Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(response)).Errors!["endDate"].ShouldHaveSingleItem().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task S8_AFixedCalendarDayLeave_GivesItsLastDay()
    {
        var (admin, employmentId) = await HireAsync();

        var preview = await PreviewAsync(admin, new { employmentId, leaveTypeId = await TypeIdAsync(admin, "EVLILIK"), startDate = "2026-10-30" });

        preview.EndDate.ShouldBe(new DateOnly(2026, 11, 1));
        preview.Quantity.ShouldBe(3);
    }

    [Fact]
    public async Task WithoutAnEndDate_ATypeWithoutAFixedLength_IsRefused_AndAnInvertedRangeToo()
    {
        var (admin, employmentId) = await HireAsync();
        var annual = await TypeIdAsync(admin, "YILLIK");

        (await admin.PostAsJsonAsync(Url, new { employmentId, leaveTypeId = annual, startDate = "2026-10-26" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync(Url, new { employmentId, leaveTypeId = annual, startDate = "2026-10-26", endDate = "2026-10-20" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OutsideTheScope_IsNotFound_AndWithoutThePermission403()
    {
        var (admin, employmentId) = await HireAsync();
        var otherCompany = await HrFactory.CreateCompanyAsync(admin);
        var body = new { employmentId, leaveTypeId = await TypeIdAsync(admin, "YILLIK"), startDate = "2026-10-26", endDate = "2026-10-30" };

        var scoped = factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Employees.Read).WithScopes(AccessScope.Company(otherCompany));
        (await scoped.PostAsJsonAsync(Url, body, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var stranger = factory.CreateClientFor("yabanci", HrFactory.Tenant, HrPermissions.Leave.Settings.Read);
        (await stranger.PostAsJsonAsync(Url, body, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private async Task<(HttpClient Admin, Guid EmploymentId)> HireAsync(object? extra = null)
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var employeeId = await setup.HireAsync(admin, new DateOnly(2026, 1, 5), extra);
        return (admin, (await EmployeeSetup.LoadAsync(factory, employeeId)).Employments.Single().Id.Value);
    }

    private static async Task<Guid> TypeIdAsync(HttpClient client, string code) =>
        (await client.GetFromJsonAsync<List<LeaveTypeItem>>("/api/hr/leave/types", Ct))!.Single(t => t.Code == code).Id;

    private static async Task<LeaveDurationPreview> PreviewAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync(Url, body, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<LeaveDurationPreview>(Ct))!;
    }
}
