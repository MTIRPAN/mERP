using Merp.Infrastructure.Persistence.Conventions;
using Merp.Modules.Recruitment.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merp.Modules.Recruitment.Infrastructure.Persistence.Configurations;

internal sealed class StageTemplateConfiguration : IEntityTypeConfiguration<StageTemplate>
{
    public void Configure(EntityTypeBuilder<StageTemplate> builder)
    {
        builder.ToTable("stage_templates");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Stages).HasJson();
    }
}

internal sealed class RecruitmentLookupConfiguration : IEntityTypeConfiguration<RecruitmentLookup>
{
    public void Configure(EntityTypeBuilder<RecruitmentLookup> builder)
    {
        builder.ToTable("lookups");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Kind).HasConversion<string>().HasMaxLength(30);
        builder.Property(l => l.Code).HasMaxLength(RecruitmentLookup.CodeMaxLength);
        builder.HasIndex(l => new { l.Kind, l.Code }).IsUnique();
    }
}

internal sealed class ConsentTextConfiguration : IEntityTypeConfiguration<ConsentText>
{
    public void Configure(EntityTypeBuilder<ConsentText> builder)
    {
        builder.ToTable("consent_texts");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Kind).HasConversion<string>().HasMaxLength(30);
        builder.Property(t => t.Culture).HasMaxLength(ConsentText.CultureMaxLength);
        builder.Property(t => t.Body).HasMaxLength(ConsentText.BodyMaxLength);

        // One row per version; a company's texts and every company's (null) count separately
        builder.HasIndex(t => new { t.Kind, t.CompanyId, t.Culture, t.Revision }).IsUnique().AreNullsDistinct(false);
    }
}
