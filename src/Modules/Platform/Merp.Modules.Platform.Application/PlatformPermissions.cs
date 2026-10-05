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
}
