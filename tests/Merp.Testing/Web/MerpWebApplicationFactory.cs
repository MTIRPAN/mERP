using Merp.Application.Abstractions.Context;
using Merp.Infrastructure.MultiTenancy;
using Merp.Infrastructure.MultiTenancy.Catalog;
using Merp.Infrastructure.Persistence;
using Merp.Testing.Containers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>HTTP client authenticated as <paramref name="subject"/> with the given tenant claim.</summary>
    public HttpClient CreateClientFor(string subject, string? tenantCode)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubjectHeader, subject);
        if (tenantCode is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantCode);
        }

        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Catalog", _catalogConnectionString);
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            ConfigureServices(services);
        });
    }

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
