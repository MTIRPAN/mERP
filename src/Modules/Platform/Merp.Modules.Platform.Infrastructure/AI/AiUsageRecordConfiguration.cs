using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merp.Modules.Platform.Infrastructure.AI;

internal sealed class AiUsageRecordConfiguration : IEntityTypeConfiguration<AiUsageRecord>
{
    public void Configure(EntityTypeBuilder<AiUsageRecord> builder)
    {
        builder.ToTable("ai_usage");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Feature).HasMaxLength(100);
        builder.Property(r => r.PromptVersion).HasMaxLength(50);
        builder.Property(r => r.Model).HasMaxLength(100);
        builder.Property(r => r.Outcome).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.CostUsd).HasPrecision(18, 6);
        builder.Property(r => r.EntityType).HasMaxLength(100);
        builder.Property(r => r.EntityId).HasMaxLength(100);
        builder.Property(r => r.TraceId).HasMaxLength(32);
        builder.HasIndex(r => r.OccurredAt);
        builder.HasIndex(r => new { r.Feature, r.OccurredAt });
    }
}
