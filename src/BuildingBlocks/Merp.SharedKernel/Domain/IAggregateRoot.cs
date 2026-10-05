namespace Merp.SharedKernel.Domain;

/// <summary>Non-generic view of an aggregate root, used by infrastructure to collect domain events.</summary>
public interface IAggregateRoot
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}
