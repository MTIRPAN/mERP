using Merp.Infrastructure.MultiTenancy;
using Merp.Infrastructure.Persistence;

namespace Merp.Api.Development;

/// <summary>Development only: migrates the catalog, registers the <c>demo</c> tenant and migrates tenant databases.</summary>
internal static class DevelopmentSetup
{
    public const string DemoTenantCode = "demo";

    public static async Task RunAsync(IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var catalog = services.GetRequiredService<TenantCatalog>();
        await catalog.MigrateAsync(cancellationToken);

        var demoConnectionString = configuration.GetConnectionString("DemoTenant");
        if (!string.IsNullOrWhiteSpace(demoConnectionString))
        {
            await catalog.EnsureTenantAsync(DemoTenantCode, "Demo Holding", demoConnectionString, "tr-TR", cancellationToken);
        }

        await services.GetRequiredService<TenantDatabaseMigrator>().MigrateTenantsAsync(tenantCode: null, cancellationToken);
    }
}
