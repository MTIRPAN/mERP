using System.Net;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.Platform.Api.Endpoints;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Companies;
using Merp.Modules.Platform.Application.Roles;
using Merp.Modules.Platform.IntegrationTests.Organization;

namespace Merp.Modules.Platform.IntegrationTests.Access;

/// <summary>PLAN P2 acceptance criteria and the request pipeline of signed-in users.</summary>
public sealed class AccessControlTests(AccessFactory factory) : IClassFixture<AccessFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ChangingThePermissionMatrix_ChangesMenuAndApiAccessOnTheNextRequest()
    {
        var subject = AccessFactory.NewSubject();
        var userId = await factory.UserIdAsync(subject);
        var roleId = await factory.CreateRoleAsync([PlatformPermissions.Companies.Read]);
        await factory.AssignAsync(userId, roleId);
        var user = factory.As(subject);

        (await user.GetAsync("/api/platform/companies", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var me = (await user.GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!;
        me.Permissions.ShouldBe([PlatformPermissions.Companies.Read]);
        MenuPaths(me).ShouldContain("/organization/companies");

        var role = (await factory.AsRoot().GetFromJsonAsync<RoleDetails>($"/api/platform/roles/{roleId}", Ct))!;
        var update = await factory.AsRoot().PutAsJsonAsync(
            $"/api/platform/roles/{roleId}",
            new { version = role.Version, name = role.Name, description = (string?)null, permissions = new[] { PlatformPermissions.Branches.Read } },
            Ct);
        update.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await user.GetAsync("/api/platform/companies", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await user.GetAsync("/api/platform/branches", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        me = (await user.GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!;
        me.Permissions.ShouldBe([PlatformPermissions.Branches.Read]);
        MenuPaths(me).ShouldNotContain("/organization/companies");
        MenuPaths(me).ShouldContain("/organization/branches");
    }

    [Fact]
    public async Task ExpiredOrFutureAssignments_GrantNothing_AndChangingTheDatesTakesEffectAtOnce()
    {
        var subject = AccessFactory.NewSubject();
        var userId = await factory.UserIdAsync(subject);
        var roleId = await factory.CreateRoleAsync([PlatformPermissions.Companies.Read]);
        var today = AccessFactory.Today;
        var expired = await factory.AssignAsync(userId, roleId, "Tenant", from: today.AddDays(-30), to: today.AddDays(-1));
        await factory.AssignAsync(userId, roleId, "Tenant", from: today.AddDays(1));
        var user = factory.As(subject);

        (await user.GetAsync("/api/platform/companies", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await user.GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!.Permissions.ShouldBeEmpty();

        // The last day of an assignment still counts
        var extend = await factory.AsRoot().PutAsJsonAsync($"/api/platform/users/{userId}/assignments/{expired}", new { validFrom = today.AddDays(-30), validTo = today }, Ct);
        extend.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await user.GetAsync("/api/platform/companies", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var details = await factory.UserAsync(userId);
        details.Assignments.Count(a => a.IsCurrent).ShouldBe(1);
    }

    [Fact]
    public async Task ScopesOfSeveralAssignments_AreUnited()
    {
        var root = factory.AsRoot();
        var first = await OrganizationFactory.CreateCompanyAsync(root);
        var second = await OrganizationFactory.CreateCompanyAsync(root);
        var third = await OrganizationFactory.CreateCompanyAsync(root);
        var outside = await OrganizationFactory.CreateCompanyAsync(root);
        var thirdBranch = await OrganizationFactory.CreateBranchAsync(root, third);
        await OrganizationFactory.CreateBranchAsync(root, third);

        var subject = AccessFactory.NewSubject();
        var userId = await factory.UserIdAsync(subject);
        var readCompanies = await factory.CreateRoleAsync([PlatformPermissions.Companies.Read, PlatformPermissions.Branches.Read]);
        var readBranches = await factory.CreateRoleAsync([PlatformPermissions.Branches.Read]);
        await factory.AssignAsync(userId, readCompanies, "Company", first);
        await factory.AssignAsync(userId, readCompanies, "Company", second);
        await factory.AssignAsync(userId, readBranches, "Branch", thirdBranch);
        var user = factory.As(subject);

        var companies = (await user.GetFromJsonAsync<PagedResult<CompanyListItem>>("/api/platform/companies?pageSize=200", Ct))!;
        companies.Items.Select(c => c.Id).ShouldBe([first, second], ignoreOrder: true);

        var branches = (await user.GetFromJsonAsync<PagedResult<Application.Branches.BranchListItem>>("/api/platform/branches?pageSize=200", Ct))!;
        branches.Items.Select(b => b.Id).ShouldBe([thirdBranch]);

        // The selector unites the scopes of all permissions: the branch scope reaches its company
        var me = (await user.GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!;
        me.Companies.Select(c => c.Id).ShouldBe([first, second, third], ignoreOrder: true);
        me.Companies.Select(c => c.Id).ShouldNotContain(outside);
    }

    [Fact]
    public async Task FirstSignIn_CreatesAUserWithoutPermissions()
    {
        var subject = AccessFactory.NewSubject("yeni");
        var me = (await factory.As(subject).GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!;

        me.User.Id.ShouldNotBeNull();
        me.Permissions.ShouldBeEmpty();
        MenuPaths(me).ShouldBe(["/"]);
        (await factory.As(subject).GetAsync("/api/platform/system/info", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var details = await factory.UserAsync(me.User.Id!.Value);
        details.UserName.ShouldBe(subject);
        details.IsActive.ShouldBeTrue();
        details.Assignments.ShouldBeEmpty();

        // The same identity maps to the same user
        (await factory.UserIdAsync(subject)).ShouldBe(me.User.Id.Value);
    }

    [Fact]
    public async Task BootstrapUser_IsSystemAdministrator()
    {
        var me = (await factory.AsRoot().GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!;

        me.Permissions.ShouldContain(PlatformPermissions.Users.Manage);
        me.Permissions.ShouldContain(PlatformPermissions.Companies.Manage);
        me.Permissions.ShouldNotContain("recruitment.cv.analyze");
        var root = await factory.UserAsync(me.User.Id!.Value);
        root.Assignments.ShouldHaveSingleItem().ScopeType.ShouldBe("Tenant");
    }

    [Fact]
    public async Task DeactivatedUser_IsRefused_UntilReactivated()
    {
        var (user, userId) = await factory.UserWithAsync([PlatformPermissions.Companies.Read]);
        (await user.GetAsync("/api/platform/companies", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await factory.AsRoot().PutAsJsonAsync($"/api/platform/users/{userId}/active", new { isActive = false }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var refused = await user.GetAsync("/api/platform/me", Ct);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Problem.ReadAsync(refused)).Detail.ShouldBe("Hesabınız pasif durumda. Sistem yöneticinize başvurun.");

        (await factory.AsRoot().PutAsJsonAsync($"/api/platform/users/{userId}/active", new { isActive = true }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await user.GetAsync("/api/platform/companies", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Preferences_KeepTheChosenCulture()
    {
        var subject = AccessFactory.NewSubject();
        var user = factory.As(subject);

        var unsupported = await user.PutAsJsonAsync("/api/platform/me/preferences", new { culture = "de-DE" }, Ct);
        unsupported.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(unsupported)).Errors!.ShouldContainKey("culture");

        (await user.PutAsJsonAsync("/api/platform/me/preferences", new { culture = "tr-TR" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var me = (await user.GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!;
        me.Preferences.PreferredCulture.ShouldBe("tr-TR");
        me.Preferences.Culture.ShouldBe("tr-TR");

        (await user.PutAsJsonAsync("/api/platform/me/preferences", new { culture = (string?)null }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await user.GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!.Preferences.PreferredCulture.ShouldBeNull();

        (await factory.CreateClient().PutAsJsonAsync("/api/platform/me/preferences", new { culture = "tr-TR" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UsersOfAnotherTenant_AreSeparate()
    {
        var subject = AccessFactory.NewSubject();
        var acmeId = await factory.UserIdAsync(subject, "acme");
        var globexId = await factory.UserIdAsync(subject, "globex");

        globexId.ShouldNotBe(acmeId);
        (await factory.AsRoot("globex").GetAsync($"/api/platform/users/{acmeId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static List<string> MenuPaths(MeResponse me) => [.. Flatten(me.Menu).Select(m => m.Path).OfType<string>()];

    private static IEnumerable<MenuItemResponse> Flatten(IEnumerable<MenuItemResponse> items) =>
        items.SelectMany(i => i.Children is null ? [i] : Flatten(i.Children).Prepend(i));
}
