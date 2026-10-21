using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Infrastructure.Persistence.Auditing;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.Employees;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.IntegrationTests.Organization;
using Merp.Testing.Data;
using Merp.Testing.Web;
using Merp.Web.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.IntegrationTests.Employees;

/// <summary>
/// The employee list and card (PLAN H2b, D38) on the fake clock, which stands on 5 January 2026 (Istanbul): hire
/// dates before it are active, later ones upcoming.
/// </summary>
public sealed class EmployeeQueryTests(HrClockFactory factory) : IClassFixture<HrClockFactory>
{
    private const string Url = "/api/hr/employees";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task List_ShowsWhoWorksToday_AndTheStatusFilterShowsTheOthers()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var active = await setup.HireAsync(admin, new DateOnly(2025, 12, 1), new { firstName = "Aktif" });
        var upcoming = await setup.HireAsync(admin, new DateOnly(2026, 2, 1), new { firstName = "Gelecek" });
        var leaver = await setup.HireAsync(admin, new DateOnly(2025, 6, 1), new { firstName = "Ayrılan" });
        var employment = (await EmployeeSetup.LoadAsync(factory, leaver)).Employments[0].Id.Value;
        (await admin.PostAsJsonAsync($"{Url}/{leaver}/terminate", new { employmentId = employment, date = "2026-01-02", sgkExitCode = "03", rehireable = true }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // PLAN H2 acceptance: a leaver is not in the default list, the status filter shows them
        var today = await ListAsync(admin, $"companyId={setup.CompanyId}");
        var item = today.Items.ShouldHaveSingleItem();
        item.ShouldSatisfyAllConditions(
            i => i.Id.ShouldBe(active),
            i => i.FullName.ShouldBe("Aktif Yılmaz"),
            i => i.Status.ShouldBe(EmploymentStatus.Active),
            i => i.RegistryNumber.ShouldNotBeNullOrEmpty(),
            i => i.CompanyName.ShouldNotBeNullOrEmpty(),
            i => i.PositionId.ShouldBe(setup.PositionA),
            i => i.PositionCode.ShouldNotBeNullOrEmpty(),
            i => i.JobName.ShouldBe("Satış Uzmanı"),
            i => i.OrgUnitId.ShouldBe(setup.UnitId),
            i => i.OrgUnitName.ShouldBe("Satış Direktörlüğü"),
            // The TCKN is masked in lists, whatever the caller's permissions
            i => i.Tckn.ShouldMatch(@"^\d{3}\*{6}\d{2}$"));

        (await ListAsync(admin, $"companyId={setup.CompanyId}&status=Terminated")).Items.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            i => i.Id.ShouldBe(leaver),
            i => i.Status.ShouldBe(EmploymentStatus.Terminated),
            i => i.TerminationDate.ShouldBe(new DateOnly(2026, 1, 2)),
            // The last assignment still places the leaver
            i => i.PositionId.ShouldBe(setup.PositionA));
        (await ListAsync(admin, $"companyId={setup.CompanyId}&status=Upcoming&status=Active&sort=-hireDate")).Items
            .Select(i => i.Id).ShouldBe([upcoming, active]);
        (await ListAsync(admin, $"companyId={setup.CompanyId}&status=Upcoming&status=Active&status=Terminated&search=yıl&pageSize=2")).TotalCount.ShouldBe(3);
        (await ListAsync(admin, $"companyId={setup.CompanyId}&search=gelecek&status=Upcoming")).Items.ShouldHaveSingleItem().Id.ShouldBe(upcoming);
    }

    [Fact]
    public async Task List_FiltersByUnitBranchPositionAndJob()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var branch = await HrFactory.CreateBranchAsync(admin, setup.CompanyId);
        var child = await HrFactory.CreateUnitAsync(admin, setup.CompanyId, setup.UnitId, "Bölge Satış", Domain.Organization.OrgUnitType.Department);
        var childPosition = await HrFactory.CreatePositionAsync(admin, child, setup.JobId);
        var inBranch = await setup.HireAsync(admin, new DateOnly(2025, 12, 1), new { branchId = branch });
        var inChild = await setup.HireAsync(admin, new DateOnly(2025, 12, 1), new { positionId = childPosition });
        var inB = await setup.HireAsync(admin, new DateOnly(2025, 12, 1), new { positionId = setup.PositionB });

