namespace Merp.Infrastructure.Messaging.Integration;

/// <summary>
/// Moves outbox messages to the inboxes of their subscribers (D8). <c>InProcess</c> (default) writes the inbox rows
/// directly; <c>RabbitMQ</c> publishes to the <c>merp.events</c> exchange and module consumers write them.
/// </summary>
public interface IMessageTransport
{
    /// <summary>Completes only when the message is safely handed over (inbox rows written or broker confirmed).</summary>
    Task SendAsync(MessageEnvelope envelope, IServiceProvider tenantScope, CancellationToken cancellationToken);
}
