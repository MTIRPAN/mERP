using System.Security.Cryptography.X509Certificates;
using Merp.Application.Abstractions.Jobs;
using Merp.Infrastructure.MultiTenancy.Catalog;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Merp.Infrastructure.MultiTenancy;

public static class MultiTenancyServiceCollectionExtensions
{
    public const string CatalogConnectionStringName = "Catalog";

    /// <summary>Tenant catalog, encrypted connection strings, tenant cache and the tenant job runner (D3).</summary>
    public static IServiceCollection AddMerpMultiTenancy(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContextFactory<CatalogDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString(CatalogConnectionStringName)
                ?? throw new InvalidOperationException($"ConnectionStrings:{CatalogConnectionStringName} is not configured.");
            CatalogDbContext.Configure(options, connectionString);
        });

        // The key ring lives in the catalog so that every instance can decrypt tenant connection strings.
        // In production the keys themselves must be encrypted with a certificate (DataProtection:CertificatePath).
        var dataProtection = services.AddDataProtection()
            .SetApplicationName("mERP")
            .PersistKeysToDbContext<CatalogDbContext>();

        var certificatePath = configuration["DataProtection:CertificatePath"];
        if (!string.IsNullOrWhiteSpace(certificatePath))
        {
            dataProtection.ProtectKeysWithCertificate(
                X509CertificateLoader.LoadPkcs12FromFile(certificatePath, configuration["DataProtection:CertificatePassword"]));
        }

        services.AddHybridCache();
        services.TryAddSingleton<TenantConnectionStringProtector>();
        services.TryAddSingleton<ITenantStore, CatalogTenantStore>();
        services.TryAddSingleton<TenantCatalog>();
        services.TryAddSingleton<TenantScopeFactory>();
        services.TryAddSingleton<ITenantJobRunner, TenantJobRunner>();
        return services;
    }
}
