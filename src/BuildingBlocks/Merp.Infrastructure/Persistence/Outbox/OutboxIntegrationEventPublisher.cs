using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Messaging;
using Merp.Infrastructure.Messaging;

namespace Merp.Infrastructure.Persistence.Outbox;

/// <summary>Stamps integration events and stores them in the outbox of the open unit of work (D8).</summary>
public sealed class OutboxIntegrationEventPublisher(
    UnitOfWorkAccessor unitOfWorkAccessor,
    ITenantContext tenantContext,
    TimeProvider timeProvider) : IIntegrationEventPublisher
{
    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (unitOfWorkAccessor.Current is not IOutboxWriter outbox)
        {
            throw new InvalidOperationException(
                "Integration events can only be published inside a command (an open unit of work).");
        }

        IntegrationEvent stamped = integrationEvent with
        {
            OccurredAt = timeProvider.GetUtcNow(),
            TenantId = tenantContext.RequiredTenant.Id,
        };

        var eventType = stamped.GetType();
        outbox.AddOutboxMessage(new OutboxMessage
        {
            Id = stamped.EventId,
            Type = eventType.FullName ?? eventType.Name,
            Payload = JsonSerializer.Serialize(stamped, eventType, SerializerOptions),
            OccurredAt = stamped.OccurredAt,
            TraceParent = Activity.Current?.Id,
        });

        return Task.CompletedTask;
    }
}
