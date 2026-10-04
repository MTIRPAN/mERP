using Testcontainers.RabbitMq;
using Xunit;

namespace Merp.Testing.Containers;

/// <summary>RabbitMQ 4 container for transport tests.</summary>
public sealed class RabbitMqFixture : IAsyncLifetime
{
    private readonly RabbitMqContainer _container = new RabbitMqBuilder("rabbitmq:4-management")
        .WithUsername("merp")
        .WithPassword("merp")
        .Build();

    public Uri Uri => new(_container.GetConnectionString());

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
