using Merp.Application.Abstractions.Jobs;
using Merp.Application.Abstractions.Messaging;
using Merp.Modules.HR.Application.Documents;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.HR.Infrastructure.Documents;

/// <summary>Every night: reminds HR of the employee documents that expire in 30 or 7 days (D38).</summary>
public sealed class DocumentRemindersJob(ITenantJobRunner tenants) : IRecurringJob
{
    public const string JobId = "hr.document-reminders";

    /// <summary>05:00 UTC, 08:00 in Istanbul: before the working day starts.</summary>
    public const string Cron = "0 5 * * *";

    public Task RunAsync(CancellationToken cancellationToken) =>
        tenants.RunForEachTenantAsync(
            async (services, ct) =>
            {
                var result = await services.GetRequiredService<ISender>().Send(new SendDocumentRemindersCommand(), ct);
                if (result.IsFailure)
                {
                    throw new InvalidOperationException($"Sending the document reminders failed: {result.Error.Code}");
                }
            },
            cancellationToken);
}
