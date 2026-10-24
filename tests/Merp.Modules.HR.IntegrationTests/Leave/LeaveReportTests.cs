using System.Net;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.Leave;
using Merp.Modules.HR.IntegrationTests.Employees;
using Merp.Modules.HR.IntegrationTests.Organization;
using Merp.Testing.Web;

namespace Merp.Modules.HR.IntegrationTests.Leave;

/// <summary>
/// Leave reports and documents (PLAN L6, D46). The fake clock stands on 5 January 2026; documents are read as the HTML
/// sent to the PDF converter.
/// </summary>
public sealed class LeaveReportTests(HrClockFactory factory) : IClassFixture<HrClockFactory>
{
    private const string Reports = "/api/hr/leave/reports";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheBalanceReport_SumsTheMovementsUpToTheDay()
    {
        var data = await DataAsync();

        var before = await PageAsync<LeaveBalanceReportRow>(data.Admin, $"{Reports}/balances?asOf=2025-12-20&companyId={data.Setup.CompanyId}");
        before.Items.Single(r => r.EmploymentId == data.First.Employment).ShouldSatisfyAllConditions(
            r => r.Earned.ShouldBe(0),
            r => r.Used.ShouldBe(3),
            r => r.Balance.ShouldBe(-3));

        var now = await PageAsync<LeaveBalanceReportRow>(data.Admin, $"{Reports}/balances?asOf=2026-01-05&companyId={data.Setup.CompanyId}&search=Ali");
        now.Items.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            r => r.EmployeeName.ShouldBe("Ali Demir"),
            r => r.LeaveTypeCode.ShouldBe("YILLIK"),
            r => r.Earned.ShouldBe(14),
            r => r.Balance.ShouldBe(11));
    }

    [Fact]
    public async Task TheUsageReport_CountsTheApprovedLeavesOfThePeriod_ByPersonAndType()
    {
        var data = await DataAsync();

        var usage = await PageAsync<LeaveUsageReportRow>(data.Admin, $"{Reports}/usage?from=2025-12-01&to=2025-12-31&companyId={data.Setup.CompanyId}");

        usage.Items.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            r => r.EmploymentId.ShouldBe(data.First.Employment),
            r => r.LeaveTypeCode.ShouldBe("YILLIK"),
            r => r.Requests.ShouldBe(1),
            r => r.Days.ShouldBe(3),
            r => r.RegistryNumber.ShouldNotBeNullOrWhiteSpace());
    }

    [Fact]
    public async Task UpcomingLeaves_AndPeopleWithoutLeave_AreListed()
    {
        var data = await DataAsync();

        var upcoming = await PageAsync<LeaveRequestListItem>(data.Admin, $"{Reports}/upcoming?companyId={data.Setup.CompanyId}");
        upcoming.Items.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            r => r.EmployeeId.ShouldBe(data.Second.Employee),
            r => r.StartDate.ShouldBe(new DateOnly(2026, 1, 12)));

        // A month without leave: the first took leave in December, the second never did
        var idle = await PageAsync<LongTimeNoLeaveRow>(data.Admin, $"{Reports}/long-time-no-leave?months=1&companyId={data.Setup.CompanyId}");
        idle.Items.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            r => r.EmploymentId.ShouldBe(data.Second.Employment),
            r => r.LastLeaveEnd.ShouldBeNull(),
            r => r.OrgUnitName.ShouldNotBeNull());
    }

    [Fact]
    public async Task Reports_AreExported_AndFollowTheScopeAndThePermission()
    {
        var data = await DataAsync();

        var file = await data.Admin.GetAsync($"{Reports}/balances/export?asOf=2026-01-05&companyId={data.Setup.CompanyId}", Ct);
        file.StatusCode.ShouldBe(HttpStatusCode.OK, await file.Content.ReadAsStringAsync(Ct));
        file.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        var elsewhere = await HrFactory.CreateCompanyAsync(data.Admin);
        var scoped = factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Leave.Reports.Read).WithScopes(AccessScope.Company(elsewhere));
        (await PageAsync<LeaveBalanceReportRow>(scoped, $"{Reports}/balances?asOf=2026-01-05&companyId={data.Setup.CompanyId}")).Items.ShouldBeEmpty();
        (await factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Leave.Balances.Read).GetAsync($"{Reports}/balances?asOf=2026-01-05", Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await data.Admin.GetAsync($"{Reports}/usage?from=2026-01-31&to=2026-01-01", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TheLeaveRecord_HoldsTheSeniority_TheDaysEarnedAndTaken_AndTheSignatures()
    {
        // PLAN L6 acceptance
        var data = await DataAsync();

        var response = await data.Admin.GetAsync($"/api/hr/employees/{data.First.Employee}/employments/{data.First.Employment}/leave-record", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        var html = await response.Content.ReadAsStringAsync(Ct);
        html.ShouldContain("YILLIK ÜCRETLİ İZİN KAYIT BELGESİ");
        html.ShouldContain("Ali Demir");
        html.ShouldContain("Kıdem Başlangıcı");
        html.ShouldContain("1.11.2025");
        html.ShouldContain("15.12.2025");
        html.ShouldContain("17.12.2025");
        html.ShouldContain("İşveren / Vekili");
        var scoped = factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Leave.Balances.Read).WithScopes(AccessScope.Company(await HrFactory.CreateCompanyAsync(data.Admin)));
        (await scoped.GetAsync($"/api/hr/employees/{data.First.Employee}/employments/{data.First.Employment}/leave-record", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheRequestForm_IsPrintedForThoseWhoSeeTheRequest()
    {
        var data = await DataAsync();

        var response = await data.Admin.GetAsync($"/api/hr/leave-requests/{data.Upcoming}/form", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var html = await response.Content.ReadAsStringAsync(Ct);
        html.ShouldContain("İZİN TALEP FORMU");
        html.ShouldContain("IZN-2026-");
        html.ShouldContain("Talep Eden");
        (await factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Leave.Balances.Read).GetAsync($"/api/hr/leave-requests/{data.Upcoming}/form", Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var stranger = factory.CreateClientFor("u" + Guid.NewGuid().ToString("N")[..10], HrFactory.Tenant, HrPermissions.Self.LeaveRead);
        (await stranger.GetAsync($"/api/hr/me/leave-requests/{data.Upcoming}/form", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private sealed record Person(Guid Employee, Guid Employment);

    private sealed record Data(HttpClient Admin, EmployeeSetup Setup, Person First, Person Second, Guid Upcoming);

    /// <summary>
    /// Two people hired on 1 November 2025: the first has fourteen days from 31 December and took three in December,
    /// the second has an administrative leave on 12–13 January and took none before.
    /// </summary>
    private async Task<Data> DataAsync()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        async Task<Person> HireAsync(string firstName, string lastName)
        {
            var id = await HrFactory.CreatedIdAsync(await admin.PostAsJsonAsync(
                "/api/hr/employees",
                EmployeeSetup.Merge((Dictionary<string, object?>)setup.HireBody(hireDate: new DateOnly(2025, 11, 1)), new { firstName, lastName }),
                Ct));
            return new Person(id, (await EmployeeSetup.LoadAsync(factory, id)).Employments.Single().Id.Value);
        }

        var first = await HireAsync("Ali", "Demir");
        var second = await HireAsync("Zeynep", "Kaya");
        var types = (await admin.GetFromJsonAsync<List<LeaveTypeItem>>("/api/hr/leave/types", Ct))!;
        (await admin.PostAsJsonAsync(
            $"/api/hr/employees/{first.Employee}/employments/{first.Employment}/leave-balances/adjustments",
            new { leaveTypeCode = "YILLIK", date = "2025-12-31", quantity = 14, note = "Devir" },
            Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await HrFactory.CreatedIdAsync(await admin.PostAsJsonAsync(
            "/api/hr/leave-requests/records",
            new { employmentId = first.Employment, leaveTypeId = types.Single(t => t.Code == "YILLIK").Id, startDate = "2025-12-15", endDate = "2025-12-17" },
            Ct));
        var upcoming = await HrFactory.CreatedIdAsync(await admin.PostAsJsonAsync(
            "/api/hr/leave-requests/records",
            new { employmentId = second.Employment, leaveTypeId = types.Single(t => t.Code == "IDARI").Id, startDate = "2026-01-12", endDate = "2026-01-13" },
            Ct));
        return new Data(admin, setup, first, second, upcoming);
    }

    private static async Task<PagedResult<T>> PageAsync<T>(HttpClient client, string url)
    {
        var response = await client.GetAsync(url, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<PagedResult<T>>(Ct))!;
    }
}
