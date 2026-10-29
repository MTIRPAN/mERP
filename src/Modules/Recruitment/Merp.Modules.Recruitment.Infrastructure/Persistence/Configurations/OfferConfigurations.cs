using Merp.Modules.Recruitment.Domain.Offers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merp.Modules.Recruitment.Infrastructure.Persistence.Configurations;

internal sealed class JobOfferConfiguration : IEntityTypeConfiguration<JobOffer>
{
    public void Configure(EntityTypeBuilder<JobOffer> builder)
    {
        builder.ToTable("job_offers");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Number).HasMaxLength(JobOffer.NumberMaxLength);
        builder.Property(o => o.PositionTitle).HasMaxLength(JobOffer.TitleMaxLength);
        builder.Property(o => o.Grade).HasMaxLength(JobOffer.GradeMaxLength);
        builder.Property(o => o.Salary).HasPrecision(18, 2);
        builder.Property(o => o.Currency).HasMaxLength(JobOffer.CurrencyLength);
        builder.Property(o => o.Period).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.Benefits).HasMaxLength(JobOffer.BenefitsMaxLength);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.ReturnComment).HasMaxLength(JobOffer.CommentMaxLength);
        builder.Property(o => o.WithdrawalReason).HasMaxLength(JobOffer.CommentMaxLength);
        builder.Property(o => o.ResponseTokenHash).HasMaxLength(JobOffer.TokenHashLength).IsFixedLength();
        builder.Property(o => o.RespondedVia).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.ResponseNote).HasMaxLength(JobOffer.CommentMaxLength);
        builder.Ignore(o => o.IsOpen);

        builder.HasIndex(o => new { o.CompanyId, o.Number }).IsUnique();
        builder.HasIndex(o => o.ApplicationId);
        builder.HasIndex(o => o.ResponseTokenHash).IsUnique().HasFilter("response_token_hash IS NOT NULL");

        // The expiry job: sent offers by their last day
        builder.HasIndex(o => o.ExpiresOn).HasFilter("status = 'Sent'").HasDatabaseName("ix_job_offers_sent_expires_on");
    }
}
