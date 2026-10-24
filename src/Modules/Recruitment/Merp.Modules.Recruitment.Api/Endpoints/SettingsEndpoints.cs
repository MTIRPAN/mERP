using Merp.Application.Abstractions.Messaging;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Application.Settings;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.SharedKernel.Results;
using Merp.Web.Endpoints;
using Merp.Web.Errors;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.Recruitment.Api.Endpoints;

/// <summary>Recruitment settings (PLAN R1, D47): stage templates, lists and KVKK texts.</summary>
internal static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder group)
    {
        var settings = group.MapGroup("/settings");

        settings.MapGet("/stage-templates", async Task<Results<Ok<IReadOnlyList<StageTemplateItem>>, ProblemHttpResult>> (bool? includeInactive, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetStageTemplatesQuery(includeInactive ?? false), ct)))
            .RequirePermission(RecruitmentPermissions.Settings.Read)
            .WithName("ListStageTemplates")
            .WithSummary("The stage templates, the default first; active ones unless asked");

        settings.MapPost("/stage-templates", (CreateStageTemplateCommand command, ISender sender, CancellationToken ct) =>
                CreatedAsync(sender.Send(command, ct), id => $"/api/recruitment/settings/stage-templates/{id}"))
            .RequirePermission(RecruitmentPermissions.Settings.Manage)
            .WithName("CreateStageTemplate")
            .WithSummary("Add a stage template: Applied first, Hired, Rejected and Withdrawn last (Tenant scope required)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        settings.MapPut("/stage-templates/{id:guid}", (Guid id, UpdateStageTemplateCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Id = id }, ct)))
            .RequirePermission(RecruitmentPermissions.Settings.Manage)
            .WithName("UpdateStageTemplate")
            .WithSummary("Change a stage template; openings keep the copy they took (Tenant scope required)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        settings.MapGet("/lookups", async Task<Results<Ok<IReadOnlyList<RecruitmentLookupItem>>, ProblemHttpResult>> (LookupKind? kind, bool? includeInactive, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetRecruitmentLookupsQuery(kind, includeInactive ?? false), ct)))
            .RequirePermission(RecruitmentPermissions.Settings.Read)
            .WithName("ListRecruitmentLookups")
            .WithSummary("Rejection and withdrawal reasons, candidate sources and tags, in their order")
            .ProducesValidationProblem();

        settings.MapPost("/lookups", (CreateRecruitmentLookupCommand command, ISender sender, CancellationToken ct) =>
                CreatedAsync(sender.Send(command, ct), _ => "/api/recruitment/settings/lookups"))
            .RequirePermission(RecruitmentPermissions.Settings.Manage)
            .WithName("CreateRecruitmentLookup")
            .WithSummary("Add a choice to a list; its code is unique in the list (Tenant scope required)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        settings.MapPut("/lookups/{id:guid}", (Guid id, UpdateRecruitmentLookupCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Id = id }, ct)))
            .RequirePermission(RecruitmentPermissions.Settings.Manage)
            .WithName("UpdateRecruitmentLookup")
            .WithSummary("Rename, reorder or retire a choice (Tenant scope required)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        settings.MapGet("/consent-texts", async Task<Results<Ok<IReadOnlyList<ConsentTextItem>>, ProblemHttpResult>> (ConsentKind? kind, Guid? companyId, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetConsentTextsQuery(kind, companyId), ct)))
            .RequirePermission(RecruitmentPermissions.Settings.Read)
            .WithName("ListConsentTexts")
            .WithSummary("The published versions of the KVKK texts, newest first")
            .ProducesValidationProblem();

        settings.MapGet("/consent-texts/current", async Task<Results<Ok<ConsentTextItem>, ProblemHttpResult>> (ConsentKind kind, Guid? companyId, string? culture, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetCurrentConsentTextQuery(kind, companyId, culture), ct)))
            .RequirePermission(RecruitmentPermissions.Settings.Read)
            .WithName("GetCurrentConsentText")
            .WithSummary("The KVKK text a candidate of a company sees in a language; the built-in text (revision 0) while none is published")
            .ProducesValidationProblem();

        settings.MapPost("/consent-texts", (PublishConsentTextCommand command, ISender sender, CancellationToken ct) =>
                CreatedAsync(sender.Send(command, ct), _ => "/api/recruitment/settings/consent-texts"))
            .RequirePermission(RecruitmentPermissions.Settings.Manage)
            .WithName("PublishConsentText")
            .WithSummary("Publish a new version of a KVKK text for a company (every company needs the Tenant scope) and a language")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static Results<Ok<T>, ProblemHttpResult> OkOrProblem<T>(Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();

    private static async Task<Results<NoContent, ProblemHttpResult>> NoContentAsync(Task<Result> sending)
    {
        var result = await sending;
        return result.IsSuccess ? TypedResults.NoContent() : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> CreatedAsync(Task<Result<Guid>> sending, Func<Guid, string> location)
    {
        var result = await sending;
        return result.IsSuccess
            ? TypedResults.Created(location(result.Value), new EntityCreatedResponse(result.Value))
            : (ProblemHttpResult)result.Error.ToProblem();
    }
}
