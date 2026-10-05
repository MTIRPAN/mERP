using System.Diagnostics.Metrics;

namespace Merp.Infrastructure.Messaging.Integration;

/// <summary>Outbox and inbox metrics, exported through OpenTelemetry (D17).</summary>
public static class MessagingTelemetry
{
    public const string MeterName = "Merp.Messaging";

    private static readonly Meter Meter = new(MeterName);

    internal static readonly Counter<long> OutboxDispatched = Meter.CreateCounter<long>("merp.outbox.dispatched", description: "Outbox messages handed over to the transport");
    internal static readonly Counter<long> OutboxFailed = Meter.CreateCounter<long>("merp.outbox.failed", description: "Failed outbox dispatch attempts");
    internal static readonly Counter<long> OutboxDeadLettered = Meter.CreateCounter<long>("merp.outbox.dead_lettered", description: "Outbox messages that exceeded the retry limit");
    internal static readonly Histogram<double> OutboxLatency = Meter.CreateHistogram<double>("merp.outbox.latency", unit: "ms", description: "Time from publishing to dispatch");
    internal static readonly Counter<long> InboxProcessed = Meter.CreateCounter<long>("merp.inbox.processed", description: "Inbox messages handled successfully");
    internal static readonly Counter<long> InboxFailed = Meter.CreateCounter<long>("merp.inbox.failed", description: "Failed inbox handler attempts");
    internal static readonly Counter<long> InboxDeadLettered = Meter.CreateCounter<long>("merp.inbox.dead_lettered", description: "Inbox messages that exceeded the retry limit");
}
