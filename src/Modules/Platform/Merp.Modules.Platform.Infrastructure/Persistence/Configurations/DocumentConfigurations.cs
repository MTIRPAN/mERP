using Merp.Modules.Platform.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merp.Modules.Platform.Infrastructure.Persistence.Configurations;

internal sealed class DocumentTemplateConfiguration : IEntityTypeConfiguration<DocumentTemplate>
{
    public void Configure(EntityTypeBuilder<DocumentTemplate> builder)
    {
        builder.ToTable("document_templates");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.DocumentType).HasMaxLength(DocumentTemplate.DocumentTypeMaxLength);
        builder.Property(t => t.Culture).HasMaxLength(20);
        builder.Property(t => t.Body).HasMaxLength(DocumentTemplate.BodyMaxLength);
        builder.Property(t => t.Css).HasMaxLength(DocumentTemplate.CssMaxLength);

        // One template per type, company (or all) and culture; null counts as a value (D36)
        builder.HasIndex(t => new { t.DocumentType, t.CompanyId, t.Culture }).IsUnique().AreNullsDistinct(false);
    }
}
