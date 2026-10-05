namespace Merp.Modules.Platform.Domain.Locations;

/// <summary>One of Turkey's 81 provinces (il). Reference data seeded with migrations; <see cref="Code"/> is the licence plate code.</summary>
public sealed class Province
{
    public Province(int code, string name)
    {
        Code = code;
        Name = name;
    }

    public int Code { get; private set; }

    public string Name { get; private set; }
}

/// <summary>A district (ilçe). Ids are stable: <c>provinceCode * 1000 + n</c>.</summary>
public sealed class District
{
    public District(int id, int provinceCode, string name)
    {
        Id = id;
        ProvinceCode = provinceCode;
        Name = name;
    }

    public int Id { get; private set; }

    public int ProvinceCode { get; private set; }

    public string Name { get; private set; }
}
