using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.SelfService;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merp.Modules.HR.Infrastructure.Persistence.Configurations;

internal sealed class ProfileChangeRequestConfiguration : IEntityTypeConfiguration<ProfileChangeRequest>
{
    public void Configure(EntityTypeBuilder<ProfileChangeRequest> builder)
    {
        builder.ToTable("profile_change_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Number).HasMaxLength(ProfileChangeRequest.NumberMaxLength);
        builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Proposed).HasColumnType("jsonb");
        builder.Property(r => r.Before).HasColumnType("jsonb");
        builder.Property(r => r.ProposedIban).HasMaxLength(34);
        builder.Property(r => r.BeforeIban).HasMaxLength(34);
        builder.Property(r => r.Note).HasMaxLength(ProfileChangeRequest.NoteMaxLength);
        builder.Property(r => r.ReturnComment).HasMaxLength(ProfileChangeRequest.NoteMaxLength);
        builder.HasOne<Employee>().WithMany().HasForeignKey(r => r.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => new { r.EmployeeId, r.Kind, r.Status });
        builder.HasIndex(r => new { r.CompanyId, r.Number }).IsUnique();
    }
}
