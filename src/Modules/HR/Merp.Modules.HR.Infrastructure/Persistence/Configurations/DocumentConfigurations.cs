using Merp.Modules.HR.Domain.Documents;
using Merp.Modules.HR.Domain.Employees;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merp.Modules.HR.Infrastructure.Persistence.Configurations;

internal sealed class DocumentTypeConfiguration : IEntityTypeConfiguration<DocumentType>
{
    public void Configure(EntityTypeBuilder<DocumentType> builder)
    {
        builder.ToTable("document_types");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Code).HasMaxLength(DocumentType.CodeMaxLength);
        builder.HasIndex(t => t.Code).IsUnique();
    }
}

internal sealed class EmployeeDocumentConfiguration : IEntityTypeConfiguration<EmployeeDocument>
{
    public void Configure(EntityTypeBuilder<EmployeeDocument> builder)
    {
        builder.ToTable("employee_documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Number).HasMaxLength(EmployeeDocument.NumberMaxLength);
        builder.Property(d => d.Note).HasMaxLength(EmployeeDocument.NoteMaxLength);
        builder.HasOne<Employee>().WithMany().HasForeignKey(d => d.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<DocumentType>().WithMany().HasForeignKey(d => d.TypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => d.EmployeeId);

        // The nightly reminder reads the documents that expire
        builder.HasIndex(d => d.ValidUntil).HasFilter("valid_until IS NOT NULL AND NOT is_deleted");
    }
}
