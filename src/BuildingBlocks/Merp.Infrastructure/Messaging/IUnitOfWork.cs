namespace Merp.Infrastructure.Messaging;

/// <summary>
/// Transaction boundary of one module. Implemented by the module's DbContext; the unit of work behavior opens it
/// around every command of that module.
/// </summary>
public interface IUnitOfWork
{
    Task BeginAsync(CancellationToken cancellationToken);

    /// <summary>Dispatches domain events, saves changes (including outbox messages) and commits.</summary>
    Task CommitAsync(CancellationToken cancellationToken);

    /// <summary>Rolls back the transaction and discards tracked changes and pending outbox messages.</summary>
    Task RollbackAsync(CancellationToken cancellationToken);
}
