namespace Merp.Application.Abstractions.Messaging;

/// <summary>
/// Writes integration events to the outbox of the current unit of work. They are dispatched only after the
/// transaction commits; a rollback discards them (D8).
/// </summary>
public interface IIntegrationEventPublisher
{
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IntegrationEvent;
}
