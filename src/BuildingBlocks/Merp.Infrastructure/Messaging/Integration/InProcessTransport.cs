namespace Merp.Infrastructure.Messaging.Integration;

/// <summary>Default transport for the monolith: writes the subscribers' inbox rows directly (D8).</summary>
public sealed class InProcessTransport(InboxWriter inboxWriter) : IMessageTransport
{
    public Task SendAsync(MessageEnvelope envelope, IServiceProvider tenantScope, CancellationToken cancellationToken) =>
        inboxWriter.WriteAsync(envelope, tenantScope, moduleName: null, cancellationToken);
}
