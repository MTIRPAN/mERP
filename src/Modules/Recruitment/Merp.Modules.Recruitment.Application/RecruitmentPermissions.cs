using Merp.Application.Abstractions.Security;

namespace Merp.Modules.Recruitment.Application;

/// <summary>Permissions of the Recruitment module: <c>recruitment.{resource}.{action}</c> (docs/domain/yetki-ve-onay.md §1.5).</summary>
public static class RecruitmentPermissions
{
    public const string Module = "recruitment";

    public static class Cv
    {
        /// <summary>Analyze a CV with AI (D24).</summary>
        public const string Analyze = "recruitment.cv.analyze";
    }

    public static class Settings
    {
        /// <summary>See the stage templates, the reasons, sources and tags, and the KVKK texts (D47).</summary>
        public const string Read = "recruitment.settings.read";

        /// <summary>Change them; the tenant's catalogs and the texts of every company need the Tenant scope.</summary>
        public const string Manage = "recruitment.settings.manage";
    }
}

/// <summary>Permission catalog of the Recruitment module (docs/domain/yetki-ve-onay.md §1.5).</summary>
public sealed class RecruitmentPermissionDefinitions : IPermissionDefinitionProvider
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(RecruitmentPermissions.Cv.Analyze),
        new(RecruitmentPermissions.Settings.Read),
        new(RecruitmentPermissions.Settings.Manage),
    ];
}
