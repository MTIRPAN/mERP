using Merp.Application.Abstractions.Jobs;
using Merp.Application.Abstractions.Messaging;
using Merp.Modules.Recruitment.Application.Applications;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Recruitment.Infrastructure.Applications;

/// <summary>Every 15 minutes: sends the rejection e-mails whose delay ran out (D50).</summary>
public sealed class RejectionEmailsJob(ITenantJobRunner tenants) : IRecurringJob
{
    public const string JobId = "recruitment.rejection-emails";
    public const string Cron = "*/15 * * * *";

    public Task RunAsync(CancellationToken cancellationToken) =>
        tenants.RunForEachTenantAsync(
            async (services, ct) =>
            {
                var result = await services.GetRequiredService<ISender>().Send(new SendDueRejectionEmailsCommand(), ct);
                if (result.IsFailure)
                {
                    throw new InvalidOperationException($"Sending the rejection e-mails failed: {result.Error.Code}");
                }
            },
            cancellationToken);
}
