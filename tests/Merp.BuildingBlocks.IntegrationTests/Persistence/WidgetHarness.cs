using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Infrastructure;
using Merp.Infrastructure.Messaging;
using Merp.Infrastructure.MultiTenancy;
using Merp.Infrastructure.Persistence;
using Merp.Infrastructure.Security;
using Merp.Testing.Containers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Merp.BuildingBlocks.IntegrationTests.Persistence;

/// <summary>Service provider wired like a host, with the widget module bound to a fresh test database.</summary>
public sealed class WidgetHarness : IAsyncDisposable
{
    public static readonly TenantInfo Tenant = new(Guid.CreateVersion7(), "test", "Test", "tr-TR");
    public static readonly Guid UserId = Guid.CreateVersion7();

    private readonly ServiceProvider _provider;
    private readonly string _connectionString;

    private WidgetHarness(ServiceProvider provider, string connectionString, FakeTimeProvider time)
    {
        _provider = provider;
        _connectionString = connectionString;
        Time = time;
    }

    public FakeTimeProvider Time { get; }

    public static async Task<WidgetHarness> CreateAsync(PostgresFixture postgres)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var permissions = Substitute.For<IPermissionChecker>();
        permissions.HasPermissionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 15, 9, 30, 0, TimeSpan.Zero));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(time);
        services.AddMerpInfrastructure();
        services.AddModuleDbContext<WidgetDbContext>(typeof(WidgetHarness).Assembly, typeof(WidgetHarness).Namespace);
        services.AddRequestHandlersFromAssembly(typeof(WidgetHarness).Assembly, typeof(WidgetHarness).Namespace);
        services.AddSingleton(permissions);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var harness = new WidgetHarness(provider, connectionString, time);

        await using var scope = harness.CreateScope();
        await scope.ServiceProvider.GetRequiredService<WidgetDbContext>().Database.EnsureCreatedAsync();
        return harness;
    }

    /// <summary>A request scope for the test tenant and user.</summary>
    public AsyncServiceScope CreateScope()
    {
        var scope = _provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(Tenant, _connectionString);
        scope.ServiceProvider.GetRequiredService<CurrentUser>().SetUser(UserId, Tenant.Id);
        return scope;
    }

    public async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request)
        where TResponse : Merp.SharedKernel.Results.Result
    {
        await using var scope = CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, TestContext.Current.CancellationToken);
    }

    public async Task<T> QueryAsync<T>(Func<WidgetDbContext, Task<T>> query)
    {
        await using var scope = CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<WidgetDbContext>());
    }

    public ValueTask DisposeAsync() => _provider.DisposeAsync();
}
