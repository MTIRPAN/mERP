using System.Data.Common;
using Merp.Infrastructure.MultiTenancy;
using Merp.Infrastructure.MultiTenancy.Catalog;
using Merp.Testing.Containers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.BuildingBlocks.IntegrationTests.MultiTenancy;

public sealed class CatalogTenantStoreTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReadStartedBeforeANewTenant_DoesNotHideTheTenantAfterInvalidation()
    {
        var gate = new QueryGate();
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        CatalogDbContext.Configure(options, await postgres.CreateDatabaseAsync()).AddInterceptors(gate);
        var factory = new CatalogContextFactory(options.Options);
        var protector = new TenantConnectionStringProtector(new EphemeralDataProtectionProvider());
        await using var services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        var store = new CatalogTenantStore(factory, protector, services.GetRequiredService<HybridCache>());
        var catalog = new TenantCatalog(factory, protector, store, TimeProvider.System);
        await catalog.MigrateAsync(Ct);

        // A background reader (outbox polling) loads the tenant list and is held after its query ran ...
        gate.HoldNextQuery();
        var staleRead = store.GetActiveTenantsAsync(Ct);
        await gate.QueryExecuted.WaitAsync(Ct);

        // ... while a tenant is added and the cache is cleared ...
        await catalog.EnsureTenantAsync("race", "RACE", "Host=localhost;Database=race", "tr-TR", Ct);

        // ... then it finishes and stores the list without the new tenant.
        gate.Release();
        (await staleRead).ShouldBeEmpty();

        var tenant = await store.FindByCodeAsync("race", Ct);

        tenant.ShouldNotBeNull();
        tenant.ConnectionString.ShouldBe("Host=localhost;Database=race");
    }

    private sealed class CatalogContextFactory(DbContextOptions<CatalogDbContext> options) : IDbContextFactory<CatalogDbContext>
    {
        public CatalogDbContext CreateDbContext() => new(options);
    }

    /// <summary>Holds one query after the database ran it, before its rows are read.</summary>
    private sealed class QueryGate : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _executed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _armed;

        public Task QueryExecuted => _executed.Task;

        public void HoldNextQuery() => Interlocked.Exchange(ref _armed, 1);

        public void Release() => _released.TrySetResult();

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _armed, 0) == 1)
            {
                _executed.TrySetResult();
                await _released.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }
}
