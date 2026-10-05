using Hangfire.Client;
using Merp.Application.Abstractions.Context;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Infrastructure.Jobs;

/// <summary>
/// Stores the tenant and user of the enqueuing scope as job parameters, so that <see cref="TenantAwareJobActivator"/>
/// can restore them when the job runs (D3).
/// </summary>
public sealed class TenantJobClientFilter : IClientFilter
{
    public const string TenantParameter = "merp.tenant";
    public const string UserParameter = "merp.user";

    public void OnCreating(CreatingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var scope = AmbientServiceScope.Current;
        if (scope is null)
        {
            return;
        }

        var tenant = scope.GetService<ITenantContext>()?.Tenant;
        if (tenant is not null && !context.Parameters.ContainsKey(TenantParameter))
        {
            context.SetJobParameter(TenantParameter, tenant.Id);
        }

        var userId = scope.GetService<ICurrentUser>()?.UserId;
        if (userId is not null && !context.Parameters.ContainsKey(UserParameter))
        {
            context.SetJobParameter(UserParameter, userId);
        }
    }

    public void OnCreated(CreatedContext context)
    {
    }
}
