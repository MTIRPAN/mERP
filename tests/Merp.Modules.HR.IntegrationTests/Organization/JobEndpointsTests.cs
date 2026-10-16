using System.Net;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Domain.Organization;
using Merp.Testing.Web;

namespace Merp.Modules.HR.IntegrationTests.Organization;

public sealed class JobEndpointsTests(HrFactory factory) : IClassFixture<HrFactory>
{
    private const string Url = "/api/hr/jobs";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_ThenGet_ThenUpdate()
    {
        var admin = factory.Admin();
        var code = HrFactory.NewCode("J").ToLowerInvariant();

        var id = await HrFactory.CreatedIdAsync(await admin.PostAsJsonAsync(Url, new { code, name = "Satış Müdürü", family = "Satış", level = "M2" }, Ct));

        var job = (await admin.GetFromJsonAsync<JobDetails>($"{Url}/{id}", Ct))!;
        job.Code.ShouldBe(code.ToUpperInvariant());
        job.Name.ShouldBe("Satış Müdürü");
        job.Family.ShouldBe("Satış");
        job.Level.ShouldBe("M2");
        job.IsActive.ShouldBeTrue();

        (await admin.PutAsJsonAsync($"{Url}/{id}", new { version = job.Version, name = "Bölge Satış Müdürü", family = "Satış", level = "M3", isActive = false }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var updated = (await admin.GetFromJsonAsync<JobDetails>($"{Url}/{id}", Ct))!;
        (updated.Name, updated.Level, updated.IsActive).ShouldBe(("Bölge Satış Müdürü", "M3", false));
        (await factory.AuditedFieldsAsync(nameof(Job), id)).ShouldContain(nameof(Job.Level));
    }

    [Fact]
    public async Task Create_WithInvalidInput_OrATakenCode_Returns400()
    {
        var admin = factory.Admin();
        var invalid = await admin.PostAsJsonAsync(Url, new { code = "Ş", name = "" }, Ct);
        (await Problem.ReadAsync(invalid)).Errors!.Keys.ShouldBe(["code", "name"], ignoreOrder: true);

        var code = HrFactory.NewCode("J");
        await HrFactory.CreatedIdAsync(await admin.PostAsJsonAsync(Url, new { code, name = "Uzman" }, Ct));
        var taken = await admin.PostAsJsonAsync(Url, new { code, name = "Uzman" }, Ct);
        taken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(taken)).Errors!.Keys.ShouldBe(["code"]);
    }

    [Fact]
    public async Task Managing_NeedsTheTenantScope_AndThePermission()
    {
        var admin = factory.Admin();
        var company = await HrFactory.CreateCompanyAsync(admin);
        var id = await HrFactory.CreateJobAsync(admin);
        var version = (await admin.GetFromJsonAsync<JobDetails>($"{Url}/{id}", Ct))!.Version;

        // A company's HR manager reads the shared catalog but does not change it (D37)
        var companyScoped = factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Org.Read, HrPermissions.Org.Manage).WithScopes(AccessScope.Company(company));
        var create = await companyScoped.PostAsJsonAsync(Url, new { code = HrFactory.NewCode("J"), name = "Uzman" }, Ct);
        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Problem.ReadAsync(create)).Code.ShouldBe("hr.jobs.errors.tenant_scope_required");
        (await companyScoped.PutAsJsonAsync($"{Url}/{id}", new { version, name = "Uzman", isActive = true }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await companyScoped.GetAsync($"{Url}/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var reader = factory.CreateClientFor("reader", HrFactory.Tenant, HrPermissions.Org.Read);
        (await reader.PostAsJsonAsync(Url, new { code = HrFactory.NewCode("J"), name = "Uzman" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await factory.CreateClientFor("nobody", HrFactory.Tenant).GetAsync(Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await admin.GetAsync($"{Url}/{Guid.CreateVersion7()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_FiltersAndCountsThePositions()
    {
        var admin = factory.Admin();
        var company = await HrFactory.CreateCompanyAsync(admin);
        var unit = await HrFactory.CreateUnitAsync(admin, company);
        var family = HrFactory.NewCode("F");
        var job = await HrFactory.CreatedIdAsync(await admin.PostAsJsonAsync(Url, new { code = HrFactory.NewCode("J"), name = "Kıdemli Muhasebeci", family }, Ct));
        await HrFactory.CreatePositionAsync(admin, unit, job);
        await HrFactory.CreatePositionAsync(admin, unit, job);

        var page = (await admin.GetFromJsonAsync<PagedResult<JobListItem>>($"{Url}?family={family}", Ct))!;

        page.Items.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            j => j.Id.ShouldBe(job),
            j => j.PositionCount.ShouldBe(2));
        (await admin.GetFromJsonAsync<PagedResult<JobListItem>>($"{Url}?search=muhasebeci&isActive=false", Ct))!.Items.ShouldNotContain(j => j.Id == job);
        (await admin.GetAsync($"{Url}?sort=positionCount", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task InactiveJobs_CannotBeGivenToNewPositions()
    {
        var admin = factory.Admin();
        var company = await HrFactory.CreateCompanyAsync(admin);
        var unit = await HrFactory.CreateUnitAsync(admin, company);
        var job = await HrFactory.CreateJobAsync(admin);
        var version = (await admin.GetFromJsonAsync<JobDetails>($"{Url}/{job}", Ct))!.Version;
        await admin.PutAsJsonAsync($"{Url}/{job}", new { version, name = "Satış Müdürü", isActive = false }, Ct);

        var response = await admin.PostAsJsonAsync("/api/hr/positions", HrFactory.PositionBody(unit, job), Ct);

        (await Problem.ReadAsync(response)).Errors!["jobId"].ShouldBe(["Pasif bir unvan seçilemez."]);
    }
}
