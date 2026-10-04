using System.Reflection;

namespace Merp.ArchitectureTests.Rules;

/// <summary>
/// CLAUDE.md rule 3: modules write to each other only through integration events; synchronous calls go through
/// read-only query interfaces in <c>*.Contracts</c>.
/// </summary>
public static class ContractsRules
{
    public static readonly string[] ReadOnlyMethodPrefixes =
    [
        "Get", "Find", "List", "Search", "Count", "Exists", "Is", "Has", "Can", "Preview", "Resolve", "Calculate",
    ];

    /// <summary>Methods of public interfaces that look like writes or are not asynchronous queries.</summary>
    public static IReadOnlyList<string> NonQueryInterfaceMethods(IEnumerable<Type> types) =>
        [.. types
            .Where(t => t is { IsInterface: true, IsPublic: true })
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => !m.IsSpecialName)
                .Where(m => !ReadOnlyMethodPrefixes.Any(p => m.Name.StartsWith(p, StringComparison.Ordinal)) || !IsAsync(m.ReturnType))
                .Select(m => $"{t.FullName}.{m.Name}"))];

    private static bool IsAsync(Type type) =>
        type == typeof(Task) || type == typeof(ValueTask)
        || (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ValueTask<>)));
}
