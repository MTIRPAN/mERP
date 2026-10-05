namespace Merp.Application.Abstractions.Messaging;

/// <summary>
/// Handles an integration event from another module. Delivery is at-least-once; the inbox guarantees that each
/// handler processes a message once (D8).
/// </summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IntegrationEvent
{
    Task Handle(TEvent integrationEvent, CancellationToken cancellationToken);
}
