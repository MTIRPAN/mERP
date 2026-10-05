using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Merp.Web.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Web.RateLimiting;

public static class RateLimitingExtensions
{
    /// <summary>Policy for anonymous public endpoints (career page, D20): per client IP.</summary>
    public const string PublicPolicy = "public";

    /// <summary>Policy for endpoints that call the AI provider (D24): per user, because every call costs money.</summary>
    public const string AiPolicy = "ai";

    public static IServiceCollection AddMerpRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var window = TimeSpan.FromMinutes(1);
        var globalLimit = configuration.GetValue("RateLimiting:Global:PermitLimit", 1000);
        var publicLimit = configuration.GetValue("RateLimiting:Public:PermitLimit", 30);
        var aiLimit = configuration.GetValue("RateLimiting:Ai:PermitLimit", 10);

        services.AddRateLimiter(options =>
        {
            // Authenticated callers are limited per user, anonymous ones per IP address.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.User.FindFirstValue("sub") ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = globalLimit, Window = window }));

            options.AddPolicy(PublicPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = publicLimit, Window = window }));

            options.AddPolicy(AiPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.User.FindFirstValue("sub") ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = aiLimit, Window = window }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                var response = context.HttpContext.Response;
                response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                await context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Extensions = { [LocalizedProblemDetails.CodeKey] = "common.errors.too_many_requests" },
                    },
                });
            };
        });

        return services;
    }
}
