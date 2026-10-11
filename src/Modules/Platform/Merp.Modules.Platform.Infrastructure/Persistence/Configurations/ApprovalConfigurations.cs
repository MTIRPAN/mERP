using Merp.Modules.Platform.Domain.Approvals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merp.Modules.Platform.Infrastructure.Persistence.Configurations;

internal sealed class ApprovalDefinitionConfiguration : IEntityTypeConfiguration<ApprovalDefinition>
{
    public void Configure(EntityTypeBuilder<ApprovalDefinition> builder)
    {
        builder.ToTable("approval_definitions");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.DocumentType).HasMaxLength(ApprovalDefinition.DocumentTypeMaxLength);
        builder.Property(d => d.EntryCondition).HasColumnType("jsonb");
        builder.Property(d => d.Steps).HasJson();
        builder.Property(d => d.Fallback).HasJson();

        // Definition selection (§2.2): the active flows of a document type
        builder.HasIndex(d => new { d.DocumentType, d.CompanyId, d.Priority });
    }
}

internal sealed class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ApprovalRequest> builder)
    {
        builder.ToTable("approval_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.DocumentType).HasMaxLength(ApprovalRequest.DocumentTypeMaxLength);
        builder.Property(r => r.DocumentId).HasMaxLength(ApprovalRequest.DocumentIdMaxLength);
        builder.Property(r => r.Facts).HasColumnType("jsonb");
        builder.Property(r => r.TitleKey).HasMaxLength(200);
        builder.Property(r => r.TitleArgs).HasJson();
        builder.Property(r => r.Summary).HasMaxLength(500);
        builder.Property(r => r.Link).HasMaxLength(500);
        builder.Property(r => r.Steps).HasJson();
        builder.Property(r => r.Fallback).HasJson();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(r => r.Step);
        builder.Ignore(r => r.OpenTasks);
        builder.Ignore(r => r.NextStep);
        builder.HasMany(r => r.Tasks).WithOne().HasForeignKey(t => t.RequestId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Tasks).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(r => r.History).WithOne().HasForeignKey(h => h.RequestId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.History).UsePropertyAccessMode(PropertyAccessMode.Field);

        // One request per document (D34); a returned document comes back on it
        builder.HasIndex(r => new { r.DocumentType, r.DocumentId }).IsUnique();
        builder.HasIndex(r => new { r.Status, r.SubmittedAt });
        builder.HasIndex(r => r.RequesterUserId);
    }
}

internal sealed class ApprovalTaskConfiguration : IEntityTypeConfiguration<ApprovalTask>
{
    public void Configure(EntityTypeBuilder<ApprovalTask> builder)
    {
        builder.ToTable("approval_tasks");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Comment).HasMaxLength(ApprovalRequest.CommentMaxLength);

        // The approval inbox (open tasks of a user) and the escalation job (open tasks past due)
        builder.HasIndex(t => t.AssigneeUserId).HasFilter("status = 'Pending'").HasDatabaseName("ix_approval_tasks_open");
        builder.HasIndex(t => t.DueAt).HasFilter("status = 'Pending' AND escalated_at IS NULL").HasDatabaseName("ix_approval_tasks_due");
    }
}

internal sealed class ApprovalHistoryEntryConfiguration : IEntityTypeConfiguration<ApprovalHistoryEntry>
{
    public void Configure(EntityTypeBuilder<ApprovalHistoryEntry> builder)
    {
        builder.ToTable("approval_history");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedNever();
        builder.HasIndex(h => new { h.RequestId, h.Sequence });
        builder.Property(h => h.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.Detail).HasMaxLength(ApprovalRequest.CommentMaxLength);
    }
}

internal sealed class DelegationConfiguration : IEntityTypeConfiguration<Delegation>
{
    public void Configure(EntityTypeBuilder<Delegation> builder)
    {
        builder.ToTable("delegations");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.DocumentTypes).HasJson();
        builder.HasIndex(d => new { d.PrincipalUserId, d.From, d.To });
        builder.HasIndex(d => d.DelegateUserId);
    }
}
