namespace Merp.Application.Abstractions.Messaging;

/// <summary>
/// Event published to other modules through the outbox (D8). Define events as records in the module's
/// <c>*.Contracts</c> project. Metadata is stamped by <see cref="IIntegrationEventPublisher"/>.
/// </summary>
public abstract record IntegrationEvent
{
    public Guid EventId { get; init; } = Guid.CreateVersion7();

    public DateTimeOffset OccurredAt { get; init; }

    public Guid TenantId { get; init; }

    public Guid? CompanyId { get; init; }
}
