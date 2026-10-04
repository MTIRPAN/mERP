using Merp.Application.Abstractions.Context;
using Merp.Infrastructure.MultiTenancy;
using Merp.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Merp.Infrastructure.Persistence;

/// <summary>Applies the migrations of every registered module to one tenant database.</summary>
public sealed partial class TenantDatabaseMigrator(
    IServiceScopeFactory scopeFactory,
    IEnumerable<ModuleDatabase> modules,
    ILogger<TenantDatabaseMigrator> logger)
{
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
