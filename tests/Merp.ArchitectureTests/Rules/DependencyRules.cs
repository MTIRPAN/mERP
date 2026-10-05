using System.Reflection;
using NetArchTest.Rules;

namespace Merp.ArchitectureTests.Rules;

/// <summary>Pure rule functions; they return violations so they can be tested with deliberately bad samples.</summary>
public static class DependencyRules
{
    /// <summary>Infrastructure and framework namespaces the domain must not use (CLAUDE.md rule 4).</summary>
    public static readonly string[] ForbiddenInDomain =
    [
        "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Extensions.DependencyInjection", "Npgsql",
        "Hangfire", "RabbitMQ", "StackExchange.Redis", "Serilog", "OpenTelemetry", "FluentValidation",
        "Merp.Infrastructure", "Merp.Web", "Merp.Application",
    ];

    /// <summary>What the application layer must not use (CLAUDE.md rule 5).</summary>
    public static readonly string[] ForbiddenInApplication =
    [
        "Merp.Infrastructure", "Merp.Web", "Microsoft.AspNetCore", "Npgsql", "Hangfire", "RabbitMQ", "StackExchange.Redis", "Serilog",
    ];

    /// <summary>Types in <paramref name="assembly"/> (optionally one namespace) that depend on a forbidden namespace.</summary>
    public static IReadOnlyList<string> ForbiddenDependencies(Assembly assembly, IEnumerable<string> forbidden, string? inNamespace = null)
    {
        var types = Types.InAssembly(assembly);
        var selection = inNamespace is null ? types.That().ResideInNamespace(assembly.GetName().Name) : types.That().ResideInNamespace(inNamespace);
        var result = selection.ShouldNot().HaveDependencyOnAny([.. forbidden]).GetResult();
        return result.IsSuccessful ? [] : [.. result.FailingTypeNames ?? []];
    }

    /// <summary>
    /// CLAUDE.md rule 1: a module may reference another module only through its <c>Contracts</c> assembly.
    /// Input: assembly name → names of referenced assemblies.
    /// </summary>
    public static IReadOnlyList<string> CrossModuleReferences(IEnumerable<(string Assembly, IEnumerable<string> References)> assemblies) =>
        [.. assemblies
            .Where(a => MerpAssemblies.ModuleName(a.Assembly) is not null)
            .SelectMany(a => a.References
                .Where(r => MerpAssemblies.ModuleName(r) is { } other
                    && other != MerpAssemblies.ModuleName(a.Assembly)
                    && !r.EndsWith(".Contracts", StringComparison.Ordinal))
                .Select(r => $"{a.Assembly} -> {r}"))];

    /// <summary>Module layers referencing their own module's layers against the layering (e.g. Domain → Application).</summary>
    public static IReadOnlyList<string> LayerReferences(IEnumerable<(string Assembly, IEnumerable<string> References)> assemblies)
    {
        var allowed = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Domain"] = [],
            ["Contracts"] = [],
            ["Application"] = ["Domain", "Contracts"],
            ["Infrastructure"] = ["Domain", "Contracts", "Application"],
            ["Api"] = ["Domain", "Contracts", "Application", "Infrastructure"],
        };

        return [.. assemblies
            .Where(a => MerpAssemblies.ModuleName(a.Assembly) is not null)
            .SelectMany(a =>
            {
                var module = MerpAssemblies.ModuleName(a.Assembly)!;
                var layer = a.Assembly[(a.Assembly.LastIndexOf('.') + 1)..];
                return a.References
                    .Where(r => MerpAssemblies.ModuleName(r) == module)
                    .Select(r => r[(r.LastIndexOf('.') + 1)..])
                    .Where(target => !allowed[layer].Contains(target, StringComparer.Ordinal))
                    .Select(target => $"{a.Assembly} -> {module}.{target}");
            })];
    }

    public static IEnumerable<(string Assembly, IEnumerable<string> References)> ReferenceGraph(IEnumerable<Assembly> assemblies) =>
        assemblies.Select(a => (a.GetName().Name!, a.GetReferencedAssemblies().Select(r => r.Name!)));
}
