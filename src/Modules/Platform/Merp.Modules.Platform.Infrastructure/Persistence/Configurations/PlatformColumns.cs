using Merp.Application.Abstractions.Persistence;
using Merp.SharedKernel.Text;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merp.Modules.Platform.Infrastructure.Persistence.Configurations;

internal static class PlatformColumns
{
    /// <summary>ICU Turkish collation, so names sort C, Ç, D… and I, İ correctly (PostgreSQL built with ICU).</summary>
    public const string TurkishCollation = "tr-x-icu";

    /// <summary>Stored, folded search column over the given SQL expressions (see <see cref="TurkishText.SqlFold"/>).</summary>
    public static void HasSearchText<T>(this EntityTypeBuilder<T> builder, params string[] columns)
        where T : class
    {
        var concatenated = string.Join(" || ' ' || ", columns.Select(c => $"coalesce({c}, '')"));
        builder.Property<string>(ShadowProperties.SearchText)
            .HasColumnName("search_text")
            .HasComputedColumnSql(TurkishText.SqlFold(concatenated), stored: true);
    }

    public static void HasAddress<T>(this EntityTypeBuilder<T> builder, System.Linq.Expressions.Expression<Func<T, Address?>> address)
        where T : class =>
        builder.ComplexProperty(address, a =>
        {
            a.Property(x => x.Line1).HasColumnName("address_line1").HasMaxLength(Address.LineMaxLength);
            a.Property(x => x.Line2).HasColumnName("address_line2").HasMaxLength(Address.LineMaxLength);
            a.Property(x => x.District).HasColumnName("address_district").HasMaxLength(Address.NameMaxLength);
            a.Property(x => x.City).HasColumnName("address_city").HasMaxLength(Address.NameMaxLength).UseCollation(TurkishCollation);
            a.Property(x => x.PostalCode).HasColumnName("address_postal_code").HasMaxLength(10);
            a.Property(x => x.CountryCode).HasColumnName("address_country_code").HasMaxLength(2);
        });
}
