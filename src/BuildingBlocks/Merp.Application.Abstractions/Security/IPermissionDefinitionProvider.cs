namespace Merp.Application.Abstractions.Security;

/// <summary>
/// A module's permission catalog (docs/domain/yetki-ve-onay.md §1.5). Labels are localization keys such as
/// <c>permissions.hr.employees.read</c>. PLAN P2 synchronizes the definitions to the database.
/// </summary>
public interface IPermissionDefinitionProvider
{
    IEnumerable<PermissionDefinition> GetPermissions();
}

public sealed record PermissionDefinition(string Code)
{
    public string LabelKey => "permissions." + Code;
}
