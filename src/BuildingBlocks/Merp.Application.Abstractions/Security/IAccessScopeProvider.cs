namespace Merp.Application.Abstractions.Security;

/// <summary>
/// Returns the data scopes in which a user holds a permission. Modules translate the scopes into query
/// predicates (docs/domain/yetki-ve-onay.md §1.3).
/// </summary>
public interface IAccessScopeProvider
{
    Task<IReadOnlyList<AccessScope>> GetScopesAsync(Guid userId, string permission, CancellationToken cancellationToken = default);
}

/// <summary>A data scope: type code plus optional id. Platform stores scopes opaquely.</summary>
public sealed record AccessScope(string Type, Guid? ScopeId = null, bool IncludeSubUnits = false);

/// <summary>Built-in scope type codes. Modules may register more through the scope type registry.</summary>
public static class AccessScopeTypes
{
    public const string Tenant = "Tenant";
    public const string Company = "Company";
    public const string Branch = "Branch";
    public const string OrgUnit = "OrgUnit";
    public const string ManagedTeam = "ManagedTeam";
    public const string Self = "Self";
}
