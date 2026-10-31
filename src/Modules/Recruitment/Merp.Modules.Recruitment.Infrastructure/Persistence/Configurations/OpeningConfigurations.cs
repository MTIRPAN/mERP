using Merp.Infrastructure.Persistence.Conventions;
using Merp.Modules.Recruitment.Domain.Openings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merp.Modules.Recruitment.Infrastructure.Persistence.Configurations;

internal sealed class JobOpeningConfiguration : IEntityTypeConfiguration<JobOpening>
{
    public void Configure(EntityTypeBuilder<JobOpening> builder)
    {
        builder.ToTable("job_openings");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Title).HasMaxLength(JobOpening.TitleMaxLength);
        builder.Property(o => o.Description).HasMaxLength(JobOpening.RichTextMaxLength);
        builder.Property(o => o.Qualifications).HasMaxLength(JobOpening.RichTextMaxLength);
        builder.Property(o => o.WorkModel).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.EmploymentType).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.Visibility).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.CloseReason).HasMaxLength(JobOpening.ReasonMaxLength);
        builder.Property(o => o.Team).HasJson();
        builder.Property(o => o.Form).HasJson();
        builder.Property(o => o.Stages).HasJson();
        builder.Ignore(o => o.AiDraftPending);
        builder.HasIndex(o => new { o.CompanyId, o.CreatedAt });
        builder.HasIndex(o => o.RequisitionId);

        // The career page lists the published openings (R3b)
        builder.HasIndex(o => new { o.Status, o.Visibility });
    }
}

internal sealed class OpeningScreeningConfiguration : IEntityTypeConfiguration<OpeningScreening>
{
    public void Configure(EntityTypeBuilder<OpeningScreening> builder)
    {
        builder.ToTable("opening_screenings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.BatchId).HasMaxLength(OpeningScreening.BatchIdMaxLength);
        builder.Property(s => s.RequirementsHash).HasMaxLength(OpeningScreening.RequirementsHashLength).IsFixedLength();
        builder.Property(s => s.HoldReason).HasMaxLength(OpeningScreening.ReasonMaxLength);
        builder.Property(s => s.EstimatedCostUsd).HasPrecision(12, 4);
        builder.Property(s => s.Items).HasJson();
        builder.Ignore(s => s.IsOpen);

        builder.HasIndex(s => new { s.OpeningId, s.RequestedAt });

        // An opening has at most one screening waiting, running or held
        builder.HasIndex(s => s.OpeningId).IsUnique().HasFilter("status IN ('Queued', 'Submitted', 'Held')").HasDatabaseName("ux_opening_screenings_open");
    }
}
