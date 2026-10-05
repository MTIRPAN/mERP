using Merp.SharedKernel.Domain;

namespace Merp.Modules.Platform.Domain.Locations;

/// <summary>
/// Province of Türkiye (il). The id is the plate number (1–81). Reference data seeded by migration; source and
/// license in <c>docs/reference-data.md</c>.
/// </summary>
public sealed class Province : Entity<int>
{
    public const int MaxCode = 81;

    private Province()
    {
    }

    public Province(int code, string name)
        : base(code) => Name = name;

    public string Name { get; private set; } = null!;
}

/// <summary>District of Türkiye (ilçe). The id is the district code of the civil registry (NVİ ilçe kodu).</summary>
public sealed class District : Entity<int>
{
    private District()
    {
    }

    public District(int code, int provinceId, string name)
        : base(code)
    {
        ProvinceId = provinceId;
        Name = name;
    }

    public int ProvinceId { get; private set; }

    public string Name { get; private set; } = null!;
}
