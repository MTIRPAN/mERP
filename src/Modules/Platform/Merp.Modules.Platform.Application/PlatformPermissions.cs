namespace Merp.Modules.Platform.Application;

/// <summary>Permissions of the Platform module: <c>platform.{resource}.{action}</c> (docs/domain/yetki-ve-onay.md §1.5).</summary>
public static class PlatformPermissions
{
    public const string Module = "platform";

    public static class SystemInfo
    {
        public const string Read = "platform.system.read";
    }

    public static class Jobs
    {
        public const string View = "platform.jobs.view";
    }

    public static class AiUsage
    {
        public const string Read = "platform.ai_usage.read";
    }

    public static class Companies
    {
        public const string Read = "platform.companies.read";
        public const string Manage = "platform.companies.manage";
    }

    public static class Branches
    {
        public const string Read = "platform.branches.read";
        public const string Manage = "platform.branches.manage";
    }

    public static class Users
    {
        public const string Read = "platform.users.read";
        public const string Manage = "platform.users.manage";
    }

    public static class Roles
    {
        public const string Read = "platform.roles.read";
        public const string Manage = "platform.roles.manage";
    }

    public static class Audit
    {
        public const string Read = "platform.audit.read";
    }

    public static class Localization
    {
        public const string Read = "platform.localization.read";
        public const string Manage = "platform.localization.manage";
    }

    public static class Numbering
    {
        public const string Read = "platform.numbering.read";
        public const string Manage = "platform.numbering.manage";
    }

    public static class CustomFields
    {
        public const string Read = "platform.custom_fields.read";
        public const string Manage = "platform.custom_fields.manage";
    }

    public static class NotificationTemplates
    {
        public const string Read = "platform.notification_templates.read";
        public const string Manage = "platform.notification_templates.manage";
    }

    public static class Calendars
    {
        public const string Read = "platform.calendars.read";
        public const string Manage = "platform.calendars.manage";
    }

    public static class ApprovalDefinitions
    {
        public const string Read = "platform.approvals.definitions.read";
        public const string Manage = "platform.approvals.definitions.manage";
    }

    public static class Approvals
    {
        /// <summary>Reassign tasks, approve in place of approvers, handle requests that need attention (§2.6).</summary>
        public const string Admin = "platform.approvals.admin";
    }

    public static class Settings
    {
        public const string Read = "platform.settings.read";
        public const string Manage = "platform.settings.manage";
    }
}
