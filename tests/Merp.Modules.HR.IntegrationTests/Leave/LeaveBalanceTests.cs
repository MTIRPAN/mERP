using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.Imports;
using Merp.Modules.HR.Application.Leave;
using Merp.Modules.HR.Contracts.Employees;
using Merp.Modules.HR.Domain.Leave;
using Merp.Modules.HR.IntegrationTests.Employees;
using Merp.Modules.HR.IntegrationTests.Organization;
using Merp.Modules.Platform.Application.Imports;
using Merp.Modules.Platform.Domain.Imports;
using Merp.Testing.Web;
using Merp.Web.Endpoints;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.HR.IntegrationTests.Leave;

/// <summary>Leave balances: accruals, corrections, opening balances, transfers and closings (PLAN L3, D43).</summary>
public sealed class LeaveBalanceTests(HrFactory factory) : IClassFixture<HrFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Accrual_CatchesUpTheAnniversariesFromTheAccrualStart_Once()
    {
        var admin = factory.Admin();
        await SetAccrualStartAsync("2024-01-01");
        var (employeeId, employmentId) = await HireAsync(admin, new DateOnly(2020, 3, 16));

        (await AccrueAsync(employmentId, new DateOnly(2026, 3, 20))).ShouldBe(3);
        (await AccrueAsync(employmentId, new DateOnly(2026, 3, 20))).ShouldBe(0);

        var annual = (await BalancesAsync(admin, employeeId, employmentId)).Single(b => b.LeaveTypeCode == "YILLIK");
        annual.Entries.Select(e => (e.PeriodKey, e.Quantity)).ShouldBe([("2026-03-16", 20m), ("2025-03-16", 20m), ("2024-03-16", 14m)]);
        annual.Balance.ShouldBe(54);
        annual.Earned.ShouldBe(54);
        annual.Available.ShouldBe(54);
        annual.NextAccrual.ShouldNotBeNull();
    }

    [Fact]
    public async Task Correction_NeedsAReason_AndTheBalancePermission()
    {
        var admin = factory.Admin();
        var (employeeId, employmentId) = await HireAsync(admin);
        var url = $"/api/hr/employees/{employeeId}/employments/{employmentId}/leave-balances/adjustments";

        (await admin.PostAsJsonAsync(url, new { leaveTypeCode = "YILLIK", date = "2026-01-10", quantity = 2.5m, note = "Eski sistemden fark" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.PostAsJsonAsync(url, new { leaveTypeCode = "YILLIK", date = "2026-01-10", quantity = 1 }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync(url, new { leaveTypeCode = "EVLILIK", date = "2026-01-10", quantity = 1, note = "x" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var annual = (await BalancesAsync(admin, employeeId, employmentId)).Single(b => b.LeaveTypeCode == "YILLIK");
        annual.Balance.ShouldBe(2.5m);
        annual.Entries.ShouldHaveSingleItem().ShouldSatisfyAllConditions(e => e.Source.ShouldBe(LeaveLedgerSource.Adjustment), e => e.Note.ShouldBe("Eski sistemden fark"));

        var reader = factory.CreateClientFor("okur", HrFactory.Tenant, HrPermissions.Leave.Balances.Read);
        (await reader.PostAsJsonAsync(url, new { leaveTypeCode = "YILLIK", date = "2026-01-10", quantity = 1, note = "x" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var otherCompany = await HrFactory.CreateCompanyAsync(admin);
        var scoped = factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Leave.Balances.Read).WithScopes(AccessScope.Company(otherCompany));
        (await scoped.GetAsync($"/api/hr/employees/{employeeId}/employments/{employmentId}/leave-balances", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OpeningBalances_AreImportedOnce()
    {
        var admin = factory.Admin();
        var companyCode = HrFactory.NewCode("C");
        var companyId = await HrFactory.CreateCompanyAsync(admin, companyCode);
        var own = await EmployeeSetup.CreateAsync(admin, companyId);
        var tckn = Testing.Data.TurkishData.Tckn();
        var employeeId = await HrFactory.CreatedIdAsync(await admin.PostAsJsonAsync("/api/hr/employees", own.HireBody(tckn: tckn), Ct));
        var employmentId = (await EmployeeSetup.LoadAsync(factory, employeeId)).Employments.Single().Id.Value;
        byte[] File(string days) => Workbook(new()
        {
            [LeaveOpeningBalanceImport.Columns.Company] = companyCode,
            [LeaveOpeningBalanceImport.Columns.Tckn] = tckn,
            [LeaveOpeningBalanceImport.Columns.Date] = "31.12.2025",
            [LeaveOpeningBalanceImport.Columns.Days] = days,
            [LeaveOpeningBalanceImport.Columns.Note] = "Eski sistem",
        });

        var job = await StartImportAsync(admin, File("12,5"));
        job.Status.ShouldBe(ImportStatus.Ready, string.Join(" | ", job.Errors.Select(e => $"{e.Row}/{e.Column}: {e.Message}")));
        (await admin.PostAsync($"/api/platform/imports/jobs/{job.Id}/commit", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var annual = (await BalancesAsync(admin, employeeId, employmentId)).Single(b => b.LeaveTypeCode == "YILLIK");
        annual.Balance.ShouldBe(12.5m);
        annual.Entries.ShouldHaveSingleItem().Source.ShouldBe(LeaveLedgerSource.OpeningBalance);
        var again = await StartImportAsync(admin, File("3"));
        again.Errors.ShouldHaveSingleItem().Column.ShouldBe(LeaveOpeningBalanceImport.Columns.Days);
    }

    [Fact]
    public async Task AGroupTransfer_MovesTheBalance_ToTheNewEmployment()
    {
        // E9 end to end: the source balance moves out, the new employment starts with it
        var admin = factory.Admin();
        var source = await EmployeeSetup.CreateAsync(admin);
        var target = await EmployeeSetup.CreateAsync(admin);
        var employeeId = await source.HireAsync(admin);
        var employmentId = (await EmployeeSetup.LoadAsync(factory, employeeId)).Employments.Single().Id.Value;
        await AdjustAsync(admin, employeeId, employmentId, 8);

        var moved = await HrFactory.CreatedIdAsync(await admin.PostAsJsonAsync(
            $"/api/hr/employees/{employeeId}/group-transfer",
            new
            {
                employmentId,
                targetCompanyId = target.CompanyId,
                transferDate = "2026-07-01",
                sgkExitCode = "16",
                employmentType = "Indefinite",
                positionId = target.PositionA,
                transferLeaveBalance = true,
            },
            Ct));

        var old = (await BalancesAsync(admin, employeeId, employmentId)).Single(b => b.LeaveTypeCode == "YILLIK");
        old.Balance.ShouldBe(0);
        old.ClosedOn.ShouldBe(new DateOnly(2026, 6, 30));
        var now = (await BalancesAsync(admin, employeeId, moved)).Single(b => b.LeaveTypeCode == "YILLIK");
        now.Balance.ShouldBe(8);
        now.Entries.ShouldHaveSingleItem().Source.ShouldBe(LeaveLedgerSource.Transfer);
    }

    [Fact]
    public async Task AnEndedEmployment_ClosesItsBalance_AndTellsPayrollWhatWasLeft()
    {
        var admin = factory.Admin();
        var (employeeId, employmentId) = await HireAsync(admin);
        await AdjustAsync(admin, employeeId, employmentId, 5);
        (await admin.PostAsJsonAsync($"/api/hr/employees/{employeeId}/terminate", new { employmentId, date = "2026-02-27", sgkExitCode = "03", rehireable = true }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await AccrueAsync(employmentId, new DateOnly(2026, 3, 2));

        var annual = (await BalancesAsync(admin, employeeId, employmentId)).Single(b => b.LeaveTypeCode == "YILLIK");
        annual.Balance.ShouldBe(0);
        annual.ClosedOn.ShouldBe(new DateOnly(2026, 2, 27));
        annual.Entries[0].Source.ShouldBe(LeaveLedgerSource.Termination);
        (await factory.OutboxAsync<EmployeeLeaveBalanceClosed>()).ShouldContain(e => e.EmploymentId == employmentId && e.RemainingDays == 5 && e.LeaveTypeCode == "YILLIK");
    }

    private async Task SetAccrualStartAsync(string date)
    {
        var settings = factory.CreateClientFor("admin", HrFactory.Tenant, "platform.settings.manage");
        (await settings.PutAsJsonAsync("/api/platform/settings", new { values = new Dictionary<string, object?> { ["hr.leave.accrual_start"] = date } }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private async Task<(Guid EmployeeId, Guid EmploymentId)> HireAsync(HttpClient admin, DateOnly? hireDate = null)
    {
        var setup = await EmployeeSetup.CreateAsync(admin);
        var employeeId = await setup.HireAsync(admin, hireDate ?? new DateOnly(2026, 1, 5));
        return (employeeId, (await EmployeeSetup.LoadAsync(factory, employeeId)).Employments.Single().Id.Value);
    }

    private Task<int> AccrueAsync(Guid employmentId, DateOnly today) =>
        factory.InTenantAsync(async services => (await services.GetRequiredService<ISender>().Send(new AccrueEmploymentLeaveCommand(employmentId, today), Ct)).Value);

    private static async Task AdjustAsync(HttpClient admin, Guid employeeId, Guid employmentId, decimal days) =>
        (await admin.PostAsJsonAsync(
            $"/api/hr/employees/{employeeId}/employments/{employmentId}/leave-balances/adjustments",
            new { leaveTypeCode = "YILLIK", date = "2026-01-10", quantity = days, note = "Devir" },
            Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

    private static async Task<List<LeaveBalanceItem>> BalancesAsync(HttpClient client, Guid employeeId, Guid employmentId) =>
        (await client.GetFromJsonAsync<List<LeaveBalanceItem>>($"/api/hr/employees/{employeeId}/employments/{employmentId}/leave-balances", Ct))!;

    private static byte[] Workbook(Dictionary<string, string?> row)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Veri");
        var c = 1;
        foreach (var (key, value) in row)
        {
            sheet.Cell(1, c).Value = key;
            sheet.Cell(2, c++).SetValue(value);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static async Task<ImportJobDetails> StartImportAsync(HttpClient client, byte[] content)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var response = await client.PostAsync($"/api/platform/imports/{LeaveOpeningBalanceImport.ImportKey}", new MultipartFormDataContent { { file, "file", "bakiye.xlsx" } }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var id = (await response.Content.ReadFromJsonAsync<EntityCreatedResponse>(Ct))!.Id;
        return (await client.GetFromJsonAsync<ImportJobDetails>($"/api/platform/imports/jobs/{id}", Ct))!;
    }
}
