using Merp.Modules.Platform.Application;
using Merp.Web.Navigation;

namespace Merp.Modules.Platform.Api;

internal sealed class PlatformMenu : IMenuContributor
{
    public IEnumerable<MenuItem> GetMenuItems() =>
    [
        new("home", "web.shell.menu.home", Path: "/", Icon: "home", Order: 0),
        new("organization", "web.shell.menu.organization", Icon: "organization", Order: 800, Children:
        [
            new("companies", "web.shell.menu.companies", Path: "/organization/companies", Icon: "company", Permission: PlatformPermissions.Companies.Read),
            new("branches", "web.shell.menu.branches", Path: "/organization/branches", Icon: "branch", Permission: PlatformPermissions.Branches.Read, Order: 10),
        ]),
        new("access", "web.shell.menu.access", Icon: "access", Order: 850, Children:
        [
            new("users", "web.shell.menu.users", Path: "/access/users", Icon: "user", Permission: PlatformPermissions.Users.Read),
            new("roles", "web.shell.menu.roles", Path: "/access/roles", Icon: "role", Permission: PlatformPermissions.Roles.Read, Order: 10),
        ]),
        new("system", "web.shell.menu.system", Icon: "settings", Order: 900, Children:
        [
            new("system-info", "web.shell.menu.system_info", Path: "/system/info", Icon: "info", Permission: PlatformPermissions.SystemInfo.Read),
            new("jobs", "web.shell.menu.jobs", Path: "/system/jobs", Icon: "schedule", Permission: PlatformPermissions.Jobs.View, Order: 10),
            new("ai-usage", "web.shell.menu.ai_usage", Path: "/system/ai-usage", Icon: "ai", Permission: PlatformPermissions.AiUsage.Read, Order: 20),
        ]),
    ];
}
