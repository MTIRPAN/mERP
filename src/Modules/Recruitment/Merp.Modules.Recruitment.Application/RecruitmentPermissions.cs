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

    public static class Applications
    {
        /// <summary>See the applications to the openings of the companies in scope and their pipelines (D50).</summary>
        public const string Read = "recruitment.applications.read";

        /// <summary>Move them through the pipeline, reject them, record withdrawals and reopen them.</summary>
        public const string Manage = "recruitment.applications.manage";
    }

    public static class Candidates
    {
        /// <summary>See the candidate pool and the cards (D50); the pool is the tenant's, the applications on a card follow the scope.</summary>
        public const string Read = "recruitment.candidates.read";

        /// <summary>Add and correct candidates, their tags and CVs, write notes and merge duplicates.</summary>
        public const string Manage = "recruitment.candidates.manage";
    }

    public static class Self
    {
        /// <summary>An employee applies to openings from self service (D49); the Employee role has it.</summary>
        public const string Apply = "recruitment.self.apply";

        /// <summary>
        /// The hiring manager and the recruiters of an opening see its applications without other permissions (D50);
        /// the Employee role has it.
        /// </summary>
        public const string Hiring = "recruitment.self.hiring";
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
        new(RecruitmentPermissions.Applications.Read),
        new(RecruitmentPermissions.Applications.Manage),
        new(RecruitmentPermissions.Candidates.Read),
        new(RecruitmentPermissions.Candidates.Manage),
        new(RecruitmentPermissions.Self.Apply),
        new(RecruitmentPermissions.Self.Hiring),
    ];
}
