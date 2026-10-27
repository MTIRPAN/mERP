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

    public static class Requisitions
    {
        /// <summary>See the requisitions of the companies in scope (D48).</summary>
        public const string Read = "recruitment.requisitions.read";

        /// <summary>Open, correct, send and cancel them, and see their salary range.</summary>
        public const string Manage = "recruitment.requisitions.manage";
    }

    public static class Openings
    {
        /// <summary>See the job openings of the companies in scope (D49).</summary>
        public const string Read = "recruitment.openings.read";

        /// <summary>Open, correct, publish, hold and close them.</summary>
        public const string Manage = "recruitment.openings.manage";
    }

    public static class Self
    {
        /// <summary>An employee applies to openings from self service (D49); the Employee role has it.</summary>
        public const string Apply = "recruitment.self.apply";
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
        new(RecruitmentPermissions.Requisitions.Read),
        new(RecruitmentPermissions.Requisitions.Manage),
        new(RecruitmentPermissions.Openings.Read),
        new(RecruitmentPermissions.Openings.Manage),
        new(RecruitmentPermissions.Self.Apply),
    ];
}
