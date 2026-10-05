using System.Reflection;

namespace Merp.Modules.Recruitment.Application;

/// <summary>Marker for the Recruitment application assembly (handler registration).</summary>
public static class AssemblyReference
{
    public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}
