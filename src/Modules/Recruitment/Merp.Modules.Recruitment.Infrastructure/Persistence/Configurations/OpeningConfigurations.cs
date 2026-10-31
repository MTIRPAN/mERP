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
