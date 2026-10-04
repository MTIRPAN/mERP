using System.Reflection;
using System.Text.RegularExpressions;

namespace Merp.ArchitectureTests.Rules;

/// <summary>The production assemblies of mERP, discovered from the API host's references.</summary>
public static partial class MerpAssemblies
{
    public static readonly IReadOnlyList<Assembly> All = Discover();

    public static IEnumerable<Assembly> ModuleLayer(string layer) =>
        All.Where(a => ModuleName(a) is not null && a.GetName().Name!.EndsWith("." + layer, StringComparison.Ordinal));

    public static IEnumerable<string> Modules => All.Select(ModuleName).OfType<string>().Distinct(StringComparer.Ordinal);

    /// <summary><c>HR</c> for <c>Merp.Modules.HR.Domain</c>; null for building blocks and hosts.</summary>
    public static string? ModuleName(Assembly assembly) => ModuleName(assembly.GetName().Name!);

    public static string? ModuleName(string assemblyName) =>
        ModulePattern().Match(assemblyName) is { Success: true } match ? match.Groups["module"].Value : null;

    public static Assembly Get(string name) => All.Single(a => a.GetName().Name == name);

    /// <summary>
    /// Production assemblies copied next to the tests. Scanning files instead of references also finds assemblies
    /// the compiler dropped from metadata because nothing uses them yet (e.g. an empty Domain project).
    /// </summary>
    private static List<Assembly> Discover() =>
        [.. Directory.GetFiles(AppContext.BaseDirectory, "Merp.*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Where(name => !name.EndsWith("Tests", StringComparison.Ordinal) && name != "Merp.Testing")
            .Select(name => Assembly.Load(new AssemblyName(name)))
            .OrderBy(a => a.GetName().Name, StringComparer.Ordinal)];

    [GeneratedRegex(@"^Merp\.Modules\.(?<module>[A-Za-z]+)\.(Domain|Application|Infrastructure|Api|Contracts)$", RegexOptions.CultureInvariant)]
    private static partial Regex ModulePattern();
}
