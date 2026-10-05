namespace Merp.Infrastructure.Messaging.Integration;

/// <summary>An integration event on its way from an outbox to the inboxes of its handlers.</summary>
public sealed record MessageEnvelope(Guid MessageId, string Type, string Payload, Guid TenantId, DateTimeOffset OccurredAt, string? TraceParent);
