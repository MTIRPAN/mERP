using Merp.Application.Abstractions.AI;
using Merp.Modules.Platform.Application;
using Merp.Web.Navigation;

namespace Merp.Modules.Platform.Api;

/// <summary>
/// Menu of Platform (PLAN P13): every user's own work first (approvals), then organization and access, then
/// administration split into definitions (what shapes the modules) and system (what keeps them running). Every
/// screen needs the same permission as its route, so a user only sees what they can open.
/// </summary>
internal sealed class PlatformMenu : IMenuContributor
{
    public IEnumerable<MenuItem> GetMenuItems() =>
    [
        new("home", "web.shell.menu.home", Path: "/", Icon: "home", Order: 0),
        new("approvals", "web.shell.menu.approvals", Icon: "approvals", Order: 10, Children:
        [
            new("approval-inbox", "web.shell.menu.approval_inbox", Path: "/approvals", Icon: "approval-inbox"),
            new("delegations", "web.shell.menu.delegations", Path: "/approvals/delegations", Icon: "delegations", Order: 10),
            new("approval-requests", "web.shell.menu.approval_requests", Path: "/system/approval-requests", Icon: "approval-requests", Permission: PlatformPermissions.Approvals.Admin, Order: 20),
        ]),
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
        new("configuration", "web.shell.menu.configuration", Icon: "configuration", Order: 870, Children:
        [
            new("settings", "web.shell.menu.settings", Path: "/system/settings", Icon: "settings", Permission: PlatformPermissions.Settings.Read),
            new("numbering", "web.shell.menu.numbering", Path: "/system/numbering", Icon: "numbering", Permission: PlatformPermissions.Numbering.Read, Order: 10),
            new("custom-fields", "web.shell.menu.custom_fields", Path: "/system/custom-fields", Icon: "custom-fields", Permission: PlatformPermissions.CustomFields.Read, Order: 20),
            new("localization", "web.shell.menu.localization", Path: "/system/localization", Icon: "language", Permission: PlatformPermissions.Localization.Read, Order: 30),
            new("holidays", "web.shell.menu.holidays", Path: "/system/holidays", Icon: "holidays", Permission: PlatformPermissions.Calendars.Read, Order: 40),
            new("work-schedules", "web.shell.menu.work_schedules", Path: "/system/work-schedules", Icon: "work-schedules", Permission: PlatformPermissions.Calendars.Read, Order: 50),
            new("approval-flows", "web.shell.menu.approval_flows", Path: "/system/approval-flows", Icon: "approval-flows", Permission: PlatformPermissions.ApprovalDefinitions.Read, Order: 60),
            new("document-templates", "web.shell.menu.document_templates", Path: "/system/document-templates", Icon: "document-templates", Permission: PlatformPermissions.DocumentTemplates.Read, Order: 70),
            new("notification-templates", "web.shell.menu.notification_templates", Path: "/system/notification-templates", Icon: "notification-templates", Permission: PlatformPermissions.NotificationTemplates.Read, Order: 80),
        ]),
        new("system", "web.shell.menu.system", Icon: "system", Order: 900, Children:
        [
            new("system-info", "web.shell.menu.system_info", Path: "/system/info", Icon: "info", Permission: PlatformPermissions.SystemInfo.Read),
            new("jobs", "web.shell.menu.jobs", Path: "/system/jobs", Icon: "schedule", Permission: PlatformPermissions.Jobs.View, Order: 10),
            new("audit", "web.shell.menu.audit", Path: "/system/audit", Icon: "audit", Permission: PlatformPermissions.Audit.Read, Order: 20),
            new("ai-usage", "web.shell.menu.ai_usage", Path: "/system/ai-usage", Icon: "ai", Permission: PlatformPermissions.AiUsage.Read, Order: 30),
        ]),
    ];
}

internal sealed class PlatformAiFeatureDefinitions : IAiFeatureDefinitionProvider
{
    public IEnumerable<AiFeatureDefinition> GetFeatures() =>
    [
        new(PlatformAiFeatures.TranslationDrafts),
        new(PlatformAiFeatures.ApprovalSummary),
    ];
}
