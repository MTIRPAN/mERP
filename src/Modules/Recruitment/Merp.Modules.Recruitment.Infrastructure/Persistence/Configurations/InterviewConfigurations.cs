using Merp.Infrastructure.Persistence.Conventions;
using Merp.Modules.Recruitment.Domain.Interviews;
using Merp.Modules.Recruitment.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merp.Modules.Recruitment.Infrastructure.Persistence.Configurations;

internal sealed class InterviewConfiguration : IEntityTypeConfiguration<Interview>
{
    public void Configure(EntityTypeBuilder<Interview> builder)
    {
        builder.ToTable("interviews");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.Location).HasMaxLength(Interview.LocationMaxLength);
        builder.Property(i => i.OnlineLink).HasMaxLength(Interview.LinkMaxLength);
        builder.Property(i => i.Notes).HasMaxLength(Interview.NotesMaxLength);
        builder.Property(i => i.CancelReason).HasMaxLength(Interview.ReasonMaxLength);
        builder.Property(i => i.Evaluation).HasJson();

        // A uuid[] array, so "my interviews" is filtered in SQL (= ANY)
        builder.PrimitiveCollection(i => i.InterviewerUserIds);

        builder.HasIndex(i => i.ApplicationId);
        builder.HasIndex(i => new { i.CompanyId, i.StartsAt });
        builder.HasIndex(i => i.InterviewerUserIds).HasMethod("gin");

        // The reminders job: planned interviews by start
        builder.HasIndex(i => i.StartsAt).HasFilter("status = 'Scheduled'").HasDatabaseName("ix_interviews_scheduled_starts_at");
    }
}

internal sealed class InterviewFeedbackConfiguration : IEntityTypeConfiguration<InterviewFeedback>
{
    public void Configure(EntityTypeBuilder<InterviewFeedback> builder)
    {
        builder.ToTable("interview_feedback");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();
        builder.Property(f => f.Recommendation).HasConversion<string>().HasMaxLength(20);
        builder.Property(f => f.Scores).HasJson();
        builder.Property(f => f.Answers).HasJson();
        builder.Property(f => f.Summary).HasMaxLength(InterviewFeedback.SummaryMaxLength);
        builder.Property(f => f.WeightedScore).HasPrecision(4, 2);

        // One form per interviewer and interview (D52)
        builder.HasIndex(f => new { f.InterviewId, f.InterviewerUserId }).IsUnique();
        builder.HasIndex(f => f.ApplicationId);
    }
}

internal sealed class EvaluationTemplateConfiguration : IEntityTypeConfiguration<EvaluationTemplate>
{
    public void Configure(EntityTypeBuilder<EvaluationTemplate> builder)
    {
        builder.ToTable("evaluation_templates");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Name).HasMaxLength(EvaluationTemplate.NameMaxLength);
        builder.Property(t => t.Form).HasJson();
    }
}
