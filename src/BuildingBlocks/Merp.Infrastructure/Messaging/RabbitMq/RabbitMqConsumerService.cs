using System.Globalization;
using System.Text;
using Merp.Infrastructure.Messaging.Integration;
using Merp.Infrastructure.MultiTenancy;
using Merp.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Merp.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// One queue per module (<c>merp.{module}</c>) bound to the event types the module handles. A delivery is written
/// to the module's inbox in the tenant database, then acknowledged; the inbox processor runs the handlers (D8).
/// </summary>
public sealed partial class RabbitMqConsumerService(
    RabbitMqConnectionProvider connectionProvider,
    InboxWriter inboxWriter,
    ITenantStore tenantStore,
    TenantScopeFactory scopeFactory,
    IEnumerable<ModuleDatabase> modules,
    TimeProvider timeProvider,
    ILogger<RabbitMqConsumerService> logger) : BackgroundService
{
    private readonly List<IChannel> _channels = [];

    /// <summary>Completes when every module queue is declared, bound and consumed.</summary>
    public Task Ready => _ready.Task;

    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connection = await connectionProvider.GetConnectionAsync(stoppingToken);
        foreach (var module in modules)
        {
            var eventTypes = inboxWriter.EventTypesHandledBy(module);
            if (eventTypes.Count == 0)
            {
                continue;
            }

            var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
            _channels.Add(channel);
            await connectionProvider.DeclareExchangeAsync(channel, stoppingToken);
            var queue = connectionProvider.Options.QueuePrefix + module.ModuleName;
            await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
            foreach (var eventType in eventTypes)
            {
                await channel.QueueBindAsync(queue, connectionProvider.Options.Exchange, eventType, cancellationToken: stoppingToken);
            }

            await channel.BasicQosAsync(0, connectionProvider.Options.PrefetchCount, global: false, stoppingToken);
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += (_, delivery) => OnReceivedAsync(channel, module, delivery, stoppingToken);
            await channel.BasicConsumeAsync(queue, autoAck: false, consumer, stoppingToken);
        }

        _ready.TrySetResult();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        foreach (var channel in _channels)
        {
            await channel.CloseAsync(cancellationToken);
            await channel.DisposeAsync();
        }
    }

    private async Task OnReceivedAsync(IChannel channel, ModuleDatabase module, BasicDeliverEventArgs delivery, CancellationToken cancellationToken)
    {
        try
        {
            var envelope = ReadEnvelope(delivery);
            var tenant = await tenantStore.FindByIdAsync(envelope.TenantId, cancellationToken)
                ?? throw new InvalidOperationException($"Tenant {envelope.TenantId} of message {envelope.MessageId} does not exist.");

            await using var scope = scopeFactory.CreateScope(tenant);
            await inboxWriter.WriteAsync(envelope, scope.ServiceProvider, module.ModuleName, cancellationToken);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogDeliveryFailed(logger, exception, delivery.BasicProperties.MessageId, module.ModuleName);
            // Back off briefly so a database outage does not turn into a redelivery storm.
            await Task.Delay(TimeSpan.FromSeconds(1), timeProvider, cancellationToken);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, cancellationToken);
        }
    }

    private static MessageEnvelope ReadEnvelope(BasicDeliverEventArgs delivery)
    {
        var properties = delivery.BasicProperties;
        var headers = properties.Headers ?? new Dictionary<string, object?>();

        return new MessageEnvelope(
            Guid.Parse(properties.MessageId ?? throw new InvalidOperationException("Message id is missing.")),
            properties.Type ?? throw new InvalidOperationException("Message type is missing."),
            Encoding.UTF8.GetString(delivery.Body.Span),
            Guid.Parse(Header(headers, RabbitMqTransport.TenantHeader) ?? throw new InvalidOperationException("Tenant header is missing.")),
            DateTimeOffset.Parse(Header(headers, RabbitMqTransport.OccurredAtHeader) ?? throw new InvalidOperationException("Occurred-at header is missing."), CultureInfo.InvariantCulture),
            Header(headers, RabbitMqTransport.TraceParentHeader));
    }

    // Header values arrive as byte arrays (AMQP long strings).
    private static string? Header(IDictionary<string, object?> headers, string name) =>
        headers.TryGetValue(name, out var value) ? value switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string text => text,
            _ => null,
        } : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not store message {MessageId} in the inbox of module {Module}; it will be redelivered")]
    private static partial void LogDeliveryFailed(ILogger logger, Exception exception, string? messageId, string module);
}
