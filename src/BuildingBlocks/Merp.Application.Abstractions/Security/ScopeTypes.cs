namespace Merp.Application.Abstractions.Security;

/// <summary>
/// A data scope type a role assignment can use (docs/domain/yetki-ve-onay.md §1.2). Modules register their types
/// through <see cref="IScopeTypeProvider"/>; Platform stores scopes opaquely as type code plus id.
/// </summary>
/// <param name="Code">Type code, e.g. <c>Company</c>; see <see cref="AccessScopeTypes"/>.</param>
/// <param name="Module">Module that defines and resolves the type.</param>
/// <param name="RequiresId">The scope names one record (a company, a unit) instead of a relation (Tenant, Self).</param>
/// <param name="SupportsSubUnits">The scope can include the sub units of its record (org units).</param>
/// <param name="Order">Position in the scope picker.</param>
public sealed record ScopeTypeDefinition(string Code, string Module, bool RequiresId, bool SupportsSubUnits = false, int Order = 0)
{
    public string LabelKey => "scopes." + Code;
}

/// <summary>A module's scope types.</summary>
public interface IScopeTypeProvider
{
    IEnumerable<ScopeTypeDefinition> GetScopeTypes();
}

/// <summary>All registered scope types.</summary>
public interface IScopeTypeRegistry
{
    IReadOnlyList<ScopeTypeDefinition> All { get; }

    ScopeTypeDefinition? Find(string code);
}

/// <summary>Collects the scope types of all modules.</summary>
public sealed class ScopeTypeRegistry(IEnumerable<IScopeTypeProvider> providers) : IScopeTypeRegistry
{
    private readonly Dictionary<string, ScopeTypeDefinition> _types = providers
        .SelectMany(p => p.GetScopeTypes())
        .ToDictionary(t => t.Code, StringComparer.Ordinal);

    public IReadOnlyList<ScopeTypeDefinition> All => [.. _types.Values.OrderBy(t => t.Order).ThenBy(t => t.Code, StringComparer.Ordinal)];

    public ScopeTypeDefinition? Find(string code) => _types.GetValueOrDefault(code);
}
