using System.Globalization;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Localization;
using Merp.Infrastructure.Persistence;
using Merp.Modules.Platform.Application.Localization;
using Merp.Modules.Platform.Domain.Localization;
using Merp.Modules.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Platform.Infrastructure.Localization;

/// <summary>
/// The tenant's active cultures and translations from <c>cultures</c> and <c>localization_texts</c> (D11, D28),
/// cached per tenant until a culture or text changes. Requests without a tenant get the embedded texts only.
/// </summary>
internal sealed class PlatformLocalizationSource(PlatformDbContext db, HybridCache cache, ITenantContext tenant) : ILocalizationSource
{
    private static readonly TimeSpan Duration = TimeSpan.FromHours(1);

    public async Task<LocalizationSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        if (tenant.Tenant is not { } current)
        {
            return LocalizationSnapshot.Embedded;
        }

        return await cache.GetOrCreateAsync(
            LocalizationCache.Key(current.Id),
            this,
            static (source, ct) => new ValueTask<LocalizationSnapshot>(source.LoadAsync(ct)),
            new HybridCacheEntryOptions { Expiration = Duration, LocalCacheExpiration = Duration },
            [LocalizationCache.Tag(current.Id)],
            cancellationToken);
    }

    private async Task<LocalizationSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        var cultures = await db.Cultures.AsNoTracking().Where(c => c.IsActive)
            .OrderByDescending(c => c.IsDefault).ThenBy(c => c.Code)
            .Select(c => new { c.Id, c.Code, c.Name, c.FallbackCode, c.IsDefault })
            .ToListAsync(cancellationToken);
        if (cultures.Count == 0)
        {
            // Not seeded yet (the seeder runs after migrations)
            return LocalizationSnapshot.Embedded;
        }

        var codes = cultures.ToDictionary(c => c.Id, c => c.Code);
        var ids = codes.Keys.ToList();
        var texts = (await db.Set<CultureText>().AsNoTracking().Where(t => ids.Contains(t.CultureId)).ToListAsync(cancellationToken))
            .GroupBy(t => codes[t.CultureId], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<string, string>)g.ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal),
                StringComparer.OrdinalIgnoreCase);

        return new LocalizationSnapshot(
            cultures.FirstOrDefault(c => c.IsDefault)?.Code ?? LocalizationSnapshot.EmbeddedCulture,
            [.. cultures.Select(c => new CultureDefinition(c.Code, c.Name, c.FallbackCode))],
            texts);
    }
}

internal sealed class LocalizationCache(HybridCache cache, ITenantContext tenant) : ILocalizationCache
{
    public static string Key(Guid tenantId) => $"merp:{tenantId}:localization";

    public static string Tag(Guid tenantId) => $"{tenantId}:localization";

    public async Task InvalidateAsync(CancellationToken cancellationToken = default) =>
        await cache.RemoveByTagAsync(Tag(tenant.RequiredTenant.Id), cancellationToken);
}

/// <summary>Seeds the embedded culture (tr-TR) as the active default when the tenant has no culture yet (PLAN P4).</summary>
internal sealed class LocalizationSeeder : ITenantDataSeeder
{
    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<PlatformDbContext>();
        if (await db.Cultures.AnyAsync(cancellationToken))
        {
            return;
        }

        var code = LocalizationSnapshot.EmbeddedCulture;
        db.Cultures.Add(Culture.Create(code, CultureInfo.GetCultureInfo(code).NativeName, null, isDefault: true));
        await db.SaveChangesAsync(cancellationToken);
    }
}
