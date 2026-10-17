using Merp.Infrastructure.Persistence.Conventions;
using Merp.Modules.Platform.Domain.Imports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merp.Modules.Platform.Infrastructure.Persistence.Configurations;

internal sealed class ImportJobConfiguration : IEntityTypeConfiguration<ImportJob>
{
    public void Configure(EntityTypeBuilder<ImportJob> builder)
    {
        builder.ToTable("import_jobs");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).ValueGeneratedNever();
        builder.Property(j => j.ImportKey).HasMaxLength(ImportJob.KeyMaxLength);
        builder.Property(j => j.Culture).HasMaxLength(20);
        builder.Property(j => j.FileName).HasMaxLength(ImportJob.FileNameMaxLength);
        builder.Property(j => j.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(j => j.Errors).HasJson();

        // A user's latest jobs of an import
        builder.HasIndex(j => new { j.UserId, j.ImportKey, j.CreatedAt });
    }
}
