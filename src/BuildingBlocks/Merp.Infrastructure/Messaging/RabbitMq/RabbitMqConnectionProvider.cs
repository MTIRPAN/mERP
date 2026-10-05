using Merp.Infrastructure.Messaging.Integration;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Merp.Infrastructure.Messaging.RabbitMq;

/// <summary>One shared, automatically recovering connection to RabbitMQ per process.</summary>
public sealed class RabbitMqConnectionProvider(IOptions<MessagingOptions> options) : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;

    public RabbitMqOptions Options => options.Value.RabbitMq;

    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            var factory = new ConnectionFactory
            {
                Uri = Options.Uri,
                ClientProvidedName = "merp",
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
            };
            _connection = await factory.CreateConnectionAsync(cancellationToken);
            return _connection;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Declares the topic exchange all integration events are published to.</summary>
    public Task DeclareExchangeAsync(IChannel channel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return channel.ExchangeDeclareAsync(Options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _lock.Dispose();
    }
}
