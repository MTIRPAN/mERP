using Merp.Modules.HR.Domain.PreHires;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merp.Modules.HR.Infrastructure.Persistence.Configurations;

internal sealed class PreHireConfiguration : IEntityTypeConfiguration<PreHire>
{
    public void Configure(EntityTypeBuilder<PreHire> builder)
    {
        builder.ToTable("pre_hires");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.PositionTitle).HasMaxLength(PreHire.TitleMaxLength);
        builder.Property(p => p.EmploymentType).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.FirstName).HasMaxLength(PreHire.NameMaxLength);
        builder.Property(p => p.LastName).HasMaxLength(PreHire.NameMaxLength);
        builder.Property(p => p.Email).HasMaxLength(PreHire.EmailMaxLength);
        builder.Property(p => p.Phone).HasMaxLength(PreHire.PhoneMaxLength);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.CancelReason).HasMaxLength(PreHire.ReasonMaxLength);
        builder.Ignore(p => p.FullName);

        // One pre-hire per application of Recruitment (D54)
        builder.HasIndex(p => p.ApplicationId).IsUnique();
        builder.HasIndex(p => new { p.CompanyId, p.Status });
    }
}
