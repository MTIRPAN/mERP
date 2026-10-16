using System.Reflection;

namespace Merp.Modules.HR.Application;

/// <summary>Marker for the HR application assembly (handler registration).</summary>
public static class AssemblyReference
{
    public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}
