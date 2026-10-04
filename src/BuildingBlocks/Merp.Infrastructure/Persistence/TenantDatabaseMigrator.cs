using Merp.Application.Abstractions.Context;
using Merp.Infrastructure.MultiTenancy;
using Merp.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Merp.Infrastructure.Persistence;

/// <summary>Applies the migrations of every registered module to tenant databases.</summary>
public sealed partial class TenantDatabaseMigrator(
    IServiceScopeFactory scopeFactory,
    IEnumerable<ModuleDatabase> modules,
    ILogger<TenantDatabaseMigrator> logger)
{
    /// <summary>Migrates every active tenant of the catalog, or only <paramref name="tenantCode"/> when given.</summary>
    public async Task MigrateTenantsAsync(string? tenantCode, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ITenantStore>();

        IReadOnlyList<TenantRecord> tenants;
        if (tenantCode is null)
        {
            tenants = await store.GetActiveTenantsAsync(cancellationToken);
        }
        else
        {
            var tenant = await store.FindByCodeAsync(tenantCode, cancellationToken)
                ?? throw new InvalidOperationException($"Tenant '{tenantCode}' is not in the catalog.");
            tenants = [tenant];
        }

        foreach (var tenant in tenants)
        {
            await MigrateAsync(tenant.ToTenantInfo(), tenant.ConnectionString, cancellationToken);
        }
    }

    public async Task MigrateAsync(TenantInfo tenant, string connectionString, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        foreach (var module in modules)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenant, connectionString);
            scope.ServiceProvider.GetRequiredService<CurrentUser>().SetSystem(tenant.Id);

            var dbContext = (DbContext)scope.ServiceProvider.GetRequiredService(module.DbContextType);
            LogMigrating(logger, tenant.Code, module.Schema);
            await dbContext.Database.MigrateAsync(cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrating tenant {TenantCode}, schema {Schema}")]
    private static partial void LogMigrating(ILogger logger, string tenantCode, string schema);
}
