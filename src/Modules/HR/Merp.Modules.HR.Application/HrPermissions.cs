using Merp.Application.Abstractions.Security;

namespace Merp.Modules.HR.Application;

/// <summary>Permissions of the HR module: <c>hr.{resource}.{action}</c> (docs/domain/yetki-ve-onay.md §1.5).</summary>
public static class HrPermissions
{
    public const string Module = "hr";

    public static class Org
    {
        /// <summary>See units, positions and jobs in the caller's scope (D37).</summary>
        public const string Read = "hr.org.read";

        /// <summary>Create and change units and positions of the companies in scope; jobs with the Tenant scope (D37).</summary>
        public const string Manage = "hr.org.manage";
    }
}

/// <summary>Permission catalog of the HR module (docs/domain/yetki-ve-onay.md §1.5).</summary>
public sealed class HrPermissionDefinitions : IPermissionDefinitionProvider
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(HrPermissions.Org.Read),
        new(HrPermissions.Org.Manage),
    ];
}

/// <summary>Scope types HR defines and resolves (docs/domain/yetki-ve-onay.md §1.2, D37).</summary>
public sealed class HrScopeTypes : IScopeTypeProvider
{
    public IEnumerable<ScopeTypeDefinition> GetScopeTypes() =>
    [
        new(AccessScopeTypes.OrgUnit, HrPermissions.Module, RequiresId: true, SupportsSubUnits: true, Order: 30),
        new(AccessScopeTypes.ManagedTeam, HrPermissions.Module, RequiresId: false, Order: 40),
    ];
}
