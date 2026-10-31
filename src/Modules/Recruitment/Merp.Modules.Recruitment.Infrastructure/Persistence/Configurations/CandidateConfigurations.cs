using Merp.Infrastructure.Persistence.Conventions;
using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Candidates;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.Modules.Recruitment.Infrastructure.Candidates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

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
        builder.Property(c => c.NormalizedPhone).HasMaxLength(Candidate.PhoneMaxLength);
        builder.Property(c => c.Location).HasMaxLength(CandidateProfile.TextMaxLength);
        builder.Property(c => c.CurrentTitle).HasMaxLength(CandidateProfile.TextMaxLength);
        builder.Property(c => c.CurrentCompany).HasMaxLength(CandidateProfile.TextMaxLength);
        builder.Property(c => c.ExperienceYears).HasPrecision(4, 1);
        builder.Property(c => c.Education).HasMaxLength(CandidateProfile.EducationMaxLength);
        builder.Property(c => c.LinkedInUrl).HasMaxLength(CandidateProfile.UrlMaxLength);
        builder.Property(c => c.PortfolioUrl).HasMaxLength(CandidateProfile.UrlMaxLength);
        builder.Property(c => c.Tags).HasJson();
        builder.Property(c => c.TagText).HasMaxLength(Candidate.TagTextMaxLength);
        builder.Ignore(c => c.FullName);
        builder.Ignore(c => c.IsAnonymized);
        builder.Ignore(c => c.RetentionStart);
        builder.Property(c => c.ConsentChanges).HasJson();
        builder.Property(c => c.ConsentTokenHash).HasMaxLength(Candidate.TokenHashLength).IsFixedLength();
        builder.HasMany(c => c.Notes).WithOne().HasForeignKey(n => n.CandidateId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Notes).UsePropertyAccessMode(PropertyAccessMode.Field);

        // The pool search (D50): names, profile, tags, notes and the CV text, with the Turkish dictionary
        builder.Property<NpgsqlTsVector>(CandidateSearch.VectorProperty)
            .HasComputedColumnSql(
                "to_tsvector('turkish', coalesce(first_name, '') || ' ' || coalesce(last_name, '') || ' ' || coalesce(email, '') || ' ' "
                + "|| coalesce(current_title, '') || ' ' || coalesce(current_company, '') || ' ' || coalesce(location, '') || ' ' "
                + "|| coalesce(education, '') || ' ' || coalesce(tag_text, '') || ' ' || coalesce(notes_text, '') || ' ' || coalesce(cv_text, ''))",
                stored: true);
        builder.HasIndex(CandidateSearch.VectorProperty).HasMethod("gin");

        // One candidate per e-mail address in the tenant (D49); the same phone or name suggests a duplicate (D50)
        builder.HasIndex(c => c.Email).IsUnique();
        builder.HasIndex(c => c.EmployeeId);
        builder.HasIndex(c => c.NormalizedPhone);
        builder.HasIndex(c => new { c.LastName, c.FirstName });
        builder.HasIndex(c => c.ConsentTokenHash).IsUnique().HasFilter("consent_token_hash IS NOT NULL");
    }
}

internal sealed class CandidateNoteConfiguration : IEntityTypeConfiguration<CandidateNote>
{
    public void Configure(EntityTypeBuilder<CandidateNote> builder)
    {
        builder.ToTable("candidate_notes");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();
        builder.Property(n => n.Body).HasMaxLength(CandidateNote.BodyMaxLength);
        builder.Property(n => n.MentionedUserIds).HasJson();
        builder.HasIndex(n => new { n.CandidateId, n.At });
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
        builder.Property(a => a.RejectionEmailText).HasJson();
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

internal sealed class ApplicationCvAnalysisConfiguration : IEntityTypeConfiguration<ApplicationCvAnalysis>
{
    public void Configure(EntityTypeBuilder<ApplicationCvAnalysis> builder)
    {
        builder.ToTable("application_cv_analyses");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Result).HasColumnType("jsonb");
        builder.Property(a => a.ErrorCode).HasMaxLength(ApplicationCvAnalysis.ErrorCodeMaxLength);
        builder.Property(a => a.Model).HasMaxLength(ApplicationCvAnalysis.ModelMaxLength);
        builder.Property(a => a.PromptVersion).HasMaxLength(ApplicationCvAnalysis.PromptVersionMaxLength);
        builder.Property(a => a.RequirementsHash).HasMaxLength(ApplicationCvAnalysis.RequirementsHashLength).IsFixedLength();
        builder.Ignore(a => a.Succeeded);

        // The analysis goes with its application (same aggregate family, same schema)
        builder.HasOne<JobApplication>().WithOne().HasForeignKey<ApplicationCvAnalysis>(a => a.Id).OnDelete(DeleteBehavior.Cascade);
    }
}
