using System.ComponentModel;
using System.Text.Json;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Settings;
using Merp.Infrastructure.Persistence.Conventions;
using Merp.Infrastructure.Settings;
using Merp.Modules.Platform.Application.Settings;
using Merp.Modules.Platform.Domain.Settings;
using Merp.Modules.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Caching.Hybrid;

namespace Merp.Modules.Platform.Infrastructure.Settings;

internal sealed class SettingsProfileConfiguration : IEntityTypeConfiguration<SettingsProfile>
{
    public void Configure(EntityTypeBuilder<SettingsProfile> builder)
    {
        builder.ToTable("settings_profiles");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Values).HasJson();

        // One profile for the tenant (no company) and one per company
        builder.HasIndex(p => p.CompanyId).IsUnique().AreNullsDistinct(false);
    }
}

/// <summary>The setting values of a tenant, cached as one entry: a few profiles of a few keys each.</summary>
[ImmutableObject(true)]
public sealed record SettingsSnapshot(IReadOnlyDictionary<string, string> Tenant, IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, string>> Companies);

/// <summary>
/// <see cref="ISettings"/> with the tenant's values (D35): the company's value, otherwise the tenant's, otherwise
/// the default. Cached per tenant until a change.
/// </summary>
internal sealed class PlatformSettings(PlatformDbContext db, HybridCache cache, ITenantContext tenant, SettingDefinitionRegistry registry) : ISettings
{
    private static readonly TimeSpan Duration = TimeSpan.FromHours(1);

    public async Task<T> GetAsync<T>(string key, Guid? companyId = null, CancellationToken cancellationToken = default)
    {
        var definition = registry.Get(key);
        var tenantId = tenant.RequiredTenant.Id;
        var snapshot = await cache.GetOrCreateAsync(
            SettingsCache.Key(tenantId),
            db,
            static async (context, ct) =>
            {
                var profiles = await context.SettingsProfiles.AsNoTracking().ToListAsync(ct);
                return new SettingsSnapshot(
                    profiles.SingleOrDefault(p => p.CompanyId is null)?.Values ?? new Dictionary<string, string>(),
                    profiles.Where(p => p.CompanyId is not null).ToDictionary(p => p.CompanyId!.Value, p => p.Values));
            },
            new HybridCacheEntryOptions { Expiration = Duration, LocalCacheExpiration = Duration },
            [SettingsCache.Tag(tenantId)],
            cancellationToken);

        var json = (definition.CompanyOverridable && companyId is { } id && snapshot.Companies.TryGetValue(id, out var company) && company.TryGetValue(key, out var own)
                ? own
                : null)
            ?? (snapshot.Tenant.TryGetValue(key, out var shared) ? shared : null);
        return json is null ? SettingValues.Read<T>(definition.DefaultValue) : SettingValues.Read<T>(JsonDocument.Parse(json).RootElement);
    }
}

internal sealed class SettingsCache(HybridCache cache, ITenantContext tenant) : ISettingsCache
{
    public static string Key(Guid tenantId) => $"merp:{tenantId}:settings";

    public static string Tag(Guid tenantId) => $"{tenantId}:settings";

    public async Task InvalidateAsync(CancellationToken cancellationToken = default) =>
        await cache.RemoveByTagAsync(Tag(tenant.RequiredTenant.Id), cancellationToken);
}
