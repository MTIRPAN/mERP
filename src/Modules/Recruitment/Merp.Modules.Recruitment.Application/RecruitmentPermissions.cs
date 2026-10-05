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
}

/// <summary>Permission catalog of the Recruitment module (docs/domain/yetki-ve-onay.md §1.5).</summary>
public sealed class RecruitmentPermissionDefinitions : IPermissionDefinitionProvider
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(RecruitmentPermissions.Cv.Analyze),
    ];
}
