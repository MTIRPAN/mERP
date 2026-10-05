using Merp.Modules.Platform.Domain.Companies;
using Merp.Modules.Platform.Domain.Locations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merp.Modules.Platform.Infrastructure.Persistence.Configurations;

internal sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("companies");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Code).HasMaxLength(Company.CodeMaxLength);
        builder.HasIndex(c => c.Code).IsUnique();
        builder.Property(c => c.Title).HasMaxLength(Company.TitleMaxLength).UseCollation(PlatformColumns.TurkishCollation);
        builder.Property(c => c.ShortName).HasMaxLength(Company.ShortNameMaxLength).UseCollation(PlatformColumns.TurkishCollation);
        builder.Property(c => c.TaxOffice).HasMaxLength(Company.TaxOfficeMaxLength);
        builder.Property(c => c.DefaultCulture).HasMaxLength(10);
        builder.Property(c => c.TimeZoneId).HasMaxLength(64);
        builder.HasAddress(c => c.Address);
        builder.HasOne<Province>().WithMany().HasForeignKey(c => c.ProvinceCode).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<District>().WithMany().HasForeignKey(c => c.DistrictId).OnDelete(DeleteBehavior.Restrict);
        builder.HasSearchText("code", "title", "short_name", "address_city");
    }
}
