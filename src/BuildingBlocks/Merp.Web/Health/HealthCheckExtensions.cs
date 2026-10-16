using System.Text.Json;
using Merp.Infrastructure.Caching;
using Merp.Infrastructure.Messaging.Integration;
using Merp.Infrastructure.Messaging.RabbitMq;
using Merp.Infrastructure.MultiTenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Merp.Web.Health;

public static class HealthCheckExtensions
{
    public const string ReadyTag = "ready";

    /// <summary>
    /// Readiness checks for the infrastructure the API depends on: PostgreSQL (catalog), Valkey, RabbitMQ (when it is
    /// the transport), Keycloak and Gotenberg (degraded only). Each can be turned off with
    /// <c>HealthChecks:{Name}:Enabled = false</c>.
    /// </summary>
    public static IServiceCollection AddMerpHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var checks = services.AddHealthChecks();
        bool Enabled(string name) => configuration.GetValue($"HealthChecks:{name}:Enabled", true);

        var catalog = configuration.GetConnectionString(MultiTenancyServiceCollectionExtensions.CatalogConnectionStringName);
        if (Enabled("Postgres") && !string.IsNullOrWhiteSpace(catalog))
        {
            checks.AddNpgSql(catalog, name: "postgres", tags: [ReadyTag], timeout: TimeSpan.FromSeconds(5));
        }

        var valkey = configuration.GetConnectionString(CachingServiceCollectionExtensions.ValkeyConnectionStringName);
        if (Enabled("Valkey") && !string.IsNullOrWhiteSpace(valkey))
        {
            checks.AddRedis(valkey, name: "valkey", tags: [ReadyTag], timeout: TimeSpan.FromSeconds(5));
        }

        var transport = configuration.GetValue($"{MessagingOptions.SectionName}:Transport", MessagingTransport.InProcess);
        if (Enabled("RabbitMQ") && transport == MessagingTransport.RabbitMQ)
        {
            checks.AddRabbitMQ(
                sp => sp.GetRequiredService<RabbitMqConnectionProvider>().GetConnectionAsync(CancellationToken.None),
                name: "rabbitmq",
                tags: [ReadyTag],
                timeout: TimeSpan.FromSeconds(5));
        }

        var authority = configuration["Authentication:Authority"];
        if (Enabled("Keycloak") && !string.IsNullOrWhiteSpace(authority))
        {
            checks.AddUrlGroup(new Uri(authority.TrimEnd('/') + "/.well-known/openid-configuration"), name: "keycloak", tags: [ReadyTag], timeout: TimeSpan.FromSeconds(5));
        }

        // Only documents need Gotenberg: without it the API still works, so it reports Degraded (D36)
        var gotenberg = configuration["Pdf:GotenbergUrl"];
        if (Enabled("Gotenberg") && !string.IsNullOrWhiteSpace(gotenberg))
        {
            checks.AddUrlGroup(
                new Uri(gotenberg.TrimEnd('/') + "/health"),
                name: "gotenberg",
                failureStatus: HealthStatus.Degraded,
                tags: [ReadyTag],
                timeout: TimeSpan.FromSeconds(5));
        }

        return services;
    }

    /// <summary><c>/health/live</c> (process is up) and <c>/health/ready</c> (dependencies reachable); anonymous (D20).</summary>
    public static IEndpointRouteBuilder MapMerpHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = WriteAsync })
            .AllowAnonymous().DisableRateLimiting();
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains(ReadyTag), ResponseWriter = WriteAsync })
            .AllowAnonymous().DisableRateLimiting();
        return endpoints;
    }

    private static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return JsonSerializer.SerializeAsync(context.Response.Body, new
        {
            status = report.Status.ToString(),
            durationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString(), durationMs = e.Value.Duration.TotalMilliseconds, error = e.Value.Exception?.Message }),
        }, cancellationToken: context.RequestAborted);
    }
}
