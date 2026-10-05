namespace Merp.Infrastructure.AI;

/// <summary>
/// Records every AI call of the current tenant and sums its cost for budget checks. Implemented by the Platform
/// module (<c>platform.ai_usage</c>); writes are independent of the caller's transaction, because a call is billed
/// even when the business operation fails.
/// </summary>
public interface IAiUsageLedger
{
    Task RecordAsync(AiUsageEntry entry, CancellationToken cancellationToken);

    /// <summary>Total estimated cost of the current tenant's calls since <paramref name="since"/>.</summary>
    Task<decimal> GetCostSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);
}

/// <summary>One AI call. Never contains the prompt or the output.</summary>
public sealed record AiUsageEntry(
    DateTimeOffset OccurredAt,
    string Feature,
    string PromptVersion,
    string Model,
    AiUsageOutcome Outcome,
    Guid? UserId,
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens,
    decimal CostUsd,
    int DurationMs,
    string? EntityType,
    string? EntityId,
    string? TraceId);

public enum AiUsageOutcome
{
    Succeeded,
    Refused,
    Truncated,
    InvalidOutput,
    Failed,
}

/// <summary>Used when no module provides a ledger: nothing is recorded and budgets cannot be enforced.</summary>
internal sealed class NullAiUsageLedger : IAiUsageLedger
{
    public Task RecordAsync(AiUsageEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<decimal> GetCostSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) => Task.FromResult(0m);
}
