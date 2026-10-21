using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.Documents;
using Merp.Modules.HR.IntegrationTests.Employees;
using Merp.Modules.HR.IntegrationTests.Organization;
using Merp.Modules.Platform.Contracts.Notifications;
using Merp.Testing.Data;
using Merp.Testing.Web;
using Merp.Web.Endpoints;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.HR.IntegrationTests.Documents;

/// <summary>
/// Employee documents and document types (PLAN H2b2, D38) on the fake clock, which stands on 5 January 2026
/// (Istanbul).
/// </summary>
public sealed class DocumentEndpointsTests(HrClockFactory factory) : IClassFixture<HrClockFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Document_IsAddedListedDownloadedChangedAndDeleted()
    {
        var admin = factory.Admin();
        var (employee, type) = await PersonAndTypeAsync(admin);
        var url = $"/api/hr/employees/{employee}/documents";

        var created = await AddAsync(admin, employee, type, "rapor.pdf", SampleFiles.Pdf(), validUntil: "2026-01-25", number: "SR-1");
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var id = (await created.Content.ReadFromJsonAsync<EntityCreatedResponse>(Ct))!.Id;

        var list = await ListAsync(admin, employee);
        list.CanManage.ShouldBeTrue();
        list.Extensions.ShouldContain("pdf");
        var item = list.Items.ShouldHaveSingleItem();
        item.ShouldSatisfyAllConditions(
            d => d.Id.ShouldBe(id),
            d => d.TypeId.ShouldBe(type),
            d => d.TypeCode.ShouldNotBeNullOrEmpty(),
            d => d.TypeName.ShouldNotBeNullOrEmpty(),
            d => d.Number.ShouldBe("SR-1"),
            d => d.ValidUntil.ShouldBe(new DateOnly(2026, 1, 25)),
            d => d.DaysLeft.ShouldBe(20),
            d => d.FileName.ShouldBe("rapor.pdf"),
            d => d.ContentType.ShouldBe("application/pdf"));

        var file = await admin.GetAsync($"{url}/{id}/file", Ct);
        file.StatusCode.ShouldBe(HttpStatusCode.OK);
        file.Content.Headers.ContentDisposition!.FileName.ShouldNotBeNull();
        (await file.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(SampleFiles.Pdf());

        (await admin.PutAsJsonAsync($"{url}/{id}", new { version = item.Version, typeId = type, validUntil = "2027-01-25", note = "Yenilendi" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.PutAsJsonAsync($"{url}/{id}", new { version = item.Version, typeId = type }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ListAsync(admin, employee)).Items.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            d => d.Number.ShouldBeNull(),
            d => d.Note.ShouldBe("Yenilendi"),
            d => d.DaysLeft.ShouldBe(385));

        (await ReplaceFileAsync(admin, employee, id, "rapor.png", SampleFiles.Png())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetAsync($"{url}/{id}/file", Ct)).Content.Headers.ContentType!.MediaType.ShouldBe("image/png");

        (await admin.DeleteAsync($"{url}/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListAsync(admin, employee)).Items.ShouldBeEmpty();
        (await admin.GetAsync($"{url}/{id}/file", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await factory.AuditedFieldsAsync("EmployeeDocument", id)).ShouldContain("ValidUntil");
    }

    [Fact]
    public async Task Document_WithInvalidFields_Returns400()
    {
        var admin = factory.Admin();
        var (employee, type) = await PersonAndTypeAsync(admin);

        var noType = await AddAsync(admin, employee, null, "rapor.pdf", SampleFiles.Pdf());
        noType.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(noType)).Errors!.Keys.ShouldBe(["typeId"]);

        var backwards = await AddAsync(admin, employee, type, "rapor.pdf", SampleFiles.Pdf(), validUntil: "2025-01-01", issuedOn: "2025-06-01");
        (await Problem.ReadAsync(backwards)).Errors!["validUntil"].ShouldBe(["Geçerlilik bitişi, veriliş tarihinden önce olamaz."]);

        var unknown = await AddAsync(admin, employee, Guid.NewGuid(), "rapor.pdf", SampleFiles.Pdf());
        (await Problem.ReadAsync(unknown)).Errors!["typeId"].ShouldBe(["Belge türü bulunamadı ya da pasif."]);

        var program = await AddAsync(admin, employee, type, "rapor.exe", SampleFiles.Pdf());
        program.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(program)).Errors!.Keys.ShouldBe(["file"]);
        (await ListAsync(admin, employee)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Documents_FollowThePermissionsAndTheDataScope()
    {
        var admin = factory.Admin();
        var (employee, type) = await PersonAndTypeAsync(admin);
        var other = await EmployeeSetup.CreateAsync(admin);
        var id = (await (await AddAsync(admin, employee, type, "rapor.pdf", SampleFiles.Pdf())).Content.ReadFromJsonAsync<EntityCreatedResponse>(Ct))!.Id;
        var url = $"/api/hr/employees/{employee}/documents";

        // Reading without managing: the list says so, and changes are refused
        var reader = factory.CreateClientFor("okuyucu", HrFactory.Tenant, HrPermissions.Employees.Documents.Read);
        (await ListAsync(reader, employee)).CanManage.ShouldBeFalse();
        (await reader.GetAsync($"{url}/{id}/file", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AddAsync(reader, employee, type, "rapor.pdf", SampleFiles.Pdf())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.DeleteAsync($"{url}/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The employee permission alone does not show documents
        (await factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Employees.Read).GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Outside the scope the person has no documents
        var elsewhere = factory.Admin().WithScopes(AccessScope.Company(other.CompanyId));
        (await elsewhere.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await elsewhere.GetAsync($"{url}/{id}/file", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AddAsync(elsewhere, employee, type, "rapor.pdf", SampleFiles.Pdf())).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await elsewhere.DeleteAsync($"{url}/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A document of another person is not found under this one
        var stranger = await other.HireAsync(admin, new DateOnly(2025, 12, 1));
        (await admin.GetAsync($"/api/hr/employees/{stranger}/documents/{id}/file", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DocumentTypes_AreSeeded_AndTheTenantMaintainsThem()
    {
        var admin = factory.Admin();
        var types = await TypesAsync(admin);
        types.ShouldContain(t => t.Code == "HEALTH_REPORT" && t.Name == "Sağlık raporu");

        var code = HrFactory.NewCode("DT");
        var created = await admin.PostAsJsonAsync("/api/hr/document-types", new { code, name = new Dictionary<string, string> { ["tr-TR"] = "Hijyen belgesi" } }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var id = (await created.Content.ReadFromJsonAsync<EntityCreatedResponse>(Ct))!.Id;

        var duplicate = await admin.PostAsJsonAsync("/api/hr/document-types", new { code, name = new Dictionary<string, string> { ["tr-TR"] = "Başka" } }, Ct);
        (await Problem.ReadAsync(duplicate)).Errors!.Keys.ShouldBe(["code"]);
        (await admin.PostAsJsonAsync("/api/hr/document-types", new { code = HrFactory.NewCode("DT") }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var version = (await TypesAsync(admin)).Single(t => t.Id == id).Version;
        (await admin.PutAsJsonAsync($"/api/hr/document-types/{id}", new { version, name = new Dictionary<string, string> { ["tr-TR"] = "Hijyen eğitimi belgesi" }, isActive = false }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await TypesAsync(admin)).ShouldNotContain(t => t.Id == id);
        (await TypesAsync(admin, includeInactive: true)).Single(t => t.Id == id).Name.ShouldBe("Hijyen eğitimi belgesi");
        (await admin.PutAsJsonAsync($"/api/hr/document-types/{id}", new { version, name = new Dictionary<string, string> { ["tr-TR"] = "X" }, isActive = true }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await admin.PutAsJsonAsync($"/api/hr/document-types/{Guid.NewGuid()}", new { version, name = new Dictionary<string, string> { ["tr-TR"] = "X" }, isActive = true }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A retired type cannot be used for new documents
        var (employee, _) = await PersonAndTypeAsync(admin);
        (await AddAsync(admin, employee, id, "belge.pdf", SampleFiles.Pdf())).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // The types are shared by every company: a company scope or the read permission cannot change them
        var company = await HrFactory.CreateCompanyAsync(admin);
        var scoped = factory.Admin().WithScopes(AccessScope.Company(company));
        (await scoped.PostAsJsonAsync("/api/hr/document-types", new { code = HrFactory.NewCode("DT"), name = new Dictionary<string, string> { ["tr-TR"] = "X" } }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await factory.CreateClientFor("okuyucu", HrFactory.Tenant, HrPermissions.Employees.Documents.Read)
            .PostAsJsonAsync("/api/hr/document-types", new { code = HrFactory.NewCode("DT"), name = new Dictionary<string, string> { ["tr-TR"] = "X" } }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reminders_AreSentThirtyAndSevenDaysBefore_ToTheCompanysDocumentManagers()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var employee = await setup.HireAsync(admin, new DateOnly(2025, 12, 1), new { firstName = "Belgeli" });
        var type = (await TypesAsync(admin)).First(t => t.Code == "HEALTH_REPORT").Id;
        var manager = await DocumentManagerAsync(setup.CompanyId);
        await AddAsync(admin, employee, type, "rapor.pdf", SampleFiles.Pdf(), validUntil: "2026-01-25");
        await AddAsync(admin, employee, type, "eski.pdf", SampleFiles.Pdf(), validUntil: "2026-06-30");

        await RemindAsync();
        var reminders = await RemindersAsync(employee);
        var reminder = reminders.ShouldHaveSingleItem();
        reminder.ShouldSatisfyAllConditions(
            r => r.Type.ShouldBe(HrNotificationTypes.DocumentExpiring),
            r => r.RecipientUserIds.ShouldContain(manager),
            r => r.Args["employee"].ShouldBe("Belgeli Yılmaz"),
            r => r.Args["document"].ShouldBe("Sağlık raporu"),
            r => r.Args["date"].ShouldBe("25.01.2026"),
            r => r.Args["days"].ShouldBe("20"),
            r => r.CompanyId.ShouldBe(setup.CompanyId));

        // Sent once: the next run has nothing new for this person
        await RemindAsync();
        (await RemindersAsync(employee)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Reminders_SkipPeopleWhoLeft()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var employee = await setup.HireAsync(admin, new DateOnly(2025, 6, 1));
        var type = (await TypesAsync(admin)).First(t => t.Code == "IDENTITY").Id;
        await DocumentManagerAsync(setup.CompanyId);
        await AddAsync(admin, employee, type, "kimlik.pdf", SampleFiles.Pdf(), validUntil: "2026-01-10");
        var employment = (await EmployeeSetup.LoadAsync(factory, employee)).Employments[0].Id.Value;
        (await admin.PostAsJsonAsync($"/api/hr/employees/{employee}/terminate", new { employmentId = employment, date = "2026-01-02", sgkExitCode = "03", rehireable = true }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await RemindAsync();

        (await RemindersAsync(employee)).ShouldBeEmpty();
    }

    private static async Task<(Guid Employee, Guid Type)> PersonAndTypeAsync(HttpClient admin)
    {
        var setup = await EmployeeSetup.CreateAsync(admin);
        var employee = await setup.HireAsync(admin, new DateOnly(2025, 12, 1));
        return (employee, (await TypesAsync(admin)).First(t => t.Code == "HEALTH_REPORT").Id);
    }

    private static async Task<List<EmployeeDocumentTypeItem>> TypesAsync(HttpClient client, bool includeInactive = false) =>
        (await client.GetFromJsonAsync<List<EmployeeDocumentTypeItem>>($"/api/hr/document-types?includeInactive={includeInactive}", Ct))!;

    private static async Task<EmployeeDocumentList> ListAsync(HttpClient client, Guid employee)
    {
        var response = await client.GetAsync($"/api/hr/employees/{employee}/documents", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<EmployeeDocumentList>(Ct))!;
    }

    private static Task<HttpResponseMessage> AddAsync(
        HttpClient client,
        Guid employee,
        Guid? type,
        string fileName,
        byte[] content,
        string? validUntil = null,
        string? issuedOn = null,
        string? number = null)
    {
        var form = new MultipartFormDataContent();
        if (type is { } typeId)
        {
            form.Add(new StringContent(typeId.ToString()), "typeId");
        }

        if (validUntil is not null)
        {
            form.Add(new StringContent(validUntil), "validUntil");
        }

        if (issuedOn is not null)
        {
            form.Add(new StringContent(issuedOn), "issuedOn");
        }

        if (number is not null)
        {
            form.Add(new StringContent(number), "number");
        }

        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        return client.PostAsync($"/api/hr/employees/{employee}/documents", form, Ct);
    }

    private static Task<HttpResponseMessage> ReplaceFileAsync(HttpClient client, Guid employee, Guid document, string fileName, byte[] content)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        return client.PutAsync($"/api/hr/employees/{employee}/documents/{document}/file", form, Ct);
    }

    /// <summary>A platform user holding <c>hr.employees.documents.manage</c> with the company's scope.</summary>
    private async Task<Guid> DocumentManagerAsync(Guid companyId)
    {
        var root = factory.CreateClientFor("hr-root", HrFactory.Tenant, "platform.roles.manage", "platform.users.manage", "platform.users.read");
        var role = await root.PostAsJsonAsync(
            "/api/platform/roles",
            new
            {
                code = HrFactory.NewCode("R"),
                name = new Dictionary<string, string> { ["tr-TR"] = "Özlük sorumlusu" },
                permissions = new[] { HrPermissions.Employees.Documents.Manage },
            },
            Ct);
        role.StatusCode.ShouldBe(HttpStatusCode.Created, await role.Content.ReadAsStringAsync(Ct));
        var roleId = (await role.Content.ReadFromJsonAsync<EntityCreatedResponse>(Ct))!.Id;

        using var me = JsonDocument.Parse(await factory.CreateClientFor("u" + Guid.NewGuid().ToString("N")[..10], HrFactory.Tenant).GetStringAsync("/api/platform/me", Ct));
        var userId = me.RootElement.GetProperty("user").GetProperty("id").GetGuid();
        var assigned = await root.PostAsJsonAsync(
            $"/api/platform/users/{userId}/assignments",
            new { roleId, scopeType = AccessScopeTypes.Company, scopeId = companyId, includeSubUnits = false },
            Ct);
        assigned.StatusCode.ShouldBe(HttpStatusCode.Created, await assigned.Content.ReadAsStringAsync(Ct));
        return userId;
    }

    private Task<int> RemindAsync() =>
        factory.InTenantAsync(async services => (await services.GetRequiredService<ISender>().Send(new SendDocumentRemindersCommand(), Ct)).Value);

    private async Task<List<NotificationRequested>> RemindersAsync(Guid employee) =>
        [.. (await factory.OutboxAsync<NotificationRequested>()).Where(n => n.Link == $"/hr/employees/{employee}?tab=documents")];
}
