using System.Net;
using System.Net.Http.Json;
using Merp.Modules.Platform.Api.Endpoints;
using Merp.Modules.Platform.Application;

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
    public async Task Bundle_ForInactiveCulture_Returns404() =>
        (await factory.CreateClient().GetAsync("/api/platform/localization/bundles/en-US", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

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
        me.Menu.Select(m => m.Key).ShouldBe(["home"]);
        me.Preferences.Culture.ShouldBe("tr-TR");
        me.Cultures.Select(c => c.Code).ShouldBe(["tr-TR"]);
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
}
