using System.Reflection;

namespace Merp.Modules.Platform.Application;

/// <summary>Marker for the Platform application assembly (handler registration, unit of work mapping).</summary>
public static class AssemblyReference
{
    public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}
