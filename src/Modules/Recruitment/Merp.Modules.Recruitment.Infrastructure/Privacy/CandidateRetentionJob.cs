using Merp.Application.Abstractions.Jobs;
using Merp.Application.Abstractions.Messaging;
using Merp.Modules.Recruitment.Application.Privacy;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Recruitment.Infrastructure.Privacy;

/// <summary>Every night: asks for the renewal of the pool consent and anonymizes the candidates whose retention ended (D55).</summary>
public sealed class CandidateRetentionJob(ITenantJobRunner tenants) : IRecurringJob
{
    public const string JobId = "recruitment.candidate-retention";
    public const string Cron = "30 2 * * *";

    public Task RunAsync(CancellationToken cancellationToken) =>
        tenants.RunForEachTenantAsync(
            async (services, ct) =>
            {
                var result = await services.GetRequiredService<ISender>().Send(new RunCandidateRetentionCommand(), ct);
                if (result.IsFailure)
                {
                    throw new InvalidOperationException($"The candidate retention failed: {result.Error.Code}");
                }
            },
            cancellationToken);
}
