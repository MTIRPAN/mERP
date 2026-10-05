using System.Text.Json;
using Merp.Modules.Platform.Domain.Locations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merp.Modules.Platform.Infrastructure.Persistence.Configurations;

internal sealed class ProvinceConfiguration : IEntityTypeConfiguration<Province>
{
    public void Configure(EntityTypeBuilder<Province> builder)
    {
        builder.ToTable("provinces");
        builder.HasKey(p => p.Code);
        builder.Property(p => p.Code).ValueGeneratedNever();
        builder.Property(p => p.Name).HasMaxLength(50).UseCollation(PlatformColumns.TurkishCollation);
        builder.HasData(LocationSeed.Provinces);
    }
}

internal sealed class DistrictConfiguration : IEntityTypeConfiguration<District>
{
    public void Configure(EntityTypeBuilder<District> builder)
    {
        builder.ToTable("districts");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Name).HasMaxLength(50).UseCollation(PlatformColumns.TurkishCollation);
        builder.HasOne<Province>().WithMany().HasForeignKey(d => d.ProvinceCode).OnDelete(DeleteBehavior.Restrict);
        builder.HasData(LocationSeed.Districts);
    }
}

/// <summary>81 provinces and 973 districts from <c>Persistence/Seed</c> (source and licence: THIRD-PARTY-NOTICES.md there).</summary>
internal static class LocationSeed
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<Province> Provinces { get; } =
        [.. Read<ProvinceRecord>("provinces.json").Select(p => new Province(p.Code, p.Name))];

    public static IReadOnlyList<District> Districts { get; } =
        [.. Read<DistrictRecord>("districts.json").Select(d => new District(d.Id, d.ProvinceCode, d.Name))];

    private static List<T> Read<T>(string file)
    {
        using var stream = typeof(LocationSeed).Assembly.GetManifestResourceStream($"Merp.Modules.Platform.Infrastructure.Persistence.Seed.{file}")
            ?? throw new InvalidOperationException($"Seed file {file} is not embedded.");
        return JsonSerializer.Deserialize<List<T>>(stream, Options) ?? [];
    }

    private sealed record ProvinceRecord(int Code, string Name);

    private sealed record DistrictRecord(int Id, int ProvinceCode, string Name);
}
