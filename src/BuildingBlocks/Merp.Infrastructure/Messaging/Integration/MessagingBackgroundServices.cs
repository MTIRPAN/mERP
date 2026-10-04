using Merp.Infrastructure.MultiTenancy;
using Merp.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Merp.Infrastructure.Messaging.Integration;

/// <summary>Polls every module of every active tenant at <see cref="MessagingOptions.PollingInterval"/>.</summary>
public abstract partial class TenantModulePollingService(
    ITenantStore tenantStore,
    IEnumerable<ModuleDatabase> modules,
    IOptions<MessagingOptions> options,
    TimeProvider timeProvider,
    ILogger logger) : BackgroundService
{
    private readonly ModuleDatabase[] _modules = [.. modules];

    protected abstract string Name { get; }

    protected abstract Task<int> ProcessAsync(TenantRecord tenant, ModuleDatabase module, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.EnableBackgroundProcessing || _modules.Length == 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.PollingInterval, timeProvider);
        do
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogPollFailed(logger, exception, Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        foreach (var tenant in await tenantStore.GetActiveTenantsAsync(cancellationToken))
        {
            foreach (var module in _modules)
            {
                try
                {
                    await ProcessAsync(tenant, module, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogModuleFailed(logger, exception, Name, tenant.Code, module.ModuleName);
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "{Service} polling failed")]
    private static partial void LogPollFailed(ILogger logger, Exception exception, string service);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Service} failed for tenant {TenantCode}, module {Module}")]
    private static partial void LogModuleFailed(ILogger logger, Exception exception, string service, string tenantCode, string module);
}

public sealed class OutboxDispatcherService(
    OutboxDispatcher dispatcher,
    TenantScopeFactory scopeFactory,
    ITenantStore tenantStore,
    IEnumerable<ModuleDatabase> modules,
    IOptions<MessagingOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcherService> logger)
    : TenantModulePollingService(tenantStore, modules, options, timeProvider, logger)
{
    protected override string Name => "Outbox dispatcher";

    protected override async Task<int> ProcessAsync(TenantRecord tenant, ModuleDatabase module, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateScope(tenant);
        return await dispatcher.DispatchAsync(scope.ServiceProvider, module, cancellationToken);
    }
}

public sealed class InboxProcessorService(
    InboxProcessor processor,
    ITenantStore tenantStore,
    IEnumerable<ModuleDatabase> modules,
    IOptions<MessagingOptions> options,
    TimeProvider timeProvider,
    ILogger<InboxProcessorService> logger)
    : TenantModulePollingService(tenantStore, modules, options, timeProvider, logger)
{
    protected override string Name => "Inbox processor";

    protected override Task<int> ProcessAsync(TenantRecord tenant, ModuleDatabase module, CancellationToken cancellationToken) =>
        processor.ProcessAsync(tenant, module, cancellationToken);
}
