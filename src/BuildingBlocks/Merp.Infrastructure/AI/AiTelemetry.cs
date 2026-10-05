using System.Diagnostics.Metrics;

namespace Merp.Infrastructure.AI;

/// <summary>Metrics of AI calls (D17): requests, tokens, estimated cost and duration per feature.</summary>
public sealed class AiTelemetry : IDisposable
{
    public const string MeterName = "Merp.AI";

    private readonly Meter _meter;
    private readonly Counter<long> _requests;
    private readonly Counter<long> _tokens;
    private readonly Counter<double> _cost;
    private readonly Histogram<double> _duration;

    public AiTelemetry(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        _meter = meterFactory.Create(MeterName);
        _requests = _meter.CreateCounter<long>("merp.ai.requests", description: "AI calls by feature and outcome.");
        _tokens = _meter.CreateCounter<long>("merp.ai.tokens", unit: "{token}", description: "Tokens by feature and kind.");
        _cost = _meter.CreateCounter<double>("merp.ai.cost", unit: "USD", description: "Estimated cost by feature.");
        _duration = _meter.CreateHistogram<double>("merp.ai.duration", unit: "s", description: "Duration of AI calls.");
    }

    public void Record(AiUsageEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var feature = new KeyValuePair<string, object?>("merp.ai.feature", entry.Feature);
        _requests.Add(1, feature, new("merp.ai.outcome", entry.Outcome.ToString()));
        _tokens.Add(entry.InputTokens, feature, new("merp.ai.token_kind", "input"));
        _tokens.Add(entry.OutputTokens, feature, new("merp.ai.token_kind", "output"));
        _tokens.Add(entry.CacheReadTokens, feature, new("merp.ai.token_kind", "cache_read"));
        _tokens.Add(entry.CacheWriteTokens, feature, new("merp.ai.token_kind", "cache_write"));
        _cost.Add((double)entry.CostUsd, feature);
        _duration.Record(entry.DurationMs / 1000d, feature);
    }

    public void Dispose() => _meter.Dispose();
}
