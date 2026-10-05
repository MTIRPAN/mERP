using Merp.Modules.Platform.Domain.Branches;
using Merp.Modules.Platform.Domain.Companies;
using Merp.Modules.Platform.Domain.Locations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merp.Modules.Platform.Infrastructure.Persistence.Configurations;

internal sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("branches");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.Code).HasMaxLength(Branch.CodeMaxLength);
        builder.HasIndex(b => new { b.CompanyId, b.Code }).IsUnique();
        builder.Property(b => b.Name).HasMaxLength(Branch.NameMaxLength).UseCollation(PlatformColumns.TurkishCollation);
        builder.Property(b => b.Type).HasConversion<string>().HasMaxLength(20);
        builder.HasAddress(b => b.Address);
        builder.HasOne<Company>().WithMany().HasForeignKey(b => b.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Province>().WithMany().HasForeignKey(b => b.ProvinceCode).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<District>().WithMany().HasForeignKey(b => b.DistrictId).OnDelete(DeleteBehavior.Restrict);
        builder.HasSearchText("code", "name", "address_city", "address_district");
    }
}
