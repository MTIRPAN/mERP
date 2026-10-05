using Merp.Modules.Platform.Domain.Branches;
using Merp.Modules.Platform.Domain.Companies;
using Merp.Modules.Platform.Domain.Locations;
using Merp.SharedKernel.ValueObjects;
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
        builder.Property(c => c.Title).HasMaxLength(Company.TitleMaxLength);
        builder.Property(c => c.ShortName).HasMaxLength(Company.ShortNameMaxLength);
        builder.Property(c => c.TaxOffice).HasMaxLength(Company.TaxOfficeMaxLength);
        builder.Property(c => c.TaxNumber).HasMaxLength(Vkn.Length);
        builder.Property(c => c.MersisNumber).HasMaxLength(MersisNumber.Length);
        builder.Property(c => c.SgkWorkplaceNumber).HasMaxLength(SgkWorkplaceNumber.Length);
        builder.Property(c => c.Phone).HasMaxLength(20);
        builder.Property(c => c.Email).HasMaxLength(Email.MaxLength);
        builder.Property(c => c.Culture).HasMaxLength(20);
        builder.Property(c => c.TimeZoneId).HasMaxLength(64);
        builder.OwnsOne(c => c.Address, AddressMapping.Configure);

        builder.HasIndex(c => c.Code).IsUnique();
        builder.HasIndex(c => c.TaxNumber).IsUnique();
    }
}

internal sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("branches");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.Code).HasMaxLength(Branch.CodeMaxLength);
        builder.Property(b => b.Name).HasMaxLength(Branch.NameMaxLength);
        builder.Property(b => b.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(b => b.SgkWorkplaceNumber).HasMaxLength(SgkWorkplaceNumber.Length);
        builder.Property(b => b.Phone).HasMaxLength(20);
        builder.OwnsOne(b => b.Address, AddressMapping.Configure);
        builder.Navigation(b => b.Address).IsRequired();

        builder.HasOne<Company>().WithMany().HasForeignKey(b => b.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(b => new { b.CompanyId, b.Code }).IsUnique();

        // One head office per company; the handlers report it as a field error before this fires.
        builder.HasIndex(b => b.CompanyId)
            .IsUnique()
            .HasFilter($"type = '{nameof(BranchType.HeadOffice)}'")
            .HasDatabaseName("ix_branches_company_id_head_office");
    }
}

internal sealed class ProvinceConfiguration : IEntityTypeConfiguration<Province>
{
    public void Configure(EntityTypeBuilder<Province> builder)
    {
        builder.ToTable("provinces");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Name).HasMaxLength(50);
    }
}

internal sealed class DistrictConfiguration : IEntityTypeConfiguration<District>
{
    public void Configure(EntityTypeBuilder<District> builder)
    {
        builder.ToTable("districts");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Name).HasMaxLength(50);
        builder.HasOne<Province>().WithMany().HasForeignKey(d => d.ProvinceId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Address columns inside the owner's table: <c>address_line1</c>, <c>address_city</c>...</summary>
internal static class AddressMapping
{
    public static void Configure<TOwner>(OwnedNavigationBuilder<TOwner, Address> address)
        where TOwner : class
    {
        address.Property(a => a.Line1).HasMaxLength(Address.LineMaxLength);
        address.Property(a => a.Line2).HasMaxLength(Address.LineMaxLength);
        address.Property(a => a.District).HasMaxLength(Address.NameMaxLength);
        address.Property(a => a.City).HasMaxLength(Address.NameMaxLength);
        address.Property(a => a.PostalCode).HasMaxLength(20);
        address.Property(a => a.CountryCode).HasMaxLength(2);
        address.Property(a => a.ProvinceCode);
        address.Property(a => a.DistrictCode);
        address.HasIndex(a => a.ProvinceCode);
    }
}
