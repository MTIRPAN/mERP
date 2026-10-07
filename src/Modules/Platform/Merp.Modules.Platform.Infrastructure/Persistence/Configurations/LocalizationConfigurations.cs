using Merp.Modules.Platform.Domain.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merp.Modules.Platform.Infrastructure.Persistence.Configurations;

internal sealed class CultureConfiguration : IEntityTypeConfiguration<Culture>
{
    public void Configure(EntityTypeBuilder<Culture> builder)
    {
        builder.ToTable("cultures");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Code).HasMaxLength(Culture.CodeMaxLength);
        builder.Property(c => c.Name).HasMaxLength(Culture.NameMaxLength);
        builder.Property(c => c.FallbackCode).HasMaxLength(Culture.CodeMaxLength);
        builder.HasMany(c => c.Texts).WithOne().HasForeignKey(t => t.CultureId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Texts).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(c => c.Code).IsUnique();
        builder.HasIndex(c => c.IsDefault).IsUnique().HasFilter("is_default = true");
    }
}

internal sealed class CultureTextConfiguration : IEntityTypeConfiguration<CultureText>
{
    public void Configure(EntityTypeBuilder<CultureText> builder)
    {
        builder.ToTable("localization_texts");
        builder.HasKey(t => new { t.CultureId, t.Key });
        builder.Property(t => t.Key).HasMaxLength(CultureText.KeyMaxLength);
        builder.Property(t => t.Value).HasMaxLength(CultureText.ValueMaxLength);
    }
}
