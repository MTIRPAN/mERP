using Merp.Application.Abstractions.Jobs;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Settings;
using Merp.Modules.Recruitment.Application.Openings;
using Merp.Modules.Recruitment.Application.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Recruitment.Infrastructure.Openings;

/// <summary>
/// Every night at 03:00 Istanbul time: sends the waiting batch screenings of each tenant to the provider's batch
/// processing, oldest request first, up to the tenant's nightly limit of applications (A6, D59). A screening over the
/// budget or with the feature off is held and tried again the next night.
/// </summary>
public sealed class ScreeningSubmitJob(ITenantJobRunner tenants) : IRecurringJob
{
    public const string JobId = "recruitment.screening-submit";
    public const string Cron = "0 0 * * *";

    public Task RunAsync(CancellationToken cancellationToken) =>
        tenants.RunForEachTenantAsync(
            async (services, ct) =>
            {
                var sender = services.GetRequiredService<ISender>();
                var limit = await services.GetRequiredService<ISettings>().GetAsync<int>(RecruitmentSettingDefinitions.ScreeningNightlyLimit, null, ct);
                var done = new List<Guid>();
                while (true)
                {
                    var result = await sender.Send(new SubmitNextScreeningQuery(limit, done), ct);
                    if (result.IsFailure)
                    {
                        throw new InvalidOperationException($"Sending the batch screenings failed: {result.Error.Code}");
                    }

                    if (result.Value is not { } run)
                    {
                        return;
                    }

                    done.Add(run.ScreeningId);
                    limit -= run.Sent;
                }
            },
            cancellationToken);
}

/// <summary>Every 10 minutes: stores the results of the batch screenings whose batch ended (A6, D59).</summary>
public sealed class ScreeningCollectJob(ITenantJobRunner tenants) : IRecurringJob
{
    public const string JobId = "recruitment.screening-collect";
    public const string Cron = "*/10 * * * *";

    public Task RunAsync(CancellationToken cancellationToken) =>
        tenants.RunForEachTenantAsync(
            async (services, ct) =>
            {
                var sender = services.GetRequiredService<ISender>();
                var done = new List<Guid>();
                while (true)
                {
                    var result = await sender.Send(new CollectNextScreeningQuery(done), ct);
                    if (result.IsFailure)
                    {
                        throw new InvalidOperationException($"Collecting the batch screenings failed: {result.Error.Code}");
                    }

                    if (result.Value is not { } run)
                    {
                        return;
                    }

                    done.Add(run.ScreeningId);
                }
            },
            cancellationToken);
}
