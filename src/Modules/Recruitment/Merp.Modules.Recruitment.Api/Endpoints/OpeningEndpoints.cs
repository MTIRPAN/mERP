using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Application.Openings;
using Merp.Modules.Recruitment.Application.Requisitions;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.SharedKernel.Results;
using Merp.Web.Endpoints;
using Merp.Web.Errors;
using Merp.Web.RateLimiting;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.Recruitment.Api.Endpoints;

/// <summary>Job openings (PLAN R3a, D49): what is offered, the pipeline, the hiring team and the application form.</summary>
internal static class OpeningEndpoints
{
    public static void MapOpeningEndpoints(this IEndpointRouteBuilder group)
    {
        var openings = group.MapGroup("/openings");

        openings.MapGet("", ListAsync)
            .RequirePermission(RecruitmentPermissions.Openings.Read)
            .WithName("ListOpenings")
            .WithSummary("Openings of the companies in scope, newest first")
            .ProducesValidationProblem();

        openings.MapGet("/{id:guid}", async Task<Results<Ok<OpeningDetails>, ProblemHttpResult>> (Guid id, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetOpeningQuery(id), ct)))
            .RequirePermission(RecruitmentPermissions.Openings.Read)
            .WithName("GetOpening")
            .WithSummary("An opening with its texts, pipeline, hiring team and application form")
            .ProducesProblem(StatusCodes.Status404NotFound);

        openings.MapPost("", (CreateOpeningCommand command, ISender sender, CancellationToken ct) =>
                CreatedAsync(sender.Send(command, ct), id => $"/api/recruitment/openings/{id}"))
            .RequirePermission(RecruitmentPermissions.Openings.Manage)
            .WithName("CreateOpening")
            .WithSummary("Open an opening as a draft; from an approved requisition it seeks at most the people left")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        openings.MapPut("/{id:guid}", (Guid id, UpdateOpeningCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Id = id }, ct)))
            .RequirePermission(RecruitmentPermissions.Openings.Manage)
            .WithName("UpdateOpening")
            .WithSummary("Correct an opening that is not closed; its stages change only while it is a draft")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        openings.MapPost("/text-draft", async Task<Results<Ok<OpeningDraftResponse>, ProblemHttpResult>> (DraftOpeningTextQuery query, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(query, ct);
                return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
            })
            .RequirePermission(RecruitmentPermissions.Openings.Manage)
            .RequireRateLimiting(RateLimitingExtensions.AiPolicy)
            .WithName("DraftOpeningText")
            .WithSummary("Draft an opening's description and qualifications with AI from its title, requisition and a brief (A4, D57)")
            .WithDescription("Nothing is stored. Contact details in the brief are removed before it reaches the AI provider; the requisition's justification is not sent. An opening saved with the draft is published only after a user confirms reviewing it.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        openings.MapPost("/language-check", async Task<Results<Ok<OpeningLanguageCheck>, ProblemHttpResult>> (CheckOpeningLanguageQuery query, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(query, ct);
                return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
            })
            .RequirePermission(RecruitmentPermissions.Openings.Manage)
            .RequireRateLimiting(RateLimitingExtensions.AiPolicy)
            .WithName("CheckOpeningLanguage")
            .WithSummary("Check an opening's title and texts for exclusionary phrases: the rule list, and the AI review when asked (A4, D57)")
            .WithDescription("Warnings never block saving or publishing.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        openings.MapPost("/{id:guid}/publish", (Guid id, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(new PublishOpeningCommand(id), ct)))
            .RequirePermission(RecruitmentPermissions.Openings.Manage)
            .WithName("PublishOpening")
            .WithSummary("Publish a draft or an opening on hold")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        openings.MapPost("/{id:guid}/hold", (Guid id, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(new HoldOpeningCommand(id), ct)))
            .RequirePermission(RecruitmentPermissions.Openings.Manage)
            .WithName("HoldOpening")
            .WithSummary("Pause a published opening")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        openings.MapPost("/{id:guid}/close", (Guid id, CloseOpeningCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Id = id }, ct)))
            .RequirePermission(RecruitmentPermissions.Openings.Manage)
            .WithName("CloseOpening")
            .WithSummary("Close an opening")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        openings.MapGet("/stage-templates", async Task<Results<Ok<IReadOnlyList<OpeningTemplateOption>>, ProblemHttpResult>> (ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetOpeningTemplatesQuery(), ct)))
            .RequirePermission(RecruitmentPermissions.Openings.Manage)
            .WithName("ListOpeningStageTemplates")
            .WithSummary("Active stage templates with their stages, the default first, for the opening form");

        openings.MapGet("/branches", async Task<Results<Ok<IReadOnlyList<BranchSummary>>, ProblemHttpResult>> (Guid companyId, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetOpeningBranchesQuery(companyId), ct)))
            .RequirePermission(RecruitmentPermissions.Openings.Manage)
            .WithName("ListOpeningBranches")
            .WithSummary("Active branches of a company, for the opening form")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        openings.MapGet("/users", async Task<Results<Ok<IReadOnlyList<RequisitionUserOption>>, ProblemHttpResult>> (string? search, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new SearchOpeningUsersQuery(search), ct)))
            .RequirePermission(RecruitmentPermissions.Openings.Manage)
            .WithName("SearchOpeningUsers")
            .WithSummary("Active users by name, for the hiring team (at most 20)");
    }

    /// <param name="page">1-based page.</param>
    /// <param name="pageSize">Rows per page, at most 200.</param>
    /// <param name="sort">createdAt, title, publishFrom or publishUntil; a leading - sorts descending.</param>
    /// <param name="companyId">Only openings of this company.</param>
    /// <param name="status">Only these states (repeatable).</param>
    /// <param name="visibility">Only internal or public openings.</param>
    /// <param name="requisitionId">Only the openings of this requisition.</param>
    /// <param name="search">Part of the title.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<PagedResult<OpeningListItem>>, ProblemHttpResult>> ListAsync(
        int? page,
        int? pageSize,
        string? sort,
        Guid? companyId,
        OpeningStatus[]? status,
        OpeningVisibility? visibility,
        Guid? requisitionId,
        string? search,
        ISender sender,
        CancellationToken cancellationToken) =>
        OkOrProblem(await sender.Send(new GetOpeningsQuery(new PageRequest(page, pageSize, sort), companyId, status, visibility, requisitionId, search), cancellationToken));

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
