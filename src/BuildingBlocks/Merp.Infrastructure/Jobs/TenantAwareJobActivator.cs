using Hangfire;
using Merp.Infrastructure.MultiTenancy;
using Merp.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Infrastructure.Jobs;

/// <summary>
/// Creates a DI scope per job and restores the tenant and acting user stored by <see cref="TenantJobClientFilter"/>.
/// Jobs always run as system; the original user id is kept for audit stamps.
/// </summary>
public sealed class TenantAwareJobActivator(IServiceScopeFactory scopeFactory) : JobActivator
{
    public override JobActivatorScope BeginScope(JobActivatorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var tenantId = context.GetJobParameter<Guid?>(TenantJobClientFilter.TenantParameter);
            var userId = context.GetJobParameter<Guid?>(TenantJobClientFilter.UserParameter);
            var currentUser = scope.ServiceProvider.GetRequiredService<CurrentUser>();

            if (tenantId is { } id)
            {
                // Hangfire activation is synchronous; the tenant store is served from memory after the first lookup.
                var tenant = scope.ServiceProvider.GetRequiredService<ITenantStore>().FindByIdAsync(id).GetAwaiter().GetResult()
                    ?? throw new InvalidOperationException($"Tenant {id} of the job does not exist.");
                if (!tenant.IsActive)
                {
                    throw new InvalidOperationException($"Tenant '{tenant.Code}' is inactive; its jobs do not run.");
                }

                scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenant.ToTenantInfo(), tenant.ConnectionString);
                currentUser.SetSystem(tenant.Id, userId);
            }
            else
            {
                currentUser.SetSystem(actingUserId: userId);
            }

            AmbientServiceScope.Current = scope.ServiceProvider;
            return new Scope(scope);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    private sealed class Scope(AsyncServiceScope scope) : JobActivatorScope
    {
        public override object Resolve(Type type) => ActivatorUtilities.GetServiceOrCreateInstance(scope.ServiceProvider, type);

        public override void DisposeScope()
        {
            AmbientServiceScope.Current = null;
            scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
