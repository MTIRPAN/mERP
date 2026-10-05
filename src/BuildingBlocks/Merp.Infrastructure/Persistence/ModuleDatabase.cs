using Merp.Infrastructure.Messaging;

namespace Merp.Infrastructure.Persistence;

/// <summary>
/// A module DbContext and the types it owns. Used to run migrations per tenant and to route integration events to
/// the inbox of the module that handles them.
/// </summary>
public sealed record ModuleDatabase(Type DbContextType, string Schema, ModuleScope Scope)
{
    /// <summary>Module name used for RabbitMQ queues (<c>merp.{module}</c>, D8).</summary>
    public string ModuleName => Schema;

    public Type UnitOfWorkType => typeof(ModuleUnitOfWork<>).MakeGenericType(DbContextType);
}
