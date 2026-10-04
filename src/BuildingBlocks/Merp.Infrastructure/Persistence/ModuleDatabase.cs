namespace Merp.Infrastructure.Persistence;

/// <summary>Describes a module DbContext so that migrations can run for every module of a tenant database.</summary>
public sealed record ModuleDatabase(Type DbContextType, string Schema);
