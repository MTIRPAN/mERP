using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Application;
using Merp.Web.Navigation;

namespace Merp.Modules.Platform.Api;

internal sealed class PlatformMenu : IMenuContributor
{
    public IEnumerable<MenuItem> GetMenuItems() =>
    [
        new("home", "web.shell.menu.home", Path: "/", Icon: "home", Order: 0),
        new("organization", "web.shell.menu.organization", Icon: "organization", Order: 100, Children:
        [
            new("companies", "web.shell.menu.companies", Path: "/organization/companies", Icon: "company", Permission: PlatformPermissions.Companies.Read),
            new("branches", "web.shell.menu.branches", Path: "/organization/branches", Icon: "branch", Permission: PlatformPermissions.Branches.Read, Order: 10),
        ]),
        new("system", "web.shell.menu.system", Icon: "settings", Order: 900, Children:
        [
            new("system-info", "web.shell.menu.system_info", Path: "/system/info", Icon: "info", Permission: PlatformPermissions.SystemInfo.Read),
            new("jobs", "web.shell.menu.jobs", Path: "/system/jobs", Icon: "schedule", Permission: PlatformPermissions.Jobs.View, Order: 10),
            new("ai-usage", "web.shell.menu.ai_usage", Path: "/system/ai-usage", Icon: "ai", Permission: PlatformPermissions.AiUsage.Read, Order: 20),
        ]),
    ];
}

internal sealed class PlatformPermissionDefinitions : IPermissionDefinitionProvider
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(PlatformPermissions.SystemInfo.Read),
        new(PlatformPermissions.Jobs.View),
        new(PlatformPermissions.AiUsage.Read),
        new(PlatformPermissions.Companies.Read),
        new(PlatformPermissions.Companies.Manage),
        new(PlatformPermissions.Branches.Read),
        new(PlatformPermissions.Branches.Manage),
        new(PlatformPermissions.ReferenceData.Read),
    ];
}
