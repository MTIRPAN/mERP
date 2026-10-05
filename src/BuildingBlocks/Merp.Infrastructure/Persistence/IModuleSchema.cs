namespace Merp.Infrastructure.Persistence;

/// <summary>Declares the PostgreSQL schema of a module DbContext (CLAUDE.md rule 2).</summary>
public interface IModuleSchema
{
    static abstract string SchemaName { get; }
}
