using System.ComponentModel;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Numbering;
using Merp.Infrastructure.Persistence;
using Merp.Modules.Platform.Application.Numbering;
using Merp.Modules.Platform.Domain.Numbering;
using Merp.Modules.Platform.Infrastructure.Persistence;
using Merp.SharedKernel.Numbering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Merp.Modules.Platform.Infrastructure.Numbering;

internal sealed class NumberSequenceConfiguration : IEntityTypeConfiguration<NumberSequence>
{
    public void Configure(EntityTypeBuilder<NumberSequence> builder)
    {
        builder.ToTable("number_sequences");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.DocumentType).HasMaxLength(NumberSequence.DocumentTypeMaxLength);
        builder.Property(s => s.Pattern).HasMaxLength(NumberPattern.MaxLength);
        builder.Property(s => s.ResetPeriod).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(s => s.IsDefault);

        // One default per document type and one pattern per company
        builder.HasIndex(s => s.DocumentType).IsUnique().HasFilter("company_id IS NULL");
        builder.HasIndex(s => new { s.DocumentType, s.CompanyId }).IsUnique().HasFilter("company_id IS NOT NULL");
    }
}

/// <summary>All sequences of a tenant, cached as one entry (a tenant has a few dozen).</summary>
[ImmutableObject(true)]
public sealed record NumberingSnapshot(IReadOnlyList<NumberingSnapshot.Entry> Sequences)
{
    public sealed record Entry(string DocumentType, Guid? CompanyId, string Pattern, NumberResetPeriod ResetPeriod);
}

/// <summary>The pattern for a company: its own, otherwise the tenant default (D29). Cached per tenant.</summary>
internal sealed class PlatformNumberSequences(PlatformDbContext db, HybridCache cache, ITenantContext tenant) : INumberSequenceSettingsSource
{
    private static readonly TimeSpan Duration = TimeSpan.FromHours(1);

    public async Task<NumberSequenceSettings?> GetAsync(Guid companyId, string documentType, CancellationToken cancellationToken = default)
    {
        var tenantId = tenant.RequiredTenant.Id;
        var snapshot = await cache.GetOrCreateAsync(
            NumberingCache.Key(tenantId),
            db,
            static async (context, ct) => new NumberingSnapshot([.. await context.NumberSequences.AsNoTracking()
                .Select(s => new NumberingSnapshot.Entry(s.DocumentType, s.CompanyId, s.Pattern, s.ResetPeriod))
                .ToListAsync(ct)]),
            new HybridCacheEntryOptions { Expiration = Duration, LocalCacheExpiration = Duration },
            [NumberingCache.Tag(tenantId)],
            cancellationToken);

        var match = snapshot.Sequences.FirstOrDefault(s => s.DocumentType == documentType && s.CompanyId == companyId)
            ?? snapshot.Sequences.FirstOrDefault(s => s.DocumentType == documentType && s.CompanyId is null);
        return match is null ? null : new NumberSequenceSettings(match.Pattern, match.ResetPeriod);
    }
}

internal sealed class NumberingCache(HybridCache cache, ITenantContext tenant) : INumberingCache
{
    public static string Key(Guid tenantId) => $"merp:{tenantId}:numbering";

    public static string Tag(Guid tenantId) => $"{tenantId}:numbering";

    public async Task InvalidateAsync(CancellationToken cancellationToken = default) =>
        await cache.RemoveByTagAsync(Tag(tenant.RequiredTenant.Id), cancellationToken);
}

/// <summary>
/// Adds the default sequence of every declared document type that has none yet (PLAN P5). Existing patterns, edited
/// or not, are kept; a document type removed from code keeps its row.
/// </summary>
internal sealed partial class NumberingSeeder(ILogger<NumberingSeeder> logger) : ITenantDataSeeder
{
    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<PlatformDbContext>();
        var existing = await db.NumberSequences.Where(s => s.CompanyId == null).Select(s => s.DocumentType).ToListAsync(cancellationToken);
        var added = 0;
        foreach (var definition in services.GetServices<INumberSequenceDefinitionProvider>().SelectMany(p => p.GetSequences())
                     .Where(d => !existing.Contains(d.DocumentType, StringComparer.Ordinal)))
        {
            var sequence = NumberSequence.Create(definition.DocumentType, null, definition.DefaultPattern, definition.ResetPeriod);
            db.NumberSequences.Add(sequence.IsSuccess
                ? sequence.Value
                : throw new InvalidOperationException($"The default pattern of {definition.DocumentType} is invalid: {sequence.Error.Code}."));
            existing.Add(definition.DocumentType);
            added++;
        }

        await db.SaveChangesAsync(cancellationToken);
        LogSeeded(logger, added);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Number sequences seeded: {Added} added")]
    private static partial void LogSeeded(ILogger logger, int added);
}
