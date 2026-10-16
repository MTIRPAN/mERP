using System.Net;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Domain.Organization;
using Merp.Testing.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.HR.IntegrationTests.Organization;

public sealed class PositionEndpointsTests(HrFactory factory) : IClassFixture<HrFactory>
{
    private const string Url = "/api/hr/positions";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_ThenGet_ReturnsThePositionWithItsManager()
    {
        var admin = factory.Admin();
        var company = await HrFactory.CreateCompanyAsync(admin);
        var unit = await HrFactory.CreateUnitAsync(admin, company);
        var manager = await HrFactory.CreateJobAsync(admin, "Satış Direktörü");
        var rep = await HrFactory.CreateJobAsync(admin, "Satış Temsilcisi");
        var director = await HrFactory.CreatePositionAsync(admin, unit, manager);

        var response = await admin.PostAsJsonAsync(Url, new { orgUnitId = unit, jobId = rep, code = "sat-01", reportsToPositionId = director, norm = 3, isManager = false }, Ct);

        var id = await HrFactory.CreatedIdAsync(response);
        var position = (await admin.GetFromJsonAsync<PositionDetails>($"{Url}/{id}", Ct))!;
        position.CompanyId.ShouldBe(company);
        position.CompanyName.ShouldBe("Acme");
        position.Code.ShouldBe("SAT-01");
        position.OrgUnitName.ShouldBe("Satış Direktörlüğü");
        position.JobName.ShouldBe("Satış Temsilcisi");
        position.ReportsToPositionId.ShouldBe(director);
        position.ManagerPositionId.ShouldBe(director);
        position.Status.ShouldBe(PositionStatus.Active);
        (position.Norm, position.Filled, position.Vacant).ShouldBe((3, 0, 3));
        position.Holders.ShouldBeEmpty();

        (await factory.ReportingLinesAsync(company)).ShouldBe([(director, id, 1)]);
        (await factory.AuditedFieldsAsync(nameof(Position), id)).ShouldContain(nameof(Position.Norm));
    }

    [Fact]
    public async Task Create_WithInvalidInput_Returns400WithFieldErrors()
    {
        var admin = factory.Admin();
        var company = await HrFactory.CreateCompanyAsync(admin);
        var unit = await HrFactory.CreateUnitAsync(admin, company);
        var job = await HrFactory.CreateJobAsync(admin);

        var invalid = await admin.PostAsJsonAsync(Url, new { orgUnitId = unit, jobId = job, code = "", norm = 0 }, Ct);
        (await Problem.ReadAsync(invalid)).Errors!.Keys.ShouldBe(["code", "norm"], ignoreOrder: true);

        var unknown = await admin.PostAsJsonAsync(Url, HrFactory.PositionBody(Guid.CreateVersion7(), job), Ct);
        (await Problem.ReadAsync(unknown)).Errors!["orgUnitId"].ShouldBe(["Birim bulunamadı."]);

        var code = HrFactory.NewCode("P");
        await HrFactory.CreatedIdAsync(await admin.PostAsJsonAsync(Url, HrFactory.PositionBody(unit, job, code), Ct));
        var references = await admin.PostAsJsonAsync(Url, HrFactory.PositionBody(unit, Guid.CreateVersion7(), code, reportsTo: Guid.CreateVersion7()), Ct);
        (await Problem.ReadAsync(references)).Errors!.Keys.ShouldBe(["code", "jobId", "reportsToPositionId"], ignoreOrder: true);
    }

