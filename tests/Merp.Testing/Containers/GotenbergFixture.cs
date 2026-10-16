using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Merp.Testing.Containers;

/// <summary>
/// Gotenberg, the PDF converter of development (docker-compose), for document tests (PLAN P12, D36). It runs with the
/// same allow list as docker-compose, so a test proves that a page needs nothing from the network.
/// </summary>
public sealed class GotenbergFixture : IAsyncLifetime
{
    private const int Port = 3000;

    private readonly IContainer _container = new ContainerBuilder("gotenberg/gotenberg:8")
        .WithName(ContainerNames.Next("gotenberg"))
        .WithCommand("gotenberg", "--chromium-allow-list=^(file:///tmp/|data:)")
        .WithPortBinding(Port, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(Port).ForPath("/health")))
        .Build();

    public string Url => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(Port)}";

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
