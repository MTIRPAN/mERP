using Merp.Infrastructure.Messaging.Integration;
using Merp.Infrastructure.Messaging.RabbitMq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Merp.Infrastructure.Messaging;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>Outbox dispatcher, inbox processor and the transport selected by <c>Messaging:Transport</c> (D8).</summary>
    public static IServiceCollection AddMerpMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(MessagingOptions.SectionName);
        services.AddOptions<MessagingOptions>().Bind(section);
        services.TryAddSingleton<InboxWriter>();
        services.TryAddSingleton<OutboxDispatcher>();
        services.TryAddSingleton<InboxProcessor>();

        var transport = section.GetValue(nameof(MessagingOptions.Transport), MessagingTransport.InProcess);
        if (transport == MessagingTransport.RabbitMQ)
        {
            services.TryAddSingleton<RabbitMqConnectionProvider>();
            services.TryAddSingleton<IMessageTransport, RabbitMqTransport>();
            services.TryAddSingleton<RabbitMqConsumerService>();
            services.AddHostedService(sp => sp.GetRequiredService<RabbitMqConsumerService>());
        }
        else
        {
            services.TryAddSingleton<IMessageTransport, InProcessTransport>();
        }

        services.AddHostedService<OutboxDispatcherService>();
        services.AddHostedService<InboxProcessorService>();
        return services;
    }
}
