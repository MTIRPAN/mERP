using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Merp.Testing.Containers;

/// <summary>Mailpit, the SMTP server of development (docker-compose), for the e-mail tests (PLAN P8).</summary>
public sealed class MailpitFixture : IAsyncLifetime
{
    private const int SmtpPort = 1025;
    private const int ApiPort = 8025;

    private readonly IContainer _container = new ContainerBuilder("axllent/mailpit:v1.31.1")
        .WithName(ContainerNames.Next("mailpit"))
        .WithPortBinding(SmtpPort, assignRandomHostPort: true)
        .WithPortBinding(ApiPort, assignRandomHostPort: true)
        .WithEnvironment("MP_SMTP_AUTH_ACCEPT_ANY", "true")
        .WithEnvironment("MP_SMTP_AUTH_ALLOW_INSECURE", "true")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(ApiPort).ForPath("/readyz")))
        .Build();

    private HttpClient? _api;

    public string SmtpHost => _container.Hostname;

    public int SmtpPortNumber => _container.GetMappedPublicPort(SmtpPort);

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        _api = new HttpClient { BaseAddress = new Uri($"http://{_container.Hostname}:{_container.GetMappedPublicPort(ApiPort)}") };
    }

    /// <summary>Messages sent to <paramref name="address"/>, newest first.</summary>
    public async Task<IReadOnlyList<MailpitMessage>> MessagesToAsync(string address, CancellationToken cancellationToken)
    {
        var result = await _api!.GetFromJsonAsync<MailpitSearch>($"/api/v1/search?query={Uri.EscapeDataString($"to:\"{address}\"")}", cancellationToken);
        return result?.Messages ?? [];
    }

    /// <summary>Waits until a message to <paramref name="address"/> arrives.</summary>
    public async Task<MailpitMessage> WaitForMessageToAsync(string address, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        while (true)
        {
            var messages = await MessagesToAsync(address, deadline.Token);
            if (messages.Count > 0)
            {
                return messages[0];
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), deadline.Token);
        }
    }

    /// <summary>The HTML body of a message.</summary>
    public async Task<string> HtmlAsync(string id, CancellationToken cancellationToken) =>
        (await _api!.GetFromJsonAsync<MailpitDetails>($"/api/v1/message/{id}", cancellationToken))!.Html;

    public async ValueTask DisposeAsync()
    {
        _api?.Dispose();
        await _container.DisposeAsync();
    }

    private sealed record MailpitSearch([property: JsonPropertyName("messages")] IReadOnlyList<MailpitMessage> Messages);

    private sealed record MailpitDetails([property: JsonPropertyName("HTML")] string Html);
}

public sealed record MailpitMessage(
    [property: JsonPropertyName("ID")] string Id,
    [property: JsonPropertyName("Subject")] string Subject);
