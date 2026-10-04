namespace Merp.Infrastructure.Messaging.Integration;

/// <summary>A registered <c>IIntegrationEventHandler&lt;TEvent&gt;</c>.</summary>
public sealed record IntegrationEventHandlerDescriptor(Type HandlerType, Type EventType)
{
    /// <summary>Name stored in the inbox to identify the handler.</summary>
    public string HandlerName => HandlerType.FullName ?? HandlerType.Name;

    /// <summary>Name stored in the outbox and used as the RabbitMQ routing key.</summary>
    public string EventTypeName => EventType.FullName ?? EventType.Name;
}
