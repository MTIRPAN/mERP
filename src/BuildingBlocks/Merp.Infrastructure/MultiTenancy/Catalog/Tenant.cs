using System.Text.RegularExpressions;

namespace Merp.Infrastructure.MultiTenancy.Catalog;

/// <summary>Customer with its own database (D3). Lives in the catalog database, not in a tenant database.</summary>
public sealed partial class Tenant
{
    private Tenant()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Lower case code used in tokens (<c>tenant</c> claim) and public URLs.</summary>
    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    /// <summary>Connection string of the tenant database, encrypted with ASP.NET Data Protection.</summary>
    public string ProtectedConnectionString { get; private set; } = null!;

    public TenantStatus Status { get; private set; }

    public string DefaultCulture { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public static Tenant Create(string code, string name, string protectedConnectionString, string defaultCulture, DateTimeOffset createdAt)
    {
        var normalized = NormalizeCode(code);
        if (!IsValidCode(normalized))
        {
            throw new ArgumentException($"'{code}' is not a valid tenant code (2-32 characters: a-z, 0-9, '-').", nameof(code));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedConnectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultCulture);

        return new Tenant
        {
            Id = Guid.CreateVersion7(),
            Code = normalized,
            Name = name.Trim(),
            ProtectedConnectionString = protectedConnectionString,
            Status = TenantStatus.Active,
            DefaultCulture = defaultCulture,
            CreatedAt = createdAt,
        };
    }

    public static string NormalizeCode(string code) => code.Trim().ToLowerInvariant();

    public static bool IsValidCode(string code) => CodePattern().IsMatch(code);

    public void Deactivate() => Status = TenantStatus.Inactive;

    public void Activate() => Status = TenantStatus.Active;

    public void ChangeConnectionString(string protectedConnectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedConnectionString);
        ProtectedConnectionString = protectedConnectionString;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}

public enum TenantStatus
{
    Active,
    Inactive,
}
