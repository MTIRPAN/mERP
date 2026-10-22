using System.Net;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Infrastructure.Persistence.Auditing;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.Documents;
using Merp.Modules.HR.Application.Employees;
using Merp.Modules.HR.Application.SelfService;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.SelfService;
using Merp.Modules.HR.IntegrationTests.Employees;
using Merp.Modules.HR.IntegrationTests.Organization;
using Merp.Modules.Platform.Api.Endpoints;
using Merp.Modules.Platform.Application.Approvals;
using Merp.Modules.Platform.Application.Roles;
using Merp.Modules.Platform.Contracts.Approvals;
using Merp.Modules.Platform.Contracts.Notifications;
using Merp.Testing.Data;
using Merp.Testing.Web;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.IntegrationTests.SelfService;

/// <summary>
/// Self service of the person linked to the user (PLAN H3b, D39): "Kartım" and change requests approved by Platform's
/// approval engine, whose default flow gives the task to the holders of the HR specialist role.
/// </summary>
public sealed class SelfServiceTests(HrClockFactory factory) : IClassFixture<HrClockFactory>
{
    private const string Me = "/api/hr/me";
    private const string Changes = "/api/hr/me/profile-changes";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MyCard_ShowsTheOwnTcknAndIban_AndAuditsTheRead()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var person = await LinkedEmployeeAsync(admin, setup);
        var iban = TurkishData.Iban();
        (await admin.PutAsJsonAsync(
            $"/api/hr/employees/{person.Employee}/iban",
            new { version = await EmployeeSetup.VersionAsync(factory, person.Employee), iban },
            Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var stored = await EmployeeSetup.LoadAsync(factory, person.Employee);

        var card = await MyCardAsync(person.Client);

        card.ShouldSatisfyAllConditions(
            c => c.Id.ShouldBe(person.Employee),
            c => c.Tckn.ShouldBe(stored.Tckn),
            c => c.Iban.ShouldBe(stored.Iban),
            c => c.Sensitive.Hidden.ShouldBeTrue());
        var reads = await factory.QueryAsync(db => db.AuditEntries.AsNoTracking()
            .Where(e => e.EntityType == nameof(Employee) && e.EntityId == person.Employee.ToString() && e.Action == AuditAction.Read && e.UserId == person.UserId)
            .Select(e => e.Field)
            .ToListAsync(Ct));
        reads.ShouldBe([nameof(Employee.Tckn), nameof(Employee.Iban)], ignoreOrder: true);

        // A user without a person, and one without the permission
        var unlinked = factory.CreateClientFor("u" + Guid.NewGuid().ToString("N")[..10], HrFactory.Tenant, HrPermissions.Self.ProfileRead);
        (await unlinked.GetAsync(Me, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await factory.CreateClientFor(person.Subject, HrFactory.Tenant, HrPermissions.Self.ProfileRequest).GetAsync(Me, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnEmployeeWithTheSelfScope_SeesTheirOwnCard_ButNotAnotherOne()
    {
        // PLAN H3 acceptance: the employee sees their own card and gets 404 for anybody else's
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var person = await LinkedEmployeeAsync(admin, setup);
        var colleague = await setup.HireAsync(admin, new DateOnly(2025, 12, 1));
        var employee = factory.CreateClientFor(person.Subject, HrFactory.Tenant, HrPermissions.Employees.Read)
            .WithScopes(new AccessScope(AccessScopeTypes.Self));

        (await employee.GetAsync($"/api/hr/employees/{person.Employee}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await employee.GetAsync($"/api/hr/employees/{colleague}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await employee.GetAsync($"/api/hr/employees/{colleague}/profile-changes", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AnApprovedAddressChange_IsWrittenToTheCard_UnderTheRequestNumber()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var specialist = await SpecialistAsync();
        var person = await LinkedEmployeeAsync(admin, setup);
        var body = new
        {
            emails = new[] { new { kind = "Personal", address = "ayse.yilmaz@ornek.test", isPrimary = true } },
            addresses = new[] { new { kind = "Home", line = "Bağdat Cad. No: 12", district = "Kadıköy", city = "İstanbul" } },
            note = "Taşındım",
        };

        var id = await HrFactory.CreatedIdAsync(await person.Client.PostAsJsonAsync($"{Changes}/contacts", body, Ct));
        var request = (await MyChangesAsync(person.Client)).ShouldHaveSingleItem();
        request.ShouldSatisfyAllConditions(
            r => r.Id.ShouldBe(id),
            r => r.Kind.ShouldBe(ProfileChangeKind.Contacts),
            r => r.Status.ShouldBe(ProfileChangeStatus.Pending),
            r => r.Number.ShouldStartWith("PDT-2026-"),
            r => r.Note.ShouldBe("Taşındım"));
        (await factory.OutboxAsync<ApprovalRequested>()).ShouldContain(e =>
            e.DocumentType == HrApprovalDocumentTypes.ProfileChange && e.DocumentId == id.ToString() && e.RequesterUserId == person.UserId && e.CompanyId == setup.CompanyId);
        // One open request per section
        (await person.Client.PostAsJsonAsync($"{Changes}/contacts", body, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await factory.DeliverMessagesAsync();
        await DecideAsync(specialist, id, "Approve");
        await factory.DeliverMessagesAsync();

        // PLAN H3 acceptance: the approved address is on the card and the audit shows the request's number
        (await MyChangesAsync(person.Client)).ShouldHaveSingleItem().Status.ShouldBe(ProfileChangeStatus.Applied);
        var card = await MyCardAsync(person.Client);
        card.Addresses.ShouldHaveSingleItem().Line.ShouldBe("Bağdat Cad. No: 12");
        card.Emails.ShouldHaveSingleItem().Address.ShouldBe("ayse.yilmaz@ornek.test");
        var sources = await factory.QueryAsync(db => db.AuditEntries.AsNoTracking()
            .Where(e => e.EntityType == nameof(Employee) && e.EntityId == person.Employee.ToString() && e.Field == nameof(Employee.Addresses) && e.Action == AuditAction.Update)
            .Select(e => e.Source)
            .ToListAsync(Ct));
        sources.ShouldBe([$"{HrApprovalDocumentTypes.ProfileChange}:{request.Number}"]);
        (await factory.OutboxAsync<NotificationRequested>()).ShouldContain(n =>
            n.Type == HrNotificationTypes.ProfileChangeCompleted && n.RecipientUserIds.Contains(person.UserId) && n.Args["number"] == request.Number);
    }

    [Fact]
    public async Task AnApprovedRequest_IsNotApplied_WhenHrChangedTheSectionMeanwhile()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var specialist = await SpecialistAsync();
        var person = await LinkedEmployeeAsync(admin, setup);
        var id = await HrFactory.CreatedIdAsync(await person.Client.PostAsJsonAsync(
            $"{Changes}/family",
            new { members = new[] { new { relation = "Child", fullName = "Ali Yılmaz", birthDate = "2018-04-23", isWorking = false } } },
            Ct));
        await factory.DeliverMessagesAsync();

        (await admin.PutAsJsonAsync(
            $"/api/hr/employees/{person.Employee}/family",
            new { version = await EmployeeSetup.VersionAsync(factory, person.Employee), members = new[] { new { relation = "Spouse", fullName = "Mehmet Yılmaz", isWorking = true } } },
            Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await DecideAsync(specialist, id, "Approve");
        await factory.DeliverMessagesAsync();

        (await MyChangesAsync(person.Client)).ShouldHaveSingleItem().Status.ShouldBe(ProfileChangeStatus.NotApplied);
        (await MyCardAsync(person.Client)).FamilyMembers.ShouldHaveSingleItem().FullName.ShouldBe("Mehmet Yılmaz");
    }

    [Fact]
    public async Task AReturnedRequest_IsCorrectedAndSentAgain_ThenRejected()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var specialist = await SpecialistAsync();
        var person = await LinkedEmployeeAsync(admin, setup);
        var id = await HrFactory.CreatedIdAsync(await person.Client.PostAsJsonAsync($"{Changes}/iban", new { iban = TurkishData.Iban() }, Ct));
        await factory.DeliverMessagesAsync();

        await DecideAsync(specialist, id, "Return", "Banka hesap cüzdanını ekleyin");
        await factory.DeliverMessagesAsync();
        (await MyChangesAsync(person.Client)).ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            r => r.Status.ShouldBe(ProfileChangeStatus.Returned),
            r => r.ReturnComment.ShouldBe("Banka hesap cüzdanını ekleyin"));

        var corrected = TurkishData.Iban();
        (await person.Client.PostAsJsonAsync($"{Changes}/iban", new { iban = corrected, note = "Doğru hesap", requestId = id }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        var resent = (await MyChangesAsync(person.Client)).ShouldHaveSingleItem();
        resent.ShouldSatisfyAllConditions(
            r => r.Status.ShouldBe(ProfileChangeStatus.Pending),
            r => r.ProposedIban.ShouldBe(corrected.Replace(" ", string.Empty, StringComparison.Ordinal)),
            r => r.ReturnComment.ShouldBeNull());
        await factory.DeliverMessagesAsync();

        await DecideAsync(specialist, id, "Reject", "Hesap şirket bankasında değil");
        await factory.DeliverMessagesAsync();
        (await MyChangesAsync(person.Client)).ShouldHaveSingleItem().Status.ShouldBe(ProfileChangeStatus.Rejected);
        (await MyCardAsync(person.Client)).Iban.ShouldBeNull();
        // Closed requests cannot be withdrawn
        (await person.Client.PostAsync($"{Changes}/{id}/withdraw", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Withdraw_ClosesTheRequest_AndAsksPlatformToCancelTheApproval()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var person = await LinkedEmployeeAsync(admin, setup);
        var other = await LinkedEmployeeAsync(admin, setup);
        var id = await HrFactory.CreatedIdAsync(await person.Client.PostAsJsonAsync($"{Changes}/iban", new { iban = TurkishData.Iban() }, Ct));

        // Only the requester
        (await other.Client.PostAsync($"{Changes}/{id}/withdraw", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await person.Client.PostAsync($"{Changes}/{id}/withdraw", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await MyChangesAsync(person.Client)).ShouldHaveSingleItem().Status.ShouldBe(ProfileChangeStatus.Withdrawn);
        (await factory.OutboxAsync<ApprovalCancellationRequested>()).ShouldContain(e =>
            e.DocumentType == HrApprovalDocumentTypes.ProfileChange && e.DocumentId == id.ToString());
        // A new request can be sent once the old one is closed
        (await person.Client.PostAsJsonAsync($"{Changes}/iban", new { iban = TurkishData.Iban() }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task HrSeesTheRequests_WithTheIbanMaskedWithoutTheSensitivePermission()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var elsewhere = await EmployeeSetup.CreateAsync(admin);
        var person = await LinkedEmployeeAsync(admin, setup);
        var iban = TurkishData.Iban().Replace(" ", string.Empty, StringComparison.Ordinal);
        var id = await HrFactory.CreatedIdAsync(await person.Client.PostAsJsonAsync($"{Changes}/iban", new { iban }, Ct));
        var url = $"/api/hr/employees/{person.Employee}/profile-changes";

        (await admin.GetFromJsonAsync<List<ProfileChangeItem>>(url, Ct))!.ShouldHaveSingleItem().ProposedIban.ShouldBe(iban);
        (await factory.QueryAsync(db => db.AuditEntries.AsNoTracking()
            .CountAsync(e => e.EntityType == nameof(ProfileChangeRequest) && e.EntityId == id.ToString() && e.Action == AuditAction.Read, Ct))).ShouldBe(1);

        var withoutSensitive = factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Employees.Read);
        var masked = (await withoutSensitive.GetFromJsonAsync<List<ProfileChangeItem>>(url, Ct))!.ShouldHaveSingleItem().ProposedIban;
        masked.ShouldNotBe(iban);
        masked.ShouldEndWith(iban[^4..]);

        (await factory.Admin().WithScopes(AccessScope.Company(elsewhere.CompanyId)).GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Org.Read).GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Submit_ValidatesTheValues_AndNeedsThePermissionAndAPerson()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var person = await LinkedEmployeeAsync(admin, setup);

        var invalid = await person.Client.PostAsJsonAsync($"{Changes}/iban", new { iban = "TR00 1234" }, Ct);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(invalid)).Errors!.Keys.ShouldBe(["iban"]);
        var badEmail = await person.Client.PostAsJsonAsync($"{Changes}/contacts", new { emails = new[] { new { kind = "Work", address = "ayse@", isPrimary = true } } }, Ct);
        (await Problem.ReadAsync(badEmail)).Errors!.Keys.ShouldBe(["emails[0].address"]);

        var readOnly = factory.CreateClientFor(person.Subject, HrFactory.Tenant, HrPermissions.Self.ProfileRead);
        (await readOnly.PostAsJsonAsync($"{Changes}/iban", new { iban = TurkishData.Iban() }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var unlinked = factory.CreateClientFor("u" + Guid.NewGuid().ToString("N")[..10], HrFactory.Tenant, HrPermissions.Self.ProfileRequest);
        (await unlinked.PostAsJsonAsync($"{Changes}/iban", new { iban = TurkishData.Iban() }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await unlinked.GetAsync(Changes, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<(Guid Employee, Guid UserId, string Subject, HttpClient Client)> LinkedEmployeeAsync(HttpClient admin, EmployeeSetup setup)
    {
        var employee = await setup.HireAsync(admin, new DateOnly(2025, 12, 1));
        var email = $"personel.{Guid.NewGuid().ToString("N")[..10]}@acme.test";
        (await admin.PostAsJsonAsync($"/api/hr/employees/{employee}/user", new { email }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await factory.DeliverMessagesAsync();
        var subject = factory.Identity.Identities.Single(i => i.Value.Email == email).Key;
        var client = factory.CreateClientFor(subject, HrFactory.Tenant, HrPermissions.Self.ProfileRead, HrPermissions.Self.ProfileRequest);
        var userId = (await client.GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!.User.Id!.Value;
        return (employee, userId, subject, client);
    }

    /// <summary>A user holding the HR specialist system role, the approver of the default flow.</summary>
    private async Task<HttpClient> SpecialistAsync()
    {
        var client = factory.CreateClientFor("uzman" + Guid.NewGuid().ToString("N")[..8], HrFactory.Tenant);
        var userId = (await client.GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!.User.Id!.Value;
        var users = factory.CreateClientFor("kullanici-yoneticisi", HrFactory.Tenant, "platform.roles.read", "platform.users.manage");
        var role = (await users.GetFromJsonAsync<PagedResult<RoleListItem>>("/api/platform/roles?pageSize=200", Ct))!
            .Items.Single(r => string.Equals(r.Code, "hr-specialist", StringComparison.OrdinalIgnoreCase));
        await HrFactory.CreatedIdAsync(await users.PostAsJsonAsync(
            $"/api/platform/users/{userId}/assignments",
            new { roleId = role.Id, scopeType = AccessScopeTypes.Tenant, includeSubUnits = false },
            Ct));
        return client;
    }

    private static async Task DecideAsync(HttpClient approver, Guid requestId, string decision, string? comment = null)
    {
        var task = (await approver.GetFromJsonAsync<PagedResult<MyApprovalTaskItem>>("/api/platform/approvals/my-tasks?pageSize=200", Ct))!
            .Items.Single(t => t.DocumentType == HrApprovalDocumentTypes.ProfileChange && t.DocumentId == requestId.ToString());
        var response = await approver.PostAsJsonAsync($"/api/platform/approvals/tasks/{task.TaskId}/decision", new { decision, comment }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<EmployeeDetails> MyCardAsync(HttpClient client)
    {
        var response = await client.GetAsync(Me, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<EmployeeDetails>(Ct))!;
    }

    private static async Task<List<ProfileChangeItem>> MyChangesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<ProfileChangeItem>>(Changes, Ct))!;
}
