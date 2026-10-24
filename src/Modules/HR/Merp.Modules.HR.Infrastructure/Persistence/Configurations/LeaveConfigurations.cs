using Merp.Infrastructure.Persistence.Conventions;
using Merp.Modules.HR.Domain.Leave;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merp.Modules.HR.Infrastructure.Persistence.Configurations;

internal sealed class LeaveTypeConfiguration : IEntityTypeConfiguration<LeaveType>
{
    public void Configure(EntityTypeBuilder<LeaveType> builder)
    {
        builder.ToTable("leave_types");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Code).HasMaxLength(LeaveType.CodeMaxLength);
        builder.Property(t => t.Category).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Payer).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.DeductsFrom).HasMaxLength(LeaveType.CodeMaxLength);
        builder.Property(t => t.Unit).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.DurationBasis).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.GenderRestriction).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.LegalBasis).HasMaxLength(LeaveType.LegalBasisMaxLength);
        builder.Property(t => t.Versions).HasJson();
        builder.HasIndex(t => t.Code).IsUnique();
    }
}

internal sealed class LeavePolicyConfiguration : IEntityTypeConfiguration<LeavePolicy>
{
    public void Configure(EntityTypeBuilder<LeavePolicy> builder)
    {
        builder.ToTable("leave_policies");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Code).HasMaxLength(LeavePolicy.CodeMaxLength);
        builder.Property(p => p.Versions).HasJson();
        builder.HasIndex(p => p.Code).IsUnique();
    }
}

internal sealed class LeavePolicyRuleSetConfiguration : IEntityTypeConfiguration<LeavePolicyRuleSet>
{
    public void Configure(EntityTypeBuilder<LeavePolicyRuleSet> builder)
    {
        builder.ToTable("leave_policy_rule_sets");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Rules).HasJson();
        builder.HasIndex(r => r.CompanyId).IsUnique();
    }
}
