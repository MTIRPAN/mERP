using Merp.Infrastructure.Jobs;
using Microsoft.AspNetCore.Http;

namespace Merp.Web.MultiTenancy;

/// <summary>Exposes the request scope to Hangfire client filters (tenant and user of enqueued jobs).</summary>
public sealed class AmbientServiceScopeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        AmbientServiceScope.Current = context.RequestServices;
        try
        {
            await next(context);
        }
        finally
        {
            AmbientServiceScope.Current = null;
        }
    }
}
