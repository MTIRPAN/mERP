using System.IO.Compression;
using System.Text.Json.Serialization;
using Merp.Infrastructure.Localization;
using Merp.Web.Errors;
using Merp.Web.Health;
using Merp.Web.Jobs;
using Merp.Web.MultiTenancy;
using Merp.Web.OpenApi;
using Merp.Web.RateLimiting;
using Merp.Web.Realtime;
using Merp.Web.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Merp.Web.Hosting;

public static class WebHostExtensions
{
    public const string CorsPolicy = "web";

    /// <summary>Everything the HTTP host needs besides modules (PLAN 0.9).</summary>
    public static WebApplicationBuilder AddMerpWebHost(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        var configuration = builder.Configuration;

        services.AddLocalizationResources(typeof(WebHostExtensions).Assembly);
        services.AddHttpContextAccessor();
        services.AddProblemDetails(options => options.CustomizeProblemDetails = LocalizedProblemDetails.Customize);
        services.AddExceptionHandler<GlobalExceptionHandler>();

        // Numbers are JSON numbers only, so the OpenAPI document (and the generated web client) types them as
        // numbers rather than "number or numeric string".
        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);

        // Authentication: Keycloak access tokens (D5). Claims keep their JWT names (sub, tenant).
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = configuration["Authentication:Authority"];
                options.Audience = configuration["Authentication:Audience"] ?? "merp-api";
                options.RequireHttpsMetadata = configuration.GetValue("Authentication:RequireHttpsMetadata", !builder.Environment.IsDevelopment());
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = "preferred_username";
                options.Events = new JwtBearerEvents
                {
                    // Browsers cannot set headers on WebSocket connections; SignalR sends the token in the query string.
                    OnMessageReceived = context =>
                    {
                        if (context.Request.Path.StartsWithSegments(NotificationsHub.Path, StringComparison.Ordinal)
                            && context.Request.Query.TryGetValue("access_token", out var token))
                        {
                            context.Token = token;
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddAuthorization();

        services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy
            .WithOrigins(configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
            .WithHeaders("Authorization", "Content-Type", "Accept", "Accept-Language", RequestContextMiddleware.CompanyHeader, "X-Requested-With")
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
            .WithExposedHeaders("Content-Disposition", "Location", "Retry-After")));

        services.AddMerpRateLimiting(configuration);

        // Only tr-TR is active until languages are managed in the database (D11, PLAN P4).
        var cultures = configuration.GetSection("Localization:SupportedCultures").Get<string[]>() is { Length: > 0 } configured
            ? configured
            : [EmbeddedTextLocalizer.DefaultCulture];
        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.SetDefaultCulture(cultures[0]).AddSupportedCultures(cultures).AddSupportedUICultures(cultures);
            options.ApplyCurrentCultureToResponseHeaders = true;
            options.RequestCultureProviders = [new AcceptLanguageHeaderRequestCultureProvider()];
        });

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            foreach (var proxy in configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
            {
                options.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
            }
        });

        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/problem+json"]);
        });
        services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);

        services.AddMerpHealthChecks(configuration);
        services.AddMerpOpenApi(configuration);
        services.AddSignalR();
        services.AddMerpJobs(configuration);
        return builder;
    }

    /// <summary>The middleware pipeline. The order is significant; see the comments.</summary>
    public static WebApplication UseMerpWebHost(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseForwardedHeaders();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseResponseCompression();
        app.UseSerilogRequestLogging();
        app.UseRequestLocalization();
        app.UseRouting();
        app.UseCors(CorsPolicy);
        app.UseAuthentication();
        // Tenant before authorization: permissions live in the tenant database.
        app.UseTenantResolution();
        app.UseMiddleware<RequestContextMiddleware>();
        app.UseRateLimiter();
        app.UseAuthorization();
        return app;
    }

    /// <summary>Host level endpoints: health, API docs, SignalR hub, jobs dashboard.</summary>
    public static WebApplication MapMerpHostEndpoints(this WebApplication app, string jobsDashboardPermission)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapMerpHealthChecks();
        if (app.Configuration.GetValue("ApiDocs:Enabled", app.Environment.IsDevelopment()))
        {
            app.MapMerpApiDocs(app.Configuration);
        }

        app.MapHub<NotificationsHub>(NotificationsHub.Path);
        app.MapMerpJobsDashboard(jobsDashboardPermission, app.Configuration);
        return app;
    }
}
