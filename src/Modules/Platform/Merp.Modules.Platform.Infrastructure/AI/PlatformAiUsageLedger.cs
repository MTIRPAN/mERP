using Merp.Infrastructure.AI;
using Merp.Modules.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Infrastructure.AI;

/// <summary>
/// Writes usage with its own <see cref="PlatformDbContext"/> instance (own connection, own transaction), so a record
/// survives when the calling command rolls back and never flushes the caller's pending changes.
/// </summary>
internal sealed class PlatformAiUsageLedger(DbContextOptions<PlatformDbContext> options) : IAiUsageLedger
{
    public async Task RecordAsync(AiUsageEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await using var context = new PlatformDbContext(options);
        context.Set<AiUsageRecord>().Add(new AiUsageRecord
        {
            Id = Guid.CreateVersion7(entry.OccurredAt),
            OccurredAt = entry.OccurredAt,
            Feature = entry.Feature,
            PromptVersion = entry.PromptVersion,
            Model = entry.Model,
            Outcome = entry.Outcome,
            UserId = entry.UserId,
            InputTokens = entry.InputTokens,
            OutputTokens = entry.OutputTokens,
            CacheReadTokens = entry.CacheReadTokens,
            CacheWriteTokens = entry.CacheWriteTokens,
            CostUsd = entry.CostUsd,
            DurationMs = entry.DurationMs,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            TraceId = entry.TraceId,
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<decimal> GetCostSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var context = new PlatformDbContext(options);
        return await context.Set<AiUsageRecord>()
            .Where(r => r.OccurredAt >= since)
            .SumAsync(r => r.CostUsd, cancellationToken);
    }
}
