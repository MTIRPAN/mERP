using Merp.Application.Abstractions.Notifications;
using Merp.Infrastructure.Templates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Merp.Infrastructure.Email;

public static class EmailServiceCollectionExtensions
{
    /// <summary>
    /// E-mail over SMTP (<c>Email</c> section) and Liquid templates (Fluid). The real-time channel is added by the web
    /// host; hosts without it (the migrator) get a notifier that drops messages.
    /// </summary>
    public static IServiceCollection AddMerpEmail(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(EmailOptions.SectionName));
        services.TryAddSingleton<IEmailSender, MailKitEmailSender>();
        services.TryAddSingleton<ITemplateRenderer, FluidTemplateRenderer>();
        services.TryAddSingleton<IRealtimeNotifier, NoRealtimeNotifier>();
        return services;
    }

    private sealed class NoRealtimeNotifier : IRealtimeNotifier
    {
        public Task SendToUserAsync(string userSubject, string method, object payload, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
