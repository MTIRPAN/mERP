using Merp.Application.Abstractions.Jobs;
using Merp.Application.Abstractions.Messaging;
using Merp.Modules.Recruitment.Application.Candidates;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Recruitment.Infrastructure.Candidates;

/// <summary>Every 5 minutes: reads the text of the newer CVs so the pool search finds them (D50).</summary>
public sealed class CandidateCvTextJob(ITenantJobRunner tenants) : IRecurringJob
{
    public const string JobId = "recruitment.candidate-cv-text";
    public const string Cron = "*/5 * * * *";

    public Task RunAsync(CancellationToken cancellationToken) =>
        tenants.RunForEachTenantAsync(
            async (services, ct) =>
            {
                var result = await services.GetRequiredService<ISender>().Send(new IndexCandidateCvsCommand(), ct);
                if (result.IsFailure)
                {
                    throw new InvalidOperationException($"Reading the CV texts failed: {result.Error.Code}");
                }
            },
            cancellationToken);
}
