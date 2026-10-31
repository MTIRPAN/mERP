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

    /// <summary>
    /// Sends many inputs of one feature to the provider's batch processing (D59): results come within 24 hours at
    /// about half price. The cost is estimated first; when it does not fit in what is left of the monthly budget,
    /// nothing is sent and <see cref="AiErrors.BudgetExceeded"/> is returned.
    /// </summary>
    Task<Result<AiBatch>> SubmitBatchAsync<T>(AiBatchRequest request, CancellationToken cancellationToken)
        where T : class;

    /// <summary>
    /// The results of a submitted batch, null while it runs. Once it ended every result is recorded in the usage
    /// ledger at the batch price, so the caller keeps them and does not ask again.
    /// </summary>
    Task<Result<IReadOnlyList<AiBatchResult<T>>?>> GetBatchResultsAsync<T>(AiBatch batch, CancellationToken cancellationToken)
        where T : class;
}

/// <summary>Many inputs of one feature with the same instructions, processed in a batch (D59).</summary>
public sealed record AiBatchRequest
{
    /// <summary>The most inputs one batch takes.</summary>
    public const int MaxItems = 10_000;

    public required string Feature { get; init; }

    public required string PromptVersion { get; init; }

    public required string Instructions { get; init; }

    /// <summary>One input per record; <see cref="AiBatchItem.CustomId"/> identifies its result.</summary>
    public required IReadOnlyList<AiBatchItem> Items { get; init; }

    public AiEffort Effort { get; init; } = AiEffort.Medium;

    public int? MaxOutputTokens { get; init; }

    /// <summary>The output tokens an input usually takes, for the cost estimate.</summary>
    public int ExpectedOutputTokens { get; init; } = 4_000;

    /// <summary>The kind of record the inputs are about; the usage ledger gets each item's id with it.</summary>
    public string? EntityType { get; init; }
}

/// <param name="CustomId">The caller's id of the input, e.g. an application id; at most 64 letters, digits, - or _.</param>
/// <param name="Input">The data, already minimized and redacted.</param>
public sealed record AiBatchItem(string CustomId, string Input);

/// <summary>A submitted batch: the provider's id, what it was sent for and its estimated cost.</summary>
public sealed record AiBatch(string Id, string Feature, string PromptVersion, string? EntityType, decimal EstimatedCostUsd);

/// <summary>The result of one input of a batch: its output, or why there is none.</summary>
public sealed record AiBatchResult<T>(string CustomId, Result<AiResponse<T>> Response);

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
