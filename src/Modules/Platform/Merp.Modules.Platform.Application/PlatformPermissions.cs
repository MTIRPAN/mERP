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

    /// <summary>Provinces, districts and other shared lists used by forms; granted to every role.</summary>
    public static class ReferenceData
    {
        public const string Read = "platform.reference_data.read";
    }
}
