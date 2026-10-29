using Merp.Infrastructure.Persistence.Conventions;
using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Candidates;
using Merp.Modules.Recruitment.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merp.Modules.Recruitment.Infrastructure.Persistence.Configurations;

internal sealed class CandidateConfiguration : IEntityTypeConfiguration<Candidate>
{
    public void Configure(EntityTypeBuilder<Candidate> builder)
    {
        builder.ToTable("candidates");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Email).HasMaxLength(Candidate.EmailMaxLength);
        builder.Property(c => c.FirstName).HasMaxLength(Candidate.NameMaxLength);
        builder.Property(c => c.LastName).HasMaxLength(Candidate.NameMaxLength);
        builder.Property(c => c.Phone).HasMaxLength(Candidate.PhoneMaxLength);
        builder.Property(c => c.Culture).HasMaxLength(Candidate.CultureMaxLength);
        builder.Property(c => c.Source).HasMaxLength(Candidate.SourceMaxLength);
        builder.Ignore(c => c.FullName);

        // One candidate per e-mail address in the tenant (D49)
        builder.HasIndex(c => c.Email).IsUnique();
        builder.HasIndex(c => c.EmployeeId);
    }
}

internal sealed class JobApplicationConfiguration : IEntityTypeConfiguration<JobApplication>
{
    public void Configure(EntityTypeBuilder<JobApplication> builder)
    {
        builder.ToTable("job_applications");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.CoverLetter).HasMaxLength(JobApplication.CoverLetterMaxLength);
        builder.Property(a => a.Source).HasMaxLength(Candidate.SourceMaxLength);
        builder.Property(a => a.Answers).HasJson();
        builder.Property(a => a.Consent).HasJson();
        builder.Property(a => a.ReasonCode).HasMaxLength(RecruitmentLookup.CodeMaxLength);
        builder.HasMany(a => a.History).WithOne().HasForeignKey(h => h.ApplicationId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(a => a.History).UsePropertyAccessMode(PropertyAccessMode.Field);

        // A candidate applies to an opening once (D49)
        builder.HasIndex(a => new { a.CandidateId, a.OpeningId }).IsUnique();
        builder.HasIndex(a => new { a.OpeningId, a.StageId });
        builder.HasIndex(a => new { a.CompanyId, a.AppliedAt });

        // The rejection e-mails job: the e-mails waiting (D50)
        builder.HasIndex(a => a.RejectionEmailDueAt).HasFilter("rejection_email_due_at IS NOT NULL").HasDatabaseName("ix_job_applications_rejection_email_due");
    }
}

internal sealed class ApplicationStageChangeConfiguration : IEntityTypeConfiguration<ApplicationStageChange>
{
    public void Configure(EntityTypeBuilder<ApplicationStageChange> builder)
    {
        builder.ToTable("application_stage_changes");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedNever();
        builder.Property(h => h.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.ReasonCode).HasMaxLength(RecruitmentLookup.CodeMaxLength);
        builder.Property(h => h.Note).HasMaxLength(JobApplication.NoteMaxLength);
        builder.HasIndex(h => new { h.ApplicationId, h.Sequence });
    }
}