    [Fact]
    public async Task Create_InAUnitOutsideTheScope_OrWithoutPermission_Returns403()
    {
        var admin = factory.Admin();
        var own = await HrFactory.CreateCompanyAsync(admin);
        var other = await HrFactory.CreateCompanyAsync(admin);
        var ownUnit = await HrFactory.CreateUnitAsync(admin, own);
        var foreignUnit = await HrFactory.CreateUnitAsync(admin, other);
        var job = await HrFactory.CreateJobAsync(admin);

        var scoped = factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Org.Read, HrPermissions.Org.Manage).WithScopes(AccessScope.Company(own));
        await HrFactory.CreatedIdAsync(await scoped.PostAsJsonAsync(Url, HrFactory.PositionBody(ownUnit, job), Ct));
        (await scoped.PostAsJsonAsync(Url, HrFactory.PositionBody(foreignUnit, job), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var reader = factory.CreateClientFor("reader", HrFactory.Tenant, HrPermissions.Org.Read);
        (await reader.PostAsJsonAsync(Url, HrFactory.PositionBody(ownUnit, job), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PositionsOutsideTheScope_Return404_AndAreNotListed()
    {
        var admin = factory.Admin();
        var own = await HrFactory.CreateCompanyAsync(admin);
        var other = await HrFactory.CreateCompanyAsync(admin);
        var job = await HrFactory.CreateJobAsync(admin);
        var ownPosition = await HrFactory.CreatePositionAsync(admin, await HrFactory.CreateUnitAsync(admin, own), job);
        var foreignUnit = await HrFactory.CreateUnitAsync(admin, other);
        var foreign = await HrFactory.CreatePositionAsync(admin, foreignUnit, job);
        var version = (await admin.GetFromJsonAsync<PositionDetails>($"{Url}/{foreign}", Ct))!.Version;

        var scoped = factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Org.Read, HrPermissions.Org.Manage).WithScopes(AccessScope.Company(own));

        (await scoped.GetAsync($"{Url}/{foreign}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await scoped.PutAsJsonAsync($"{Url}/{foreign}", new { version, orgUnitId = foreignUnit, jobId = job, norm = 1, status = "Active", isManager = false }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var visible = (await scoped.GetFromJsonAsync<PagedResult<PositionListItem>>($"{Url}?pageSize=200", Ct))!.Items.Select(p => p.Id).ToList();
        visible.ShouldContain(ownPosition);
        visible.ShouldNotContain(foreign);
    }

    [Fact]
    public async Task List_WithVacantOnly_IsTheVacancyReport()
    {
        var admin = factory.Admin();
        var company = await HrFactory.CreateCompanyAsync(admin);
        var sales = await HrFactory.CreateUnitAsync(admin, company);
        var team = await HrFactory.CreateUnitAsync(admin, company, sales, "İstanbul", OrgUnitType.Team);
        var other = await HrFactory.CreateUnitAsync(admin, company, name: "Pazarlama");
        var job = await HrFactory.CreateJobAsync(admin, "Satış Temsilcisi");
        var director = await HrFactory.CreatePositionAsync(admin, sales, job);
        var reps = await HrFactory.CreatePositionAsync(admin, team, job, norm: 5);
        var marketer = await HrFactory.CreatePositionAsync(admin, other, job);
        var closed = await HrFactory.CreatePositionAsync(admin, team, job);
        var details = (await admin.GetFromJsonAsync<PositionDetails>($"{Url}/{closed}", Ct))!;
        (await admin.PutAsJsonAsync($"{Url}/{closed}", new { version = details.Version, orgUnitId = team, jobId = job, norm = 1, status = "Closed", isManager = false }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var vacancies = (await admin.GetFromJsonAsync<PagedResult<PositionListItem>>($"{Url}?companyId={company}&vacantOnly=true&sort=-norm", Ct))!;
        vacancies.TotalCount.ShouldBe(3);
        vacancies.Items.Select(p => p.Id).First().ShouldBe(reps);
        vacancies.Items.Select(p => p.Id).ShouldBe([reps, director, marketer], ignoreOrder: true);
        vacancies.Items.Sum(p => p.Vacant).ShouldBe(7);

        // The subtree of the sales directorate
        var subtree = (await admin.GetFromJsonAsync<PagedResult<PositionListItem>>($"{Url}?orgUnitId={sales}&includeSubUnits=true&vacantOnly=true", Ct))!;
        subtree.Items.Select(p => p.Id).ShouldBe([director, reps], ignoreOrder: true);
        (await admin.GetFromJsonAsync<PagedResult<PositionListItem>>($"{Url}?orgUnitId={sales}", Ct))!.Items.Select(p => p.Id).ShouldBe([director]);
        (await admin.GetFromJsonAsync<PagedResult<PositionListItem>>($"{Url}?companyId={company}&status=Closed", Ct))!.Items.Select(p => p.Id).ShouldBe([closed]);

        // A one row page of the report
        var page = (await admin.GetFromJsonAsync<PagedResult<PositionListItem>>($"{Url}?companyId={company}&vacantOnly=true&sort=-norm&page=2&pageSize=1", Ct))!;
        (page.Page, page.PageSize, page.TotalCount).ShouldBe((2, 1, 3));
        page.Items.ShouldHaveSingleItem().Id.ShouldNotBe(reps);
    }

    [Fact]
    public async Task Update_ThatWouldMakeAPositionManageItself_Returns400()
    {
        var admin = factory.Admin();
        var company = await HrFactory.CreateCompanyAsync(admin);
        var unit = await HrFactory.CreateUnitAsync(admin, company);
        var job = await HrFactory.CreateJobAsync(admin);
        var top = await HrFactory.CreatePositionAsync(admin, unit, job);
        var middle = await HrFactory.CreatePositionAsync(admin, unit, job, reportsTo: top);
        var bottom = await HrFactory.CreatePositionAsync(admin, unit, job, reportsTo: middle);
        var version = (await admin.GetFromJsonAsync<PositionDetails>($"{Url}/{top}", Ct))!.Version;

        var response = await admin.PutAsJsonAsync($"{Url}/{top}", new { version, orgUnitId = unit, jobId = job, reportsToPositionId = bottom, norm = 1, status = "Active", isManager = false }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(response)).Errors!["reportsToPositionId"].ShouldBe(["Bu değişiklikle bir pozisyon, yönetici zincirinde kendisine bağlanmış olur."]);
        (await factory.ReportingLinesAsync(company)).ShouldBe([(top, middle, 1), (middle, bottom, 1), (top, bottom, 2)], ignoreOrder: true);
    }

    [Fact]
    public async Task Update_ClosingTheManagerOfAUnit_Returns400()
    {
        var admin = factory.Admin();
        var company = await HrFactory.CreateCompanyAsync(admin);
        var unit = await HrFactory.CreateUnitAsync(admin, company);
        var job = await HrFactory.CreateJobAsync(admin);
        var director = await HrFactory.CreatePositionAsync(admin, unit, job);
        var unitVersion = (await admin.GetFromJsonAsync<OrgUnitDetails>($"/api/hr/org-units/{unit}", Ct))!.Version;
        await admin.PutAsJsonAsync($"/api/hr/org-units/{unit}", new { version = unitVersion, name = "Satış", type = "Directorate", managerPositionId = director }, Ct);
        var version = (await admin.GetFromJsonAsync<PositionDetails>($"{Url}/{director}", Ct))!.Version;

        var response = await admin.PutAsJsonAsync($"{Url}/{director}", new { version, orgUnitId = unit, jobId = job, norm = 1, status = "Closed", isManager = true }, Ct);

        (await Problem.ReadAsync(response)).Errors!["status"].ShouldBe(["Bu pozisyon bir birimin yöneticisi. Önce birime başka bir yönetici pozisyonu seçin."]);
    }

    [Fact]
    public async Task NightlyRebuild_RestoresTheReportingLines()
    {
        var admin = factory.Admin();
        var company = await HrFactory.CreateCompanyAsync(admin);
        var unit = await HrFactory.CreateUnitAsync(admin, company);
        var job = await HrFactory.CreateJobAsync(admin);
        var top = await HrFactory.CreatePositionAsync(admin, unit, job);
        var rep = await HrFactory.CreatePositionAsync(admin, unit, job, reportsTo: top);
        await factory.QueryAsync(db => db.Database.ExecuteSqlAsync($"DELETE FROM hr.reporting_lines WHERE company_id = {company}", Ct));

        var companies = await factory.InTenantAsync(async services =>
            (await services.GetRequiredService<ISender>().Send(new RebuildReportingLinesCommand(), Ct)).Value);

        companies.ShouldBeGreaterThan(0);
        (await factory.ReportingLinesAsync(company)).ShouldBe([(top, rep, 1)]);
    }
}
