using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Merp.ArchitectureTests.Rules;

/// <summary>D10: time comes from <see cref="TimeProvider"/>; reading the system clock directly is forbidden.</summary>
public static class ClockRules
{
    private static readonly HashSet<string> ForbiddenGetters = new(StringComparer.Ordinal)
    {
        "System.DateTime System.DateTime::get_Now()",
        "System.DateTime System.DateTime::get_UtcNow()",
        "System.DateTime System.DateTime::get_Today()",
        "System.DateTimeOffset System.DateTimeOffset::get_Now()",
        "System.DateTimeOffset System.DateTimeOffset::get_UtcNow()",
    };

    /// <summary>Methods in <paramref name="assembly"/> (optionally one namespace) that read the system clock.</summary>
    public static IReadOnlyList<string> SystemClockUsages(Assembly assembly, string? inNamespace = null)
    {
        using var module = ModuleDefinition.ReadModule(assembly.Location);
        return [.. module.GetTypes()
            .Where(t => inNamespace is null || t.Namespace == inNamespace || (t.DeclaringType?.Namespace ?? string.Empty) == inNamespace)
            .SelectMany(t => t.Methods)
            .Where(m => m.HasBody && m.Body.Instructions.Any(i =>
                (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                && i.Operand is MethodReference called
                && ForbiddenGetters.Contains(called.FullName)))
            .Select(m => $"{m.DeclaringType.FullName}::{m.Name}")];
    }
}
