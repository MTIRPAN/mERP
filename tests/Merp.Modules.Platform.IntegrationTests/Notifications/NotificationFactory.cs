using Merp.Infrastructure.MultiTenancy;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Infrastructure.Persistence;
using Merp.Testing.Containers;
using Merp.Testing.Web;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Platform.IntegrationTests.Notifications;

/// <summary>
/// The host with integration events processed in the background (as in production) and e-mail sent to Mailpit, so
/// a notification travels the whole way: outbox → inbox → bell, SignalR and SMTP (PLAN P8).
/// </summary>
public sealed class NotificationFactory(PostgresFixture postgres) : MerpWebApplicationFactory(postgres)
{
    public MailpitFixture Mailpit { get; } = new();

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Messaging:EnableBackgroundProcessing"] = "true",
        ["Messaging:PollingInterval"] = "00:00:00.200",
        ["Email:Host"] = Mailpit.SmtpHost,
        ["Email:Port"] = Mailpit.SmtpPortNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Email:Security"] = "None",
        ["Notifications:WebBaseUrl"] = "https://erp.acme.test",
    };

    public override async ValueTask InitializeAsync()
    {
        // The SMTP address goes into the host's settings, so Mailpit runs before the host starts
        await Mailpit.InitializeAsync();
        await base.InitializeAsync();
        await CreateTenantAsync("acme");
        await CreateTenantAsync("globex");
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await Mailpit.DisposeAsync();
    }

    /// <summary>A signed-in user of acme with an e-mail address in the token.</summary>
    public HttpClient User(string subject, string? email = null, params string[] permissions)
    {
        var client = CreateClientFor(subject, "acme", permissions);
        if (email is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, email);
        }

        return client;
    }

    /// <summary>An administrator of e-mail templates and companies.</summary>
    public HttpClient Administrator() =>
        CreateClientFor(
            "notification-admin",
            "acme",
            PlatformPermissions.NotificationTemplates.Read,
            PlatformPermissions.NotificationTemplates.Manage,
            PlatformPermissions.Companies.Read,
            PlatformPermissions.Companies.Manage);

    /// <summary>Audited fields of a notification template in acme.</summary>
    public async Task<List<string?>> AuditedFieldsAsync(Guid templateId)
    {
        var tenant = (await Services.GetRequiredService<ITenantStore>().FindByCodeAsync("acme", TestContext.Current.CancellationToken))!;
        await using var scope = Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        return await db.AuditEntries.AsNoTracking()
            .Where(e => e.EntityType == "NotificationTemplate" && e.EntityId == templateId.ToString())
            .Select(e => e.Field)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The user's connection to <c>/hubs/notifications</c> (long polling through the test server). It carries the same
    /// token claims as <see cref="User"/>: when it is the user's first request, it creates the user.
    /// </summary>
    public async Task<HubConnection> ConnectAsync(string subject, string? email = null)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(Server.BaseAddress, "hubs/notifications"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => Server.CreateHandler();
                options.Headers[TestAuthHandler.SubjectHeader] = subject;
                options.Headers[TestAuthHandler.TenantHeader] = "acme";
                if (email is not null)
                {
                    options.Headers[TestAuthHandler.EmailHeader] = email;
                }
            })
            .Build();
        await connection.StartAsync(TestContext.Current.CancellationToken);
        return connection;
    }
}
