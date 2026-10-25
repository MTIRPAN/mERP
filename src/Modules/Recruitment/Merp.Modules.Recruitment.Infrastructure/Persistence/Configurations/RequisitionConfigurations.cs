using Merp.Modules.Recruitment.Domain.Requisitions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merp.Modules.Recruitment.Infrastructure.Persistence.Configurations;

internal sealed class JobRequisitionConfiguration : IEntityTypeConfiguration<JobRequisition>
{
    public void Configure(EntityTypeBuilder<JobRequisition> builder)
    {
        builder.ToTable("job_requisitions");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Number).HasMaxLength(JobRequisition.NumberMaxLength);
        builder.Property(r => r.NewPositionTitle).HasMaxLength(JobRequisition.TitleMaxLength);
        builder.Property(r => r.NewPositionLevel).HasMaxLength(JobRequisition.LevelMaxLength);
        builder.Property(r => r.Reason).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.EmploymentType).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.SalaryMin).HasPrecision(18, 2);
        builder.Property(r => r.SalaryMax).HasPrecision(18, 2);
        builder.Property(r => r.Currency).HasMaxLength(JobRequisition.CurrencyLength);
        builder.Property(r => r.Justification).HasMaxLength(JobRequisition.JustificationMaxLength);
        builder.Property(r => r.ReturnComment).HasMaxLength(JobRequisition.CommentMaxLength);
        builder.Property(r => r.CancellationReason).HasMaxLength(JobRequisition.CommentMaxLength);
        builder.HasIndex(r => new { r.CompanyId, r.Number }).IsUnique();
        builder.HasIndex(r => new { r.PositionId, r.Status });
        builder.HasIndex(r => new { r.CompanyId, r.CreatedAt });
    }
}
