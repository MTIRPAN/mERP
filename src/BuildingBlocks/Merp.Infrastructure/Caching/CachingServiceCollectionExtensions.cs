using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Infrastructure.Caching;

public static class CachingServiceCollectionExtensions
{
    public const string ValkeyConnectionStringName = "Valkey";

    /// <summary>
    /// HybridCache with Valkey as the distributed second level when <c>ConnectionStrings:Valkey</c> is set (D19);
    /// otherwise in-memory only.
    /// </summary>
    public static IServiceCollection AddMerpCaching(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var valkey = configuration.GetConnectionString(ValkeyConnectionStringName);
        if (!string.IsNullOrWhiteSpace(valkey))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = valkey;
                options.InstanceName = "merp:";
            });
        }

        services.AddHybridCache();
        return services;
    }
}
