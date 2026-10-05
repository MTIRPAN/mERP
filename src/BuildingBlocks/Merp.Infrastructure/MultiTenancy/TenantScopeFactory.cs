using Merp.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Infrastructure.MultiTenancy;

/// <summary>Creates DI scopes bound to a tenant, running as system (background processing).</summary>
public sealed class TenantScopeFactory(IServiceScopeFactory scopeFactory)
{
    public AsyncServiceScope CreateScope(TenantRecord tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenant.ToTenantInfo(), tenant.ConnectionString);
        scope.ServiceProvider.GetRequiredService<CurrentUser>().SetSystem(tenant.Id);
        return scope;
    }
}
