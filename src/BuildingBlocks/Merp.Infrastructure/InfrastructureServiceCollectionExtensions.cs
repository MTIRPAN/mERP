using Merp.Application.Abstractions.Context;
using Merp.Infrastructure.Localization;
using Merp.Infrastructure.Messaging;
using Merp.Infrastructure.MultiTenancy;
using Merp.Infrastructure.Persistence;
using Merp.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Merp.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Core infrastructure every host needs: time, caller and tenant context, mediator, persistence.</summary>
    public static IServiceCollection AddMerpInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddScoped<CurrentUser>();
        services.TryAddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());
        services.TryAddScoped<TenantContext>();
        services.TryAddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.TryAddScoped<CompanyContext>();
        services.TryAddScoped<ICompanyContext>(sp => sp.GetRequiredService<CompanyContext>());

        services.AddLocalizationResources(typeof(SharedKernel.Results.Error).Assembly);
        services.AddMerpLocalization();

        services.AddMerpMediator();
        services.AddMerpPersistence();
        return services;
    }
}
