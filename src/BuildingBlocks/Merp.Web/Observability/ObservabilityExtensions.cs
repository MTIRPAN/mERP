using Merp.Infrastructure.Messaging;
using Merp.Infrastructure.Messaging.Integration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Sinks.OpenTelemetry;

namespace Merp.Web.Observability;

public static class ObservabilityExtensions
{
    public const string ServiceName = "merp-api";

    /// <summary>
    /// Serilog for structured logs, OpenTelemetry for traces and metrics; everything is exported over OTLP when
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set (Aspire Dashboard in development, D17).
    /// </summary>
    public static WebApplicationBuilder AddMerpObservability(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

        builder.Services.AddSerilog((services, logger) =>
        {
            logger.ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", ServiceName)
                .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture);

            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
                logger.WriteTo.OpenTelemetry(options =>
                {
                    options.Endpoint = otlpEndpoint;
                    options.Protocol = OtlpProtocol.Grpc;
                    options.ResourceAttributes = new Dictionary<string, object> { ["service.name"] = ServiceName };
                });
            }
        });

        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options => options.Filter = context => !context.Request.Path.StartsWithSegments("/health", StringComparison.Ordinal))
                .AddHttpClientInstrumentation()
                .AddNpgsql()
                .AddSource(MediatorTelemetry.ActivitySourceName, "Merp.Messaging"))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddNpgsqlInstrumentation()
                .AddMeter(MessagingTelemetry.MeterName));

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            telemetry.WithTracing(tracing => tracing.AddOtlpExporter()).WithMetrics(metrics => metrics.AddOtlpExporter());
        }

        return builder;
    }
}
