using Merp.SharedKernel.Results;

namespace Merp.Application.Abstractions.AI;

/// <summary>
/// Large language model features (D24). Modules depend on this abstraction, never on a provider SDK. Every call is
/// checked against the tenant's AI settings and monthly budget and is recorded in the usage ledger.
/// </summary>
public interface IAiService
{
    /// <summary>True when an AI provider is configured and <paramref name="feature"/> is enabled for the current tenant.</summary>
    bool IsEnabled(string feature);

    /// <summary>
    /// Sends <see cref="AiRequest.Input"/> with the feature's instructions and returns the model output deserialized
    /// to <typeparamref name="T"/>. The JSON schema of <typeparamref name="T"/> is enforced by the provider
    /// (structured outputs), so the result is always well formed. Input must already be minimized and redacted.
    /// </summary>
    Task<Result<AiResponse<T>>> GenerateAsync<T>(AiRequest request, CancellationToken cancellationToken)
        where T : class;
}

/// <summary>One model call.</summary>
public sealed record AiRequest
{
    /// <summary>Feature key, <c>{module}.{feature}</c> (e.g. <c>recruitment.cv_analysis</c>).</summary>
    public required string Feature { get; init; }

    /// <summary>Version of the instructions, recorded with the usage so results can be traced to a prompt.</summary>
    public required string PromptVersion { get; init; }

    /// <summary>Stable instructions (system prompt). Kept identical between calls so the provider can cache them.</summary>
    public required string Instructions { get; init; }

    /// <summary>The data to work on for this call.</summary>
    public required string Input { get; init; }

    public AiEffort Effort { get; init; } = AiEffort.Medium;

    /// <summary>Upper bound for output tokens; the configured default is used when null.</summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>Optional record the call is about, written to the usage ledger (never the content itself).</summary>
    public string? EntityType { get; init; }

    public string? EntityId { get; init; }
}

/// <summary>How much reasoning the model should spend; higher costs more and takes longer.</summary>
public enum AiEffort
{
    Low,
    Medium,
    High,
}

public sealed record AiResponse<T>(T Value, AiUsage Usage);

/// <summary>Tokens and estimated cost of one call.</summary>
public sealed record AiUsage(
    string Model,
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens,
    decimal CostUsd,
    TimeSpan Duration);
