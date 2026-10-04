using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.BuildingBlocks.IntegrationTests.Messaging.Orders;
using Merp.BuildingBlocks.IntegrationTests.Messaging.Shipping;
using Merp.Infrastructure;
using Merp.Infrastructure.Messaging;
using Merp.Infrastructure.Messaging.Integration;
using Merp.Infrastructure.MultiTenancy;
using Merp.Infrastructure.MultiTenancy.Catalog;
using Merp.Infrastructure.Persistence;
using Merp.SharedKernel.Results;
using Merp.Testing.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Merp.BuildingBlocks.IntegrationTests.Messaging;

/// <summary>Host-like service provider with the Orders and Shipping test modules in one tenant database.</summary>
public sealed class MessagingHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private MessagingHarness(ServiceProvider provider, TenantRecord tenant, FakeTimeProvider time)
    {
        _provider = provider;
        Tenant = tenant;
        Time = time;
    }

    public TenantRecord Tenant { get; }

    public FakeTimeProvider Time { get; }

    public IServiceProvider Services => _provider;

    public ModuleDatabase OrdersModule => Module<OrdersDbContext>();

    public ModuleDatabase ShippingModule => Module<ShippingDbContext>();

    public static async Task<MessagingHarness> CreateAsync(PostgresFixture postgres, Uri? rabbitMq = null, int maxAttempts = 10)
    {
        var tenant = new TenantRecord(Guid.CreateVersion7(), "msg", "Messaging", "tr-TR", TenantStatus.Active, await postgres.CreateDatabaseAsync());
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 5, 4, 8, 0, 0, TimeSpan.Zero));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:Transport"] = rabbitMq is null ? "InProcess" : "RabbitMQ",
            ["Messaging:RabbitMq:Uri"] = rabbitMq?.ToString(),
            ["Messaging:MaxAttempts"] = maxAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Messaging:EnableBackgroundProcessing"] = "false",
        }).Build();

        var tenantStore = Substitute.For<ITenantStore>();
        tenantStore.FindByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        tenantStore.GetActiveTenantsAsync(Arg.Any<CancellationToken>()).Returns([tenant]);
        var permissions = Substitute.For<IPermissionChecker>();
        permissions.HasPermissionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(time);
        services.AddMerpInfrastructure();
        services.AddMerpMessaging(configuration);
        services.AddSingleton(tenantStore);
        services.AddSingleton<TenantScopeFactory>();
        services.AddSingleton(permissions);

        var assembly = typeof(MessagingHarness).Assembly;
        services.AddModuleDbContext<OrdersDbContext>(assembly, typeof(OrdersDbContext).Namespace);
        services.AddRequestHandlersFromAssembly(assembly, typeof(OrdersDbContext).Namespace);
        services.AddModuleDbContext<ShippingDbContext>(assembly, typeof(ShippingDbContext).Namespace);
        services.AddRequestHandlersFromAssembly(assembly, typeof(ShippingDbContext).Namespace);

        var harness = new MessagingHarness(services.BuildServiceProvider(), tenant, time);
        await using var scope = harness.CreateScope();
        // Both modules share the tenant database; create each schema from its own model.
        foreach (var context in new DbContext[] { scope.ServiceProvider.GetRequiredService<OrdersDbContext>(), scope.ServiceProvider.GetRequiredService<ShippingDbContext>() })
        {
            await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        }

        return harness;
    }

    public AsyncServiceScope CreateScope() => _provider.GetRequiredService<TenantScopeFactory>().CreateScope(Tenant);

    public async Task<Result<TResponse>> SendAsync<TResponse>(ICommand<TResponse> command)
    {
        await using var scope = CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, TestContext.Current.CancellationToken);
    }

    public async Task<int> DispatchOutboxAsync()
    {
        await using var scope = CreateScope();
        return await _provider.GetRequiredService<OutboxDispatcher>().DispatchAsync(scope.ServiceProvider, OrdersModule, TestContext.Current.CancellationToken);
    }

    public Task<int> ProcessShippingInboxAsync() =>
        _provider.GetRequiredService<InboxProcessor>().ProcessAsync(Tenant, ShippingModule, TestContext.Current.CancellationToken);

    public async Task<T> QueryAsync<TContext, T>(Func<TContext, Task<T>> query)
        where TContext : DbContext
    {
        await using var scope = CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<TContext>());
    }

    public ValueTask DisposeAsync() => _provider.DisposeAsync();

    private ModuleDatabase Module<TContext>() =>
        _provider.GetServices<ModuleDatabase>().Single(m => m.DbContextType == typeof(TContext));
}
