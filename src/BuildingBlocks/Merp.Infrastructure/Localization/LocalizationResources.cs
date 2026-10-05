using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Infrastructure.Localization;

/// <summary>Assemblies that embed <c>Localization/{culture}.json</c> default texts (D11).</summary>
public sealed class LocalizationResources
{
    private readonly HashSet<Assembly> _assemblies = [];

    public IReadOnlyCollection<Assembly> Assemblies => _assemblies;

    public LocalizationResources Add(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        _assemblies.Add(assembly);
        return this;
    }
}

public static class LocalizationResourcesServiceCollectionExtensions
{
    /// <summary>Adds an assembly's embedded texts to the single <see cref="LocalizationResources"/> instance.</summary>
    public static IServiceCollection AddLocalizationResources(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        var resources = services
            .Where(d => d.ServiceType == typeof(LocalizationResources))
            .Select(d => d.ImplementationInstance)
            .OfType<LocalizationResources>()
            .FirstOrDefault();

        if (resources is null)
        {
            resources = new LocalizationResources();
            services.AddSingleton(resources);
        }

        resources.Add(assembly);
        return services;
    }
}
