using Merp.SharedKernel.Domain;

namespace Merp.Application.Abstractions.Messaging;

/// <summary>
/// Handles a domain event in the same module and transaction, before the unit of work commits.
/// Use it to update other aggregates of the module (CLAUDE.md rule 7).
/// </summary>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task Handle(TEvent domainEvent, CancellationToken cancellationToken);
}
