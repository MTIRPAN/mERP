using Merp.Application.Abstractions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Merp.Infrastructure.AI;

public static class AiServiceCollectionExtensions
{
    /// <summary>
    /// AI features (D24): <see cref="IAiService"/> and the provider selected by <c>Ai:Provider</c>. Everything stays
    /// disabled until a provider with credentials is configured and the tenant enables features.
    /// </summary>
    public static IServiceCollection AddMerpAi(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<AiOptions>().Bind(configuration.GetSection(AiOptions.SectionName));
        services.AddHttpClient(AnthropicModelClient.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);

        services.TryAddSingleton<IAiModelClient>(sp => sp.GetRequiredService<IOptions<AiOptions>>().Value.Provider switch
        {
            AiProvider.Anthropic => ActivatorUtilities.CreateInstance<AnthropicModelClient>(sp),
            _ => new DisabledAiModelClient(),
        });

        services.AddMetrics();
        services.TryAddSingleton<AiTelemetry>();
        services.TryAddScoped<IAiUsageLedger, NullAiUsageLedger>();
        services.TryAddScoped<IAiService, AiService>();
        return services;
    }
}
