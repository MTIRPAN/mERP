using Testcontainers.Redis;
using Xunit;

namespace Merp.Testing.Containers;

/// <summary>Valkey 8 container (Redis protocol) for cache tests.</summary>
public sealed class ValkeyFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("valkey/valkey:8").WithName(ContainerNames.Next("valkey")).Build();

    public string ConnectionString => _container.GetConnectionString();

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
