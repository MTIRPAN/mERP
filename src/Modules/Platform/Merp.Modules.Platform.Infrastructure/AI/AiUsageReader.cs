using Merp.Modules.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Infrastructure.AI;

/// <summary>Usage totals of the current tenant for the AI usage screen.</summary>
public sealed class AiUsageReader(PlatformDbContext context)
{
    public async Task<IReadOnlyList<AiFeatureUsage>> GetByFeatureAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var rows = await context.Set<AiUsageRecord>()
            .AsNoTracking()
            .Where(r => r.OccurredAt >= from && r.OccurredAt < to)
            .GroupBy(r => r.Feature)
            .Select(g => new AiFeatureUsage(
                g.Key,
                g.Count(),
                g.Count(r => r.Outcome == Merp.Infrastructure.AI.AiUsageOutcome.Succeeded),
                g.Sum(r => r.InputTokens + r.CacheReadTokens + r.CacheWriteTokens),
                g.Sum(r => r.OutputTokens),
                g.Sum(r => r.CostUsd)))
            .ToListAsync(cancellationToken);

        return [.. rows.OrderByDescending(r => r.CostUsd).ThenBy(r => r.Feature, StringComparer.Ordinal)];
    }
}

/// <param name="Feature">Feature key.</param>
/// <param name="Requests">All calls, whatever the outcome.</param>
/// <param name="Succeeded">Calls that returned a usable result.</param>
/// <param name="InputTokens">Input tokens including cached ones.</param>
/// <param name="OutputTokens">Output tokens, including the model's reasoning.</param>
/// <param name="CostUsd">Estimated cost at the configured list prices.</param>
public sealed record AiFeatureUsage(string Feature, int Requests, int Succeeded, long InputTokens, long OutputTokens, decimal CostUsd);
