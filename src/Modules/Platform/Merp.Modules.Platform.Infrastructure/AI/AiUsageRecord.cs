using Merp.Infrastructure.AI;

namespace Merp.Modules.Platform.Infrastructure.AI;

/// <summary>Row of <c>platform.ai_usage</c>: one AI call of the tenant (D24). Append only; holds no prompt or output.</summary>
internal sealed class AiUsageRecord
{
    public Guid Id { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public required string Feature { get; init; }

    public required string PromptVersion { get; init; }

    public required string Model { get; init; }

    public AiUsageOutcome Outcome { get; init; }

    public Guid? UserId { get; init; }

    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public long CacheReadTokens { get; init; }

    public long CacheWriteTokens { get; init; }

    public decimal CostUsd { get; init; }

    public int DurationMs { get; init; }

    public string? EntityType { get; init; }

    public string? EntityId { get; init; }

    public string? TraceId { get; init; }
}
