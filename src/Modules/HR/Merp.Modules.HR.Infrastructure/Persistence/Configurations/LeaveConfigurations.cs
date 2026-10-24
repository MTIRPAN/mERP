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

internal sealed class LeaveAccountConfiguration : IEntityTypeConfiguration<LeaveAccount>
{
    public void Configure(EntityTypeBuilder<LeaveAccount> builder)
    {
        builder.ToTable("leave_accounts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.LeaveTypeCode).HasMaxLength(LeaveType.CodeMaxLength);
        builder.HasMany(a => a.Entries).WithOne().HasForeignKey(e => e.AccountId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(a => a.Entries).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(a => new { a.EmploymentId, a.LeaveTypeCode }).IsUnique();
        builder.HasIndex(a => a.EmployeeId);
    }
}

internal sealed class LeaveLedgerEntryConfiguration : IEntityTypeConfiguration<LeaveLedgerEntry>
{
    public void Configure(EntityTypeBuilder<LeaveLedgerEntry> builder)
    {
        builder.ToTable("leave_ledger_entries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Quantity).HasPrecision(7, 2);
        builder.Property(e => e.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.PeriodKey).HasMaxLength(LeaveLedgerEntry.PeriodKeyMaxLength);
        builder.Property(e => e.Note).HasMaxLength(LeaveLedgerEntry.NoteMaxLength);

        // One accrual per period of an account, whatever runs the job (docs/domain/izin-kurallari.md §5)
        builder.HasIndex(e => new { e.AccountId, e.PeriodKey }).IsUnique().HasFilter("source = 'Accrual'");
    }
}

internal sealed class LeaveRequestConfiguration : IEntityTypeConfiguration<LeaveRequest>
{
    public void Configure(EntityTypeBuilder<LeaveRequest> builder)
    {
        builder.ToTable("leave_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Number).HasMaxLength(LeaveRequest.NumberMaxLength);
        builder.Property(r => r.Origin).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(r => r.LeaveTypeCode).HasMaxLength(LeaveType.CodeMaxLength);
        builder.Property(r => r.DeductsFrom).HasMaxLength(LeaveType.CodeMaxLength);
        builder.Property(r => r.StartPart).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.EndPart).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Basis).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Quantity).HasPrecision(7, 2);
        builder.Property(r => r.Days).HasPrecision(7, 2);
        builder.Property(r => r.Note).HasMaxLength(LeaveRequest.NoteMaxLength);
        builder.Property(r => r.Address).HasMaxLength(LeaveRequest.AddressMaxLength);
        builder.Property(r => r.Phone).HasMaxLength(LeaveRequest.PhoneMaxLength);
        builder.Property(r => r.ReturnComment).HasMaxLength(LeaveRequest.CommentMaxLength);
        builder.Property(r => r.CancellationReason).HasMaxLength(LeaveRequest.CommentMaxLength);
        builder.Ignore(r => r.Period);
        builder.HasIndex(r => new { r.CompanyId, r.Number }).IsUnique();
        builder.HasIndex(r => new { r.EmployeeId, r.StartDate });
        builder.HasIndex(r => new { r.EmploymentId, r.Status });
        builder.HasIndex(r => new { r.Status, r.StartDate });
    }
}
