using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Security;
using Merp.Infrastructure.MultiTenancy;
using Merp.Infrastructure.MultiTenancy.Catalog;
using Merp.Infrastructure.Persistence;
using Merp.Testing.Containers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Merp.Testing.Web;

/// <summary>
/// The real API host in the <c>Testing</c> environment, with its own catalog database on the shared PostgreSQL
/// container and header based test authentication (<see cref="TestAuthHandler"/>).
/// </summary>
public class MerpWebApplicationFactory(PostgresFixture postgres) : WebApplicationFactory<Program>, IAsyncLifetime
{
    private string _catalogConnectionString = null!;

    public PostgresFixture Postgres => postgres;

    /// <summary>Clock of the host when <see cref="UseFakeTime"/> is true.</summary>
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero));

    /// <summary>Replace the system clock with <see cref="Time"/>.</summary>
    protected virtual bool UseFakeTime => false;

    public virtual async ValueTask InitializeAsync()
    {
        _catalogConnectionString = await postgres.CreateDatabaseAsync();
        await Services.GetRequiredService<TenantCatalog>().MigrateAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Creates a tenant database, registers it in the catalog and applies all module migrations.</summary>
    public async Task<TenantInfo> CreateTenantAsync(string code, bool active = true)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var catalog = Services.GetRequiredService<TenantCatalog>();
        await catalog.EnsureTenantAsync(code, code.ToUpperInvariant(), connectionString, "tr-TR", TestContext.Current.CancellationToken);

        var tenant = (await Services.GetRequiredService<ITenantStore>().FindByCodeAsync(code, TestContext.Current.CancellationToken))!;
        await Services.GetRequiredService<TenantDatabaseMigrator>().MigrateAsync(tenant.ToTenantInfo(), tenant.ConnectionString, TestContext.Current.CancellationToken);

        if (!active)
        {
            await catalog.SetStatusAsync(code, TenantStatus.Inactive, TestContext.Current.CancellationToken);
        }

        return tenant.ToTenantInfo();
    }

    /// <summary>HTTP client authenticated as <paramref name="subject"/> with the given tenant claim and permissions.</summary>
    public HttpClient CreateClientFor(string subject, string? tenantCode, params string[] permissions)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubjectHeader, subject);
        if (tenantCode is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantCode);
        }

        if (permissions.Length > 0)
        {
            client.DefaultRequestHeaders.Add(TestPermissionChecker.PermissionsHeader, string.Join(',', permissions));
        }

        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Catalog", _catalogConnectionString);
        // Only PostgreSQL runs in integration tests; other dependencies are opt-in per test class.
        builder.UseSetting("HealthChecks:Keycloak:Enabled", "false");
        builder.UseSetting("Jobs:EnableServer", "false");
        builder.UseSetting("Messaging:EnableBackgroundProcessing", "false");
        builder.UseSetting("ApiDocs:Enabled", "true");
        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            services.AddScoped<IPermissionChecker, TestPermissionChecker>();
            if (UseFakeTime)
            {
                services.AddSingleton<TimeProvider>(Time);
            }

            ConfigureServices(services);
        });
    }

    /// <summary>Configuration overrides for derived factories.</summary>
    protected virtual IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>();

    /// <summary>Extra registrations for derived factories, e.g. a test module with test-only endpoints.</summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
