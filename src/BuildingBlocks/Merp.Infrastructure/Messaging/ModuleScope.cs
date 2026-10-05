using System.Reflection;

namespace Merp.Infrastructure.Messaging;

/// <summary>
/// Identifies the types that belong to a module: the module's application assembly, optionally narrowed to a
/// namespace (used by tests that host several miniature modules in one assembly).
/// </summary>
public sealed record ModuleScope(Assembly Assembly, string? NamespacePrefix = null)
{
    public bool Contains(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.Assembly == Assembly
            && (NamespacePrefix is null || (type.Namespace is { } ns && (ns == NamespacePrefix || ns.StartsWith(NamespacePrefix + ".", StringComparison.Ordinal))));
    }

    /// <summary>More specific scopes (with a namespace) win over assembly-wide ones.</summary>
    public int Specificity => NamespacePrefix?.Length ?? 0;

    public static T? FindBest<T>(IEnumerable<T> candidates, Func<T, ModuleScope> scope, Type type)
        where T : class =>
        candidates.Where(c => scope(c).Contains(type)).MaxBy(c => scope(c).Specificity);
}
