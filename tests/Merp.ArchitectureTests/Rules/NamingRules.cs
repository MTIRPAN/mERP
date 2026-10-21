namespace Merp.ArchitectureTests.Rules;

/// <summary>
/// The API documents request and response types by their simple name: two modules exposing a type of the same name
/// merge into one OpenAPI schema and the web client sends the wrong fields to one of them.
/// </summary>
public static class NamingRules
{
    /// <summary>Simple names that public types of more than one module share, with the types that share them.</summary>
    public static IReadOnlyList<string> SharedNames(IEnumerable<(string Module, Type Type)> types) =>
        [.. types
            .Where(t => CanBeSchema(t.Type))
            .GroupBy(t => t.Type.Name, StringComparer.Ordinal)
            .Where(g => g.Select(t => t.Module).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(t => t.Type.FullName).Order(StringComparer.Ordinal))}")
            .Order(StringComparer.Ordinal)];

    // Interfaces and static classes are never request or response bodies; every module has an assembly marker
    private static bool CanBeSchema(Type type) =>
        !type.IsInterface && !(type.IsAbstract && type.IsSealed) && type.Name != "AssemblyReference";
}
