using Merp.Application.Abstractions.Messaging;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Settings;
using Merp.Web.Errors;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.Platform.Api.Endpoints;

/// <summary>Settings of all modules (PLAN P11a, D35): <c>platform.settings.*</c> with the Tenant scope.</summary>
internal static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/settings", GetAsync)
            .RequirePermission(PlatformPermissions.Settings.Read)
            .WithName("GetSettings")
            .WithSummary("Settings of all modules with their values for the tenant or a company")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/settings", UpdateAsync)
            .RequirePermission(PlatformPermissions.Settings.Manage)
            .WithName("UpdateSettings")
            .WithSummary("Save setting values for the tenant or a company; null falls back to the level above")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    /// <param name="companyId">A company's values; without it the tenant's.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<IReadOnlyList<SettingModuleGroup>>, ProblemHttpResult>> GetAsync(
        Guid? companyId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetSettingsQuery(companyId), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UpdateAsync(
        UpdateSettingsCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : (ProblemHttpResult)result.Error.ToProblem();
    }
}
