using Merp.Infrastructure.Messaging;
using Merp.Infrastructure.Persistence.Outbox;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Merp.Infrastructure.Persistence;

/// <summary>
/// Unit of work over one module DbContext: one database transaction per command, domain events handled before
/// commit, outbox messages saved with the business data (D8).
/// </summary>
public sealed class ModuleUnitOfWork<TContext>(
    TContext dbContext,
    DomainEventDispatcher domainEventDispatcher,
    IOptions<PersistenceOptions> options) : IUnitOfWork, IOutboxWriter
    where TContext : ModuleDbContext
{
    private IDbContextTransaction? _transaction;
    private bool _manyAggregates;

    public TContext DbContext => dbContext;

    public async Task BeginAsync(CancellationToken cancellationToken)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException($"A transaction is already open on {typeof(TContext).Name}.");
        }

        _transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        _manyAggregates = false;
    }

    public void AllowManyAggregates() => _manyAggregates = true;

    public void AddOutboxMessage(OutboxMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        dbContext.OutboxMessages.Add(message);
    }

    public async Task<Result> CommitAsync(CancellationToken cancellationToken)
    {
        var transaction = _transaction ?? throw new InvalidOperationException("CommitAsync was called without BeginAsync.");

        dbContext.ChangeTracker.DetectChanges();
        if (options.Value.EnforceSingleAggregatePerCommand && !_manyAggregates)
        {
            EnsureSingleAggregateChanged();
        }

        await DispatchDomainEventsAsync(cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(CommonErrors.ConcurrencyConflict);
        }

        await transaction.CommitAsync(cancellationToken);
        await DisposeTransactionAsync();
        if (_manyAggregates)
        {
            // The next batch of an import runs in the same scope: entities kept tracked would slow every later save
            dbContext.ChangeTracker.Clear();
        }

        return Result.Success();
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        if (_transaction is not null)
        {
            await _transaction.RollbackAsync(cancellationToken);
            await DisposeTransactionAsync();
        }

        dbContext.ChangeTracker.Clear();
    }

    private async Task DispatchDomainEventsAsync(CancellationToken cancellationToken)
    {
        for (var round = 0; ; round++)
        {
            var aggregates = dbContext.ChangeTracker.Entries<IAggregateRoot>()
                .Select(entry => entry.Entity)
                .Where(aggregate => aggregate.DomainEvents.Count > 0)
                .ToList();

            if (aggregates.Count == 0)
            {
                return;
            }

            if (round >= options.Value.MaxDomainEventRounds)
            {
                throw new InvalidOperationException(
                    $"Domain events were still being raised after {options.Value.MaxDomainEventRounds} rounds; check for handlers that raise events in a loop.");
            }

            // Equal events of a round (records with the same values) are handled once: an import batch that adds many
            // positions of a company rebuilds its reporting lines once, not per position (D40)
            var events = aggregates.SelectMany(aggregate => aggregate.DomainEvents).Distinct().ToList();
            aggregates.ForEach(aggregate => aggregate.ClearDomainEvents());

            foreach (var domainEvent in events)
            {
                await domainEventDispatcher.DispatchAsync(domainEvent, cancellationToken);
            }

            dbContext.ChangeTracker.DetectChanges();
        }
    }

    // CLAUDE.md rule 7: a command changes one aggregate. Other aggregates of the module are updated by domain event
    // handlers, which run after this check.
    private void EnsureSingleAggregateChanged()
    {
        var changed = dbContext.ChangeTracker.Entries<IAggregateRoot>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(entry => $"{entry.Metadata.ClrType.Name}")
            .ToList();

        if (changed.Count > 1)
        {
            throw new InvalidOperationException(
                $"A command changed {changed.Count} aggregates ({string.Join(", ", changed)}). "
                + "Change one aggregate per command and update the others with domain events (CLAUDE.md rule 7).");
        }
    }

    private async Task DisposeTransactionAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }
}
