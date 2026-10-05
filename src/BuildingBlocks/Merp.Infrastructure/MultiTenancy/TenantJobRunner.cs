using Merp.Application.Abstractions.Jobs;
using Microsoft.Extensions.Logging;

namespace Merp.Infrastructure.MultiTenancy;

public sealed partial class TenantJobRunner(
    TenantScopeFactory scopeFactory,
    ITenantStore tenantStore,
    ILogger<TenantJobRunner> logger) : ITenantJobRunner
{
    public async Task RunForEachTenantAsync(Func<IServiceProvider, CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        var failures = new List<Exception>();
        foreach (var tenant in await tenantStore.GetActiveTenantsAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await RunAsync(tenant, work, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogTenantFailed(logger, exception, tenant.Code);
                failures.Add(exception);
            }
        }

        if (failures.Count > 0)
        {
            throw new AggregateException("The job failed for one or more tenants.", failures);
        }
    }

    public async Task RunForTenantAsync(Guid tenantId, Func<IServiceProvider, CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        var tenant = await tenantStore.FindByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist.");
        if (!tenant.IsActive)
        {
            LogTenantInactive(logger, tenant.Code);
            return;
        }

        await RunAsync(tenant, work, cancellationToken);
    }

    private async Task RunAsync(TenantRecord tenant, Func<IServiceProvider, CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateScope(tenant);
        await work(scope.ServiceProvider, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Background work failed for tenant {TenantCode}")]
    private static partial void LogTenantFailed(ILogger logger, Exception exception, string tenantCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping background work for inactive tenant {TenantCode}")]
    private static partial void LogTenantInactive(ILogger logger, string tenantCode);
}
