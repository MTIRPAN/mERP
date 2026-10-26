using Merp.Infrastructure.Persistence.Conventions;
using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Candidates;
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

        // A candidate applies to an opening once (D49)
        builder.HasIndex(a => new { a.CandidateId, a.OpeningId }).IsUnique();
        builder.HasIndex(a => new { a.OpeningId, a.StageId });
        builder.HasIndex(a => new { a.CompanyId, a.AppliedAt });
    }
}
