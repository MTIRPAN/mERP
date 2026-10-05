using System.Globalization;
using System.Text;
using Merp.Infrastructure.Messaging.Integration;
using RabbitMQ.Client;

namespace Merp.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// Publishes integration events to the <c>merp.events</c> topic exchange with the event type as routing key and
/// waits for the publisher confirm (D8). Module consumers write the inbox rows.
/// </summary>
public sealed class RabbitMqTransport(RabbitMqConnectionProvider connectionProvider) : IMessageTransport, IAsyncDisposable
{
    public const string TenantHeader = "merp-tenant-id";
    public const string TraceParentHeader = "traceparent";
    public const string OccurredAtHeader = "merp-occurred-at";

    private readonly SemaphoreSlim _lock = new(1, 1);
    private IChannel? _channel;

    public async Task SendAsync(MessageEnvelope envelope, IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var properties = new BasicProperties
        {
            MessageId = envelope.MessageId.ToString(),
            Type = envelope.Type,
            ContentType = "application/json",
            ContentEncoding = "utf-8",
            DeliveryMode = DeliveryModes.Persistent,
            Headers = new Dictionary<string, object?>
            {
                [TenantHeader] = envelope.TenantId.ToString(),
                [OccurredAtHeader] = envelope.OccurredAt.ToString("O", CultureInfo.InvariantCulture),
                [TraceParentHeader] = envelope.TraceParent,
            },
        };

        // Channels are not safe for concurrent publishing; confirms make each publish wait for the broker.
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var channel = await GetChannelAsync(cancellationToken);
            await channel.BasicPublishAsync(
                connectionProvider.Options.Exchange,
                envelope.Type,
                mandatory: false,
                properties,
                Encoding.UTF8.GetBytes(envelope.Payload),
                cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        _lock.Dispose();
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        var connection = await connectionProvider.GetConnectionAsync(cancellationToken);
        _channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        await connectionProvider.DeclareExchangeAsync(_channel, cancellationToken);
        return _channel;
    }
}
