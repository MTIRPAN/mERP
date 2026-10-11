using Merp.Application.Abstractions.Security;

namespace Merp.Modules.Platform.Application.Access;

/// <summary>Permission catalog of the Platform module (docs/domain/yetki-ve-onay.md §1.5).</summary>
public sealed class PlatformPermissionDefinitions : IPermissionDefinitionProvider
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
        new(PlatformPermissions.Users.Read),
        new(PlatformPermissions.Users.Manage),
        new(PlatformPermissions.Roles.Read),
        new(PlatformPermissions.Roles.Manage),
        new(PlatformPermissions.Audit.Read),
        new(PlatformPermissions.Localization.Read),
        new(PlatformPermissions.Localization.Manage),
        new(PlatformPermissions.Numbering.Read),
        new(PlatformPermissions.Numbering.Manage),
        new(PlatformPermissions.CustomFields.Read),
        new(PlatformPermissions.CustomFields.Manage),
        new(PlatformPermissions.NotificationTemplates.Read),
        new(PlatformPermissions.NotificationTemplates.Manage),
        new(PlatformPermissions.Calendars.Read),
        new(PlatformPermissions.Calendars.Manage),
        new(PlatformPermissions.ApprovalDefinitions.Read),
        new(PlatformPermissions.ApprovalDefinitions.Manage),
        new(PlatformPermissions.Approvals.Admin),
    ];
}

/// <summary>Scope types Platform defines; HR adds OrgUnit and ManagedTeam (docs/domain/yetki-ve-onay.md §1.2).</summary>
public sealed class PlatformScopeTypes : IScopeTypeProvider
{
    public IEnumerable<ScopeTypeDefinition> GetScopeTypes() =>
    [
        new(AccessScopeTypes.Tenant, PlatformPermissions.Module, RequiresId: false, Order: 0),
        new(AccessScopeTypes.Company, PlatformPermissions.Module, RequiresId: true, Order: 10),
        new(AccessScopeTypes.Branch, PlatformPermissions.Module, RequiresId: true, Order: 20),
        new(AccessScopeTypes.Self, PlatformPermissions.Module, RequiresId: false, Order: 90),
    ];
}
