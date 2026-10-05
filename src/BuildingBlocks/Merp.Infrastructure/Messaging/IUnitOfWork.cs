using Merp.SharedKernel.Results;

namespace Merp.Infrastructure.Messaging;

/// <summary>
/// Transaction boundary of one module. The unit of work behavior opens it around every command of that module.
/// </summary>
public interface IUnitOfWork
{
    Task BeginAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Dispatches domain events, saves changes (including outbox messages) and commits. Returns a conflict error
    /// when an optimistic concurrency check fails.
    /// </summary>
    Task<Result> CommitAsync(CancellationToken cancellationToken);

    /// <summary>Rolls back the transaction and discards tracked changes and pending outbox messages.</summary>
    Task RollbackAsync(CancellationToken cancellationToken);
}

/// <summary>Unit of work that can store integration events in its outbox.</summary>
public interface IOutboxWriter
{
    void AddOutboxMessage(Persistence.Outbox.OutboxMessage message);
}