        (await ListAsync(admin, $"branchId={branch}")).Items.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            i => i.Id.ShouldBe(inBranch),
            i => i.BranchId.ShouldBe(branch),
            i => i.BranchName.ShouldNotBeNullOrEmpty());
        (await ListAsync(admin, $"orgUnitId={child}")).Items.ShouldHaveSingleItem().Id.ShouldBe(inChild);
        (await ListAsync(admin, $"orgUnitId={setup.UnitId}")).Items.Select(i => i.Id).ShouldBe([inBranch, inB], ignoreOrder: true);
        (await ListAsync(admin, $"orgUnitId={setup.UnitId}&includeSubUnits=true")).Items.Select(i => i.Id).ShouldBe([inBranch, inChild, inB], ignoreOrder: true);
        (await ListAsync(admin, $"positionId={setup.PositionB}")).Items.ShouldHaveSingleItem().Id.ShouldBe(inB);
        (await ListAsync(admin, $"companyId={setup.CompanyId}&jobId={setup.JobId}")).TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task List_FollowsTheDataScope()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var other = await EmployeeSetup.CreateAsync(admin);
        var branch = await HrFactory.CreateBranchAsync(admin, setup.CompanyId);
        var child = await HrFactory.CreateUnitAsync(admin, setup.CompanyId, setup.UnitId, "Bölge Satış", Domain.Organization.OrgUnitType.Department);
        var childPosition = await HrFactory.CreatePositionAsync(admin, child, setup.JobId);
        var inBranch = await setup.HireAsync(admin, new DateOnly(2025, 12, 1), new { branchId = branch, positionId = setup.PositionB });
        var inChild = await setup.HireAsync(admin, new DateOnly(2025, 12, 1), new { positionId = childPosition });
        var inParent = await setup.HireAsync(admin, new DateOnly(2025, 12, 1));
        var elsewhere = await other.HireAsync(admin, new DateOnly(2025, 12, 1));

        async Task<List<Guid>> SeenAsync(params AccessScope[] scopes)
        {
            var client = factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Employees.Read).WithScopes(scopes);
            return [.. (await ListAsync(client, "pageSize=200")).Items.Select(i => i.Id)];
        }

        var company = await SeenAsync(AccessScope.Company(setup.CompanyId));
        company.ShouldBe([inBranch, inChild, inParent], ignoreOrder: true);
        (await SeenAsync(AccessScope.Branch(branch))).ShouldBe([inBranch]);
        (await SeenAsync(new AccessScope(AccessScopeTypes.OrgUnit, setup.UnitId))).ShouldBe([inBranch, inParent], ignoreOrder: true);
        (await SeenAsync(new AccessScope(AccessScopeTypes.OrgUnit, setup.UnitId, IncludeSubUnits: true))).ShouldBe([inBranch, inChild, inParent], ignoreOrder: true);
        (await SeenAsync(new AccessScope(AccessScopeTypes.OrgUnit, child))).ShouldBe([inChild]);

        // Outside the scope the card does not exist
        var scoped = factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Employees.Read).WithScopes(AccessScope.Company(setup.CompanyId));
        (await scoped.GetAsync($"{Url}/{elsewhere}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await scoped.GetAsync($"{Url}/{inParent}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task List_WithAnInvalidSortOrCustomFieldFilter_Returns400_AndWithoutPermission_Returns403()
    {
        var admin = factory.Admin();

        var sort = await admin.GetAsync($"{Url}?sort=tckn", Ct);
        sort.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(sort)).Errors!.Keys.ShouldBe(["sort"]);
        (await admin.GetAsync($"{Url}?customFields=[1]", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var noRead = factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Org.Read);
        (await noRead.GetAsync(Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await noRead.GetAsync($"{Url}/{Guid.NewGuid()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var noExport = factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Employees.Read);
        (await noExport.GetAsync($"{Url}/export", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Export_ReturnsTheListAsExcel()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        await setup.HireAsync(admin, new DateOnly(2025, 12, 1));

        var response = await admin.GetAsync($"{Url}/export?companyId={setup.CompanyId}&columns=registryNumber,firstName,status", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        (await response.Content.ReadAsByteArrayAsync(Ct)).Take(2).ShouldBe("PK"u8.ToArray());
    }

    [Fact]
    public async Task Card_ShowsThePersonAndTheJobHistory()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var id = await setup.HireAsync(admin, new DateOnly(2025, 12, 1), new { birthDate = "1990-05-01", gender = "Female" });
        var employment = (await EmployeeSetup.LoadAsync(factory, id)).Employments[0].Id.Value;
        (await admin.PostAsJsonAsync(
            $"{Url}/{id}/assignments",
            new { employmentId = employment, validFrom = "2026-02-01", reason = "Promotion", positionId = setup.PositionB, managerPositionId = setup.PositionA },
            Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var card = await CardAsync(admin, id);

        var version = await EmployeeSetup.VersionAsync(factory, id);
        card.ShouldSatisfyAllConditions(
            c => c.FullName.ShouldBe("Ayşe Yılmaz"),
            c => c.BirthDate.ShouldBe(new DateOnly(1990, 5, 1)),
            c => c.Gender.ShouldBe(Gender.Female),
            c => c.Version.ShouldBe(version),
            c => c.PhotoFileId.ShouldBeNull());
        var item = card.Employments.ShouldHaveSingleItem();
        item.ShouldSatisfyAllConditions(
            e => e.CompanyId.ShouldBe(setup.CompanyId),
            e => e.Status.ShouldBe(EmploymentStatus.Active),
            e => e.Type.ShouldBe(EmploymentType.Indefinite),
            e => e.SeniorityDate.ShouldBe(new DateOnly(2025, 12, 1)));
        item.Assignments.Count.ShouldBe(2);
        item.Assignments[0].ShouldSatisfyAllConditions(
            a => a.PositionId.ShouldBe(setup.PositionB),
            a => a.Reason.ShouldBe(AssignmentReason.Promotion),
            a => a.ValidFrom.ShouldBe(new DateOnly(2026, 2, 1)),
            a => a.ManagerPositionId.ShouldBe(setup.PositionA),
            a => a.ManagerPositionCode.ShouldNotBeNullOrEmpty(),
            a => a.IsCurrent.ShouldBeFalse());
        item.Assignments[1].ShouldSatisfyAllConditions(
            a => a.PositionId.ShouldBe(setup.PositionA),
            a => a.OrgUnitName.ShouldBe("Satış Direktörlüğü"),
            a => a.JobName.ShouldBe("Satış Uzmanı"),
            a => a.ValidTo.ShouldBe(new DateOnly(2026, 1, 31)),
            a => a.IsCurrent.ShouldBeTrue());

        (await admin.GetAsync($"{Url}/{Guid.NewGuid()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Card_AfterAGroupTransfer_ShowsOnlyTheEmploymentsInTheScope()
    {
        var admin = factory.Admin();
        var source = await EmployeeSetup.CreateAsync(admin);
        var target = await EmployeeSetup.CreateAsync(admin);
        var id = await source.HireAsync(admin, new DateOnly(2025, 6, 1));
        var employment = (await EmployeeSetup.LoadAsync(factory, id)).Employments[0].Id.Value;
        (await admin.PostAsJsonAsync(
            $"{Url}/{id}/group-transfer",
            new
            {
                employmentId = employment,
                targetCompanyId = target.CompanyId,
                transferDate = "2026-01-01",
                sgkExitCode = "16",
                employmentType = "Indefinite",
                positionId = target.PositionA,
            },
            Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await CardAsync(admin, id)).Employments.Select(e => (e.CompanyId, e.Status)).ShouldBe(
            [(target.CompanyId, EmploymentStatus.Active), (source.CompanyId, EmploymentStatus.Terminated)]);

        // PLAN H2 acceptance: the transfer keeps the seniority date
        var scoped = factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Employees.Read).WithScopes(AccessScope.Company(target.CompanyId));
        (await CardAsync(scoped, id)).Employments.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.CompanyId.ShouldBe(target.CompanyId),
            e => e.SeniorityDate.ShouldBe(new DateOnly(2025, 6, 1)));
    }

    [Fact]
    public async Task Card_MasksSensitiveValues_WithoutThePermission_AndAuditsTheirRead()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var id = await setup.HireAsync(admin, new DateOnly(2025, 12, 1));
        var employee = await EmployeeSetup.LoadAsync(factory, id);
        var iban = TurkishData.Iban().Replace(" ", string.Empty, StringComparison.Ordinal);
        (await admin.PutAsJsonAsync($"{Url}/{id}/iban", new { version = await EmployeeSetup.VersionAsync(factory, id), iban }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.PutAsJsonAsync($"{Url}/{id}/sensitive-data", new { disabilityDegree = 40, bloodType = "A Rh+" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // PLAN H2 acceptance: masked for a user without hr.employees.sensitive.read, and nothing is audited
        var reader = factory.CreateClientFor("okuyucu", HrFactory.Tenant, HrPermissions.Employees.Read);
        (await CardAsync(reader, id)).ShouldSatisfyAllConditions(
            c => c.SensitiveVisible.ShouldBeFalse(),
            c => c.Tckn.ShouldBe(string.Concat(employee.Tckn.AsSpan(0, 3), "******", employee.Tckn.AsSpan(9, 2))),
            c => c.Iban.ShouldBe($"TR** **** … {iban[^4..]}"),
            c => c.Sensitive.ShouldBe(new EmployeeSensitiveItem(true, null, null, null, null)));
        (await ReadsAsync(id)).ShouldBeEmpty();

        // Shown in full with the permission, and every value shown is audited
        (await CardAsync(admin, id)).ShouldSatisfyAllConditions(
            c => c.SensitiveVisible.ShouldBeTrue(),
            c => c.Tckn.ShouldBe(employee.Tckn),
            c => c.Iban.ShouldBe(iban),
            c => c.Sensitive.ShouldBe(new EmployeeSensitiveItem(false, 40, "A Rh+", null, null)));
        (await ReadsAsync(id)).ShouldBe(
            [
                (nameof(Employee), nameof(Employee.Tckn)),
                (nameof(Employee), nameof(Employee.Iban)),
                (nameof(EmployeeSensitiveData), nameof(EmployeeSensitiveData.DisabilityDegree)),
                (nameof(EmployeeSensitiveData), nameof(EmployeeSensitiveData.BloodType)),
            ],
            ignoreOrder: true);
    }

    [Fact]
    public async Task Photo_IsShownOnTheCardAndTheList_AndCanBeReplacedAndRemoved()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var id = await setup.HireAsync(admin, new DateOnly(2025, 12, 1));
        var version = await EmployeeSetup.VersionAsync(factory, id);

        var first = await SetPhotoAsync(admin, id, "foto.png", SampleFiles.Png());
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync(Ct));
        var firstId = (await first.Content.ReadFromJsonAsync<EntityCreatedResponse>(Ct))!.Id;
        var card = await CardAsync(admin, id);
        card.PhotoFileId.ShouldBe(firstId);
        // A file of the person, not a field: the card keeps its version
        card.Version.ShouldBe(version);
        (await ListAsync(admin, $"companyId={setup.CompanyId}")).Items.ShouldHaveSingleItem().PhotoFileId.ShouldBe(firstId);

        var image = await admin.GetAsync($"{Url}/{id}/photo", Ct);
        image.StatusCode.ShouldBe(HttpStatusCode.OK);
        image.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        (await image.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(SampleFiles.Png());

        var second = await SetPhotoAsync(admin, id, "foto.jpg", SampleFiles.Jpeg());
        var secondId = (await second.Content.ReadFromJsonAsync<EntityCreatedResponse>(Ct))!.Id;
        (await CardAsync(admin, id)).PhotoFileId.ShouldBe(secondId);
        (await admin.GetAsync($"{Url}/{id}/photo", Ct)).Content.Headers.ContentType!.MediaType.ShouldBe("image/jpeg");

        (await admin.DeleteAsync($"{Url}/{id}/photo", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await CardAsync(admin, id)).PhotoFileId.ShouldBeNull();
        (await admin.GetAsync($"{Url}/{id}/photo", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Photo_ThatIsNotAnImage_Returns400_OutsideTheScope404_AndWithoutUpdate403()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var other = await EmployeeSetup.CreateAsync(admin);
        var id = await setup.HireAsync(admin, new DateOnly(2025, 12, 1));

        var pdf = await SetPhotoAsync(admin, id, "foto.pdf", SampleFiles.Pdf());
        pdf.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(pdf)).Errors!.Keys.ShouldBe(["file"]);

        var scoped = factory.Admin().WithScopes(AccessScope.Company(other.CompanyId));
        (await SetPhotoAsync(scoped, id, "foto.png", SampleFiles.Png())).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await scoped.GetAsync($"{Url}/{id}/photo", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var reader = factory.CreateClientFor("okuyucu", HrFactory.Tenant, HrPermissions.Employees.Read);
        (await SetPhotoAsync(reader, id, "foto.png", SampleFiles.Png())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SgkExitCodes_ListsTheSeededCodes()
    {
        var codes = (await factory.Admin().GetFromJsonAsync<List<SgkExitCodeItem>>("/api/hr/sgk-exit-codes", Ct))!;

        codes.ShouldContain(c => c.Code == "03" && c.IsActive && c.Name.Length > 0);
        codes.Select(c => c.Code).ShouldBeUnique();
        (await factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Org.Read).GetAsync("/api/hr/sgk-exit-codes", Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static async Task<PagedResult<EmployeeListItem>> ListAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"{Url}?{query}", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<PagedResult<EmployeeListItem>>(Ct))!;
    }

    private static async Task<EmployeeDetails> CardAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"{Url}/{id}", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<EmployeeDetails>(Ct))!;
    }

    private static Task<HttpResponseMessage> SetPhotoAsync(HttpClient client, Guid id, string fileName, byte[] content)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        return client.PutAsync($"{Url}/{id}/photo", form, Ct);
    }

    private Task<List<(string Entity, string? Field)>> ReadsAsync(Guid id) =>
        factory.QueryAsync(async db => (await db.AuditEntries.AsNoTracking()
                .Where(e => e.EntityId == id.ToString() && e.Action == AuditAction.Read)
                .Select(e => new { e.EntityType, e.Field })
                .ToListAsync(Ct))
            .Select(e => (e.EntityType, e.Field))
            .ToList());
}
