using System.Net;
using System.Net.Http.Json;
using Merp.Modules.Platform.Api.Endpoints;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Access;

namespace Merp.BuildingBlocks.IntegrationTests.Web;

/// <summary>Endpoints the web shell loads at start-up.</summary>
public sealed class ShellEndpointsTests(WebHostFactory factory) : IClassFixture<WebHostFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Bundle_IsAnonymous_AndContainsOnlyWebTexts()
    {
        var response = await factory.CreateClient().GetAsync("/api/platform/localization/bundles/tr-TR", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.Public.ShouldBeTrue();
        var texts = (await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(Ct))!;
        texts["web.shell.menu.home"].ShouldBe("Ana sayfa");
        texts.Keys.ShouldAllBe(k => k.StartsWith("web.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Bundle_BeforeSignIn_FallsBackToTheEmbeddedTexts_ForAnyKnownCulture()
    {
        var response = await factory.CreateClient().GetAsync("/api/platform/localization/bundles/en-US", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(Ct))!["web.shell.menu.home"].ShouldBe("Ana sayfa");
        (await factory.CreateClient().GetAsync("/api/platform/localization/bundles/xx-YY", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Bundle_ForACultureTheTenantHasNotSwitchedOn_Returns404()
    {
        var response = await factory.CreateClientFor("employee-1", "acme").GetAsync("/api/platform/localization/bundles/en-US", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Headers.CacheControl!.Private.ShouldBeTrue();
    }

    [Fact]
    public async Task Me_RequiresSignIn() =>
        (await factory.CreateClient().GetAsync("/api/platform/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Me_WithoutPermissions_ShowsOnlyUnrestrictedMenu()
    {
        var me = (await factory.CreateClientFor("employee-1", "acme").GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!;

        me.Tenant.Code.ShouldBe("acme");
        me.User.Subject.ShouldBe("employee-1");
        me.Permissions.ShouldBeEmpty();
        // The approval inbox and delegations are every user's own (D34)
        me.Menu.Select(m => m.Key).ShouldBe(["home", "approvals"]);
        me.Preferences.Culture.ShouldBe("tr-TR");
        me.Cultures.Select(c => c.Code).ShouldBe(["tr-TR"]);
    }

    [Fact]
    public async Task Me_WithEveryPlatformPermission_ListsEveryPlatformScreen()
    {
        var permissions = new PlatformPermissionDefinitions().GetPermissions().Select(p => p.Code).ToArray();

        var me = (await factory.CreateClientFor("admin-all", "acme", permissions).GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!;

        // PLAN P13: every Platform screen is reachable from the menu, grouped for administration
        me.Menu.Select(m => m.Key).ShouldBe(["home", "approvals", "organization", "access", "configuration", "system"]);
        Leaves(me.Menu).ShouldBe(
            [
                "/", "/approvals", "/approvals/delegations", "/system/approval-requests", "/organization/companies", "/organization/branches",
                "/access/users", "/access/roles", "/system/settings", "/system/numbering", "/system/custom-fields", "/system/localization",
                "/system/holidays", "/system/work-schedules", "/system/approval-flows", "/system/document-templates",
                "/system/notification-templates", "/system/info", "/system/jobs", "/system/audit", "/system/ai-usage",
            ]);
    }

    [Fact]
    public async Task Me_WithPermissions_ShowsPermittedMenuItems()
    {
        var me = (await factory.CreateClientFor("admin-1", "acme", PlatformPermissions.SystemInfo.Read)
            .GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!;

        me.Permissions.ShouldBe([PlatformPermissions.SystemInfo.Read]);
        var system = me.Menu.Single(m => m.Key == "system");
        system.Children!.Select(c => c.Key).ShouldBe(["system-info"]);
    }

    [Fact]
    public async Task Me_WithUserPermission_ShowsUsersButNotRoles()
    {
        var me = (await factory.CreateClientFor("admin-1", "acme", PlatformPermissions.Users.Read)
            .GetFromJsonAsync<MeResponse>("/api/platform/me", Ct))!;

        var access = me.Menu.Single(m => m.Key == "access");
        access.Children!.Select(c => c.Path).ShouldBe(["/access/users"]);
    }

    private static IEnumerable<string> Leaves(IEnumerable<MenuItemResponse> items) =>
        items.SelectMany(i => i.Children is { Count: > 0 } children ? Leaves(children) : [i.Path!]);
}
