using System.Globalization;
using Merp.Application.Abstractions.Context;
using Merp.Infrastructure.AI;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Infrastructure.AI;
using Merp.SharedKernel.Results;
using Merp.Web.Errors;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Merp.Modules.Platform.Api.Endpoints;

internal static class AiUsageEndpoints
{
    public static void MapAiUsageEndpoints(this IEndpointRouteBuilder group) =>
        group.MapGet("/ai-usage/summary", GetSummaryAsync)
            .RequirePermission(PlatformPermissions.AiUsage.Read)
            .WithName("GetAiUsageSummary")
            .WithSummary("AI calls, tokens and estimated cost of the tenant for a month (D24)")
            .ProducesValidationProblem();

    private static async Task<Results<Ok<AiUsageSummaryResponse>, ProblemHttpResult>> GetSummaryAsync(
        string? month,
        ITenantContext tenant,
        IOptionsSnapshot<AiOptions> options,
        IAiModelClient client,
        AiUsageReader reader,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var (year, monthNumber) = AiBudgetPeriod.MonthOf(timeProvider.GetUtcNow());
        if (month is not null)
        {
            if (!DateOnly.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return (ProblemHttpResult)Error.Validation([new FieldError("month", "common.errors.invalid_format")]).ToProblem();
            }

            (year, monthNumber) = (parsed.Year, parsed.Month);
        }

        var from = AiBudgetPeriod.Start(year, monthNumber);
        var to = monthNumber == 12 ? AiBudgetPeriod.Start(year + 1, 1) : AiBudgetPeriod.Start(year, monthNumber + 1);
        var features = await reader.GetByFeatureAsync(from, to, cancellationToken);

        var settings = options.Value.ForTenant(tenant.RequiredTenant.Code);
        var configured = options.Value.Provider != AiProvider.None && client.IsConfigured;
        return TypedResults.Ok(new AiUsageSummaryResponse(
            new DateOnly(year, monthNumber, 1).ToString("yyyy-MM", CultureInfo.InvariantCulture),
            configured,
            configured && settings.Enabled,
            configured ? options.Value.Model : null,
            settings.MonthlyBudgetUsd,
            features.Sum(f => f.CostUsd),
            features.Sum(f => f.Requests),
            [.. features.Select(f => new AiFeatureUsageResponse(f.Feature, f.Requests, f.Succeeded, f.InputTokens, f.OutputTokens, f.CostUsd))]));
    }
}

/// <param name="Month">Month in <c>yyyy-MM</c> form; months start in the Europe/Istanbul time zone.</param>
/// <param name="ProviderConfigured">An AI provider with credentials is configured on the server.</param>
/// <param name="Enabled">AI is enabled for this tenant.</param>
/// <param name="Model">Model used for new calls.</param>
/// <param name="MonthlyBudgetUsd">Monthly limit; 0 means no limit.</param>
/// <param name="CostUsd">Estimated cost of the month at list prices.</param>
/// <param name="Requests">Calls in the month.</param>
/// <param name="Features">Totals per feature, most expensive first.</param>
public sealed record AiUsageSummaryResponse(
    string Month,
    bool ProviderConfigured,
    bool Enabled,
    string? Model,
    decimal MonthlyBudgetUsd,
    decimal CostUsd,
    int Requests,
    IReadOnlyList<AiFeatureUsageResponse> Features);

public sealed record AiFeatureUsageResponse(string Feature, int Requests, int Succeeded, long InputTokens, long OutputTokens, decimal CostUsd);
