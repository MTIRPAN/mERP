using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.Approvals;
using Merp.Modules.HR.Application.Employees;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.IntegrationTests.Organization;
using Merp.Modules.Platform.Api.Endpoints;
using Merp.Modules.Platform.Contracts.Approvals;
using Merp.Modules.Platform.Contracts.Users;
using Merp.Modules.Platform.Infrastructure.Persistence;
using Merp.Testing.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.HR.IntegrationTests.Employees;

/// <summary>
/// The link of employees and users (PLAN H3a, D39), with the messages between HR and Platform delivered by the test.
/// The fake clock stands on 5 January 2026 (Istanbul).
/// </summary>
public sealed class EmployeeUserTests(HrClockFactory factory) : IClassFixture<HrClockFactory>
{
    private const string Url = "/api/hr/employees";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateUser_OpensTheUserWithTheEmployeeRole_AndLinksBothSides()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var employee = await setup.HireAsync(admin, new DateOnly(2025, 12, 1));
        var email = Email();

        (await admin.PostAsJsonAsync($"{Url}/{employee}/user", new { email }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await CardAsync(admin, employee)).UserLinkState.ShouldBe(UserLinkState.Pending);
        // One request at a time
        (await admin.PostAsJsonAsync($"{Url}/{employee}/user", new { email }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await factory.DeliverMessagesAsync();

        var card = await CardAsync(admin, employee);
        card.ShouldSatisfyAllConditions(
            c => c.UserLinkState.ShouldBe(UserLinkState.Linked),
            c => c.User.ShouldNotBeNull(),
            c => c.User!.UserName.ShouldBe(email),
            c => c.User!.IsActive.ShouldBeTrue(),
            c => c.UserId.ShouldBe(c.User!.Id));
        var userId = card.UserId!.Value;

        // Platform keeps the other side of the link and gives the Employee role with the Self scope
        var user = await PlatformUserAsync(userId);
        user.EmployeeId.ShouldBe(employee);
        user.Roles.ShouldContain(("EMPLOYEE", AccessScopeTypes.Self));

        // The user's requests know their employee (ICurrentUser.EmployeeId)
        var me = (await factory.CreateClientFor(SubjectOf(email), HrFactory.Tenant).GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!;
        me.User.Id.ShouldBe(userId);
        me.User.EmployeeId.ShouldBe(employee);
    }

    [Fact]
    public async Task CreateUser_WithAnEmailInUse_ShowsWhyOnTheCard_AndWithoutAnyEmail_Returns400()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var first = await setup.HireAsync(admin, new DateOnly(2025, 12, 1));
        var second = await setup.HireAsync(admin, new DateOnly(2025, 12, 1));
        var email = Email();
        await admin.PostAsJsonAsync($"{Url}/{first}/user", new { email }, Ct);
        await factory.DeliverMessagesAsync();

        (await admin.PostAsJsonAsync($"{Url}/{second}/user", new { email, userName = "baska." + Guid.NewGuid().ToString("N")[..6] }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await factory.DeliverMessagesAsync();

        (await CardAsync(admin, second)).ShouldSatisfyAllConditions(
            c => c.UserLinkState.ShouldBe(UserLinkState.Failed),
            c => c.UserLinkError.ShouldBe("Bu e-posta adresiyle kayıtlı bir kullanıcı var."),
            c => c.UserId.ShouldBeNull());

        // The person has no e-mail on the card and none was given
        var third = await setup.HireAsync(admin, new DateOnly(2025, 12, 1));
        var missing = await admin.PostAsJsonAsync($"{Url}/{third}/user", new { }, Ct);
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(missing)).Errors!.Keys.ShouldBe(["email"]);
    }

    [Fact]
    public async Task LinkAndUnlink_AnExistingUser()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var employee = await setup.HireAsync(admin, new DateOnly(2025, 12, 1));
        var subject = "u" + Guid.NewGuid().ToString("N")[..10];
        var userId = (await factory.CreateClientFor(subject, HrFactory.Tenant).GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!.User.Id!.Value;

        // The candidates are the users not linked to anybody
        var candidates = (await admin.GetFromJsonAsync<List<UserSummary>>($"{Url}/user-candidates?search={subject}", Ct))!;
        candidates.ShouldHaveSingleItem().Id.ShouldBe(userId);

        (await admin.PostAsJsonAsync($"{Url}/{employee}/user/link", new { userId }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await factory.DeliverMessagesAsync();
        (await CardAsync(admin, employee)).UserId.ShouldBe(userId);
        (await PlatformUserAsync(userId)).Roles.ShouldContain(("EMPLOYEE", AccessScopeTypes.Self));
        (await admin.GetFromJsonAsync<List<UserSummary>>($"{Url}/user-candidates?search={subject}", Ct))!.ShouldBeEmpty();

        // A user cannot be linked to a second person
        var other = await setup.HireAsync(admin, new DateOnly(2025, 12, 1));
        await admin.PostAsJsonAsync($"{Url}/{other}/user/link", new { userId }, Ct);
        await factory.DeliverMessagesAsync();
        (await CardAsync(admin, other)).UserLinkError.ShouldBe("Kullanıcı başka bir personele bağlı.");

        (await admin.DeleteAsync($"{Url}/{employee}/user", Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await factory.DeliverMessagesAsync();
        (await CardAsync(admin, employee)).ShouldSatisfyAllConditions(
            c => c.UserId.ShouldBeNull(),
            c => c.UserLinkState.ShouldBe(UserLinkState.None));
        (await PlatformUserAsync(userId)).EmployeeId.ShouldBeNull();
        (await admin.DeleteAsync($"{Url}/{employee}/user", Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UserEndpoints_NeedTheirPermission_AndThePersonInScope()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var other = await EmployeeSetup.CreateAsync(admin);
        var employee = await setup.HireAsync(admin, new DateOnly(2025, 12, 1));

        var withoutPermission = factory.CreateClientFor("ik", HrFactory.Tenant, HrPermissions.Employees.Read, HrPermissions.Employees.Update);
        (await withoutPermission.PostAsJsonAsync($"{Url}/{employee}/user", new { email = Email() }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await withoutPermission.GetAsync($"{Url}/user-candidates", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var elsewhere = factory.Admin().WithScopes(AccessScope.Company(other.CompanyId));
        (await elsewhere.PostAsJsonAsync($"{Url}/{employee}/user", new { email = Email() }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await elsewhere.PostAsJsonAsync($"{Url}/{employee}/user/link", new { userId = Guid.NewGuid() }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await admin.PostAsJsonAsync($"{Url}/{employee}/user/link", new { userId = Guid.Empty }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Leaving_DeactivatesTheUser_AfterTheTerminationDate_ButNotWhileAnotherEmploymentLasts()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var target = await EmployeeSetup.CreateAsync(admin);
        var leaver = await LinkedEmployeeAsync(admin, setup);
        var transferred = await LinkedEmployeeAsync(admin, setup);
        var later = await LinkedEmployeeAsync(admin, setup);

        await TerminateAsync(admin, leaver.Employee, "2026-01-02");
        await TerminateAsync(admin, later.Employee, "2026-01-20");
        var employment = (await EmployeeSetup.LoadAsync(factory, transferred.Employee)).Employments[0].Id.Value;
        (await admin.PostAsJsonAsync(
            $"{Url}/{transferred.Employee}/group-transfer",
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

        // The default setting waits for the termination date; the hourly job asks Platform after it
        factory.Identity.Enabled[leaver.Subject].ShouldBeTrue();
        (await DeactivationsAsync()).ShouldBe(1);
        await factory.DeliverMessagesAsync();

        (await CardAsync(admin, leaver.Employee)).User!.IsActive.ShouldBeFalse();
        factory.Identity.Enabled[leaver.Subject].ShouldBeFalse();
        (await CardAsync(admin, later.Employee)).User!.IsActive.ShouldBeTrue();
        (await CardAsync(admin, transferred.Employee)).User!.IsActive.ShouldBeTrue();

        // Asked once
        (await DeactivationsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Leaving_WithTheImmediateSetting_DeactivatesTheUserAtOnce()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var settings = factory.CreateClientFor("ayarci", HrFactory.Tenant, "platform.settings.read", "platform.settings.manage");
        (await settings.PutAsJsonAsync(
            "/api/platform/settings",
            new { companyId = setup.CompanyId, values = new Dictionary<string, string> { [HrSettingDefinitions.UserDeactivation] = HrSettingDefinitions.Immediately } },
            Ct)).IsSuccessStatusCode.ShouldBeTrue();
        var leaver = await LinkedEmployeeAsync(admin, setup);

        await TerminateAsync(admin, leaver.Employee, "2026-01-31");
        await factory.DeliverMessagesAsync();

        (await CardAsync(admin, leaver.Employee)).User!.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task LineManagerTwoLevelsUp_IsTheUserHoldingThePositionTwoLevelsAbove()
    {
        // D37 acceptance carried to H3: line-manager(2) end to end, with real holders and their users
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var director = await HrFactory.CreatePositionAsync(admin, setup.UnitId, setup.JobId);
        var manager = await HrFactory.CreatePositionAsync(admin, setup.UnitId, setup.JobId, reportsTo: director);
        var specialist = await HrFactory.CreatePositionAsync(admin, setup.UnitId, setup.JobId, reportsTo: manager);
        var top = await LinkedEmployeeAsync(admin, setup, director);
        var middle = await LinkedEmployeeAsync(admin, setup, manager);
        var requester = await LinkedEmployeeAsync(admin, setup, specialist);

        async Task<IReadOnlyList<Guid>> ResolveAsync(string level) =>
            await factory.InTenantAsync(services => services.GetServices<IApproverResolver>()
                .Single(r => r.Key == LineManagerApproverResolver.ResolverKey)
                .ResolveAsync(
                    new ApproverResolutionContext("hr.test", setup.CompanyId, requester.UserId, requester.Employee, JsonDocument.Parse("{}").RootElement),
                    new Dictionary<string, string> { [LineManagerApproverResolver.LevelParameter] = level },
                    Ct));

        (await ResolveAsync("1")).ShouldBe([middle.UserId]);
        (await ResolveAsync("2")).ShouldBe([top.UserId]);
    }

    private static string Email() => $"personel.{Guid.NewGuid().ToString("N")[..10]}@acme.test";

    private string SubjectOf(string email) => factory.Identity.Identities.Single(i => i.Value.Email == email).Key;

    private async Task<(Guid Employee, Guid UserId, string Subject)> LinkedEmployeeAsync(HttpClient admin, EmployeeSetup setup, Guid? positionId = null)
    {
        var employee = await setup.HireAsync(admin, new DateOnly(2025, 12, 1), positionId is null ? null : new { positionId });
        var email = Email();
        (await admin.PostAsJsonAsync($"{Url}/{employee}/user", new { email }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await factory.DeliverMessagesAsync();
        var userId = (await CardAsync(admin, employee)).UserId!.Value;
        return (employee, userId, SubjectOf(email));
    }

    private static async Task TerminateAsync(HttpClient admin, Guid employee, string date)
    {
        var card = await CardAsync(admin, employee);
        (await admin.PostAsJsonAsync(
            $"{Url}/{employee}/terminate",
            new { employmentId = card.Employments[0].Id, date, sgkExitCode = "03", rehireable = true },
            Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private Task<int> DeactivationsAsync() =>
        factory.InTenantAsync(async services => (await services.GetRequiredService<ISender>().Send(new RequestUserDeactivationsCommand(), Ct)).Value);

    private async Task<(Guid? EmployeeId, List<(string Code, string Scope)> Roles)> PlatformUserAsync(Guid userId) =>
        await factory.InTenantAsync(async services =>
        {
            var db = services.GetRequiredService<PlatformDbContext>();
            var user = await db.Users.AsNoTracking().Include(u => u.Assignments).SingleAsync(u => u.Id == Platform.Domain.Users.UserId.From(userId), Ct);
            var roleIds = user.Assignments.Select(a => a.RoleId).ToList();
            var codes = await db.Roles.AsNoTracking().Where(r => roleIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Code, Ct);
            return (user.EmployeeId, user.Assignments.Select(a => (codes[a.RoleId], a.Scope.Type)).ToList());
        });

    private static async Task<EmployeeDetails> CardAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"{Url}/{id}", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<EmployeeDetails>(Ct))!;
    }
}
