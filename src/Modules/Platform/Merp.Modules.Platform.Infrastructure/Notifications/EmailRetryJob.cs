using Merp.Application.Abstractions.Jobs;
using Merp.Application.Abstractions.Messaging;
using Merp.Modules.Platform.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Platform.Infrastructure.Notifications;

/// <summary>Retries the queued e-mails of every tenant whose next attempt is due (D32).</summary>
public sealed class EmailRetryJob(ITenantJobRunner tenants) : IRecurringJob
{
    public const string JobId = "notifications.email";

    public Task RunAsync(CancellationToken cancellationToken) =>
        tenants.RunForEachTenantAsync(
            async (services, ct) =>
            {
                var result = await services.GetRequiredService<ISender>().Send(new SendDueEmailsCommand(), ct);
                if (result.IsFailure)
                {
                    throw new InvalidOperationException($"Sending queued e-mails failed: {result.Error.Code}");
                }
            },
            cancellationToken);
}
