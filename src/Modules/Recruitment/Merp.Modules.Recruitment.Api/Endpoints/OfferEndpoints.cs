using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Application.Applications;
using Merp.Modules.Recruitment.Application.Hiring;
using Merp.Modules.Recruitment.Application.Offers;
using Merp.Modules.Recruitment.Domain.Offers;
using Merp.SharedKernel.Results;
using Merp.Web.Endpoints;
using Merp.Web.Errors;
using Merp.Web.Files;
using Merp.Web.RateLimiting;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.Recruitment.Api.Endpoints;

/// <summary>
/// Job offers (PLAN R6, D53): drafted, approved through <c>recruitment.offer</c>, sent with the letter and answered by
/// the candidate with the link or recorded by HR; an accepted offer hires the application.
/// </summary>
internal static class OfferEndpoints
{
    public static void MapOfferEndpoints(this IEndpointRouteBuilder group)
    {
        var offers = group.MapGroup("/offers");

        offers.MapGet("", ListAsync)
            .RequirePermission(RecruitmentPermissions.Offers.Read)
            .WithName("ListOffers")
            .WithSummary("Offers of the companies in scope, newest first; the salary is not listed")
            .ProducesValidationProblem();

        offers.MapPost("", (CreateOfferCommand command, ISender sender, CancellationToken ct) =>
                CreatedAsync(sender.Send(command, ct), id => $"/api/recruitment/offers/{id}"))
            .RequirePermission(RecruitmentPermissions.Offers.Manage)
            .WithName("CreateOffer")
            .WithSummary("Draft an offer for an application in the pipeline")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        offers.MapGet("/{id:guid}", async Task<Results<Ok<OfferDetails>, ProblemHttpResult>> (Guid id, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetOfferQuery(id), ct)))
            .RequirePermission(RecruitmentPermissions.Offers.Read)
            .WithName("GetOffer")
            .WithSummary("An offer; the salary to those who manage offers and to its approvers")
            .ProducesProblem(StatusCodes.Status404NotFound);

        offers.MapGet("/{id:guid}/letter", async (Guid id, HttpContext context, ISender sender, CancellationToken ct) =>
                Download(context, await sender.Send(new GetOfferLetterQuery(id), ct)))
            .RequirePermission(RecruitmentPermissions.Offers.Read)
            .WithName("DownloadOfferLetter")
            .WithSummary("The offer letter as sent, or a preview before sending")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .ProducesProblem(StatusCodes.Status404NotFound);

        offers.MapPut("/{id:guid}", (Guid id, UpdateOfferCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Id = id }, ct)))
            .RequirePermission(RecruitmentPermissions.Offers.Manage)
            .WithName("UpdateOffer")
            .WithSummary("Correct a draft or a returned offer")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        offers.MapPost("/{id:guid}/submit", (Guid id, SubmitOfferCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Id = id }, ct)))
            .RequirePermission(RecruitmentPermissions.Offers.Manage)
            .WithName("SubmitOffer")
            .WithSummary("Send a draft or a returned offer for approval")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        offers.MapPost("/{id:guid}/send", (Guid id, SendOfferCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Id = id }, ct)))
            .RequirePermission(RecruitmentPermissions.Offers.Manage)
            .WithName("SendOffer")
            .WithSummary("E-mail an approved offer to the candidate with the letter and the answer link")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        offers.MapPost("/{id:guid}/response", (Guid id, RecordOfferResponseCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Id = id }, ct)))
            .RequirePermission(RecruitmentPermissions.Offers.Manage)
            .WithName("RecordOfferResponse")
            .WithSummary("Record the candidate's answer to a sent offer")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        offers.MapPost("/{id:guid}/withdraw", (Guid id, WithdrawOfferCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Id = id }, ct)))
            .RequirePermission(RecruitmentPermissions.Offers.Manage)
            .WithName("WithdrawOffer")
            .WithSummary("Take back an offer the candidate has not answered")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/applications/{id:guid}/hire", (Guid id, HireApplicationCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Id = id }, ct)))
            .RequirePermission(RecruitmentPermissions.Applications.Manage)
            .WithName("HireApplication")
            .WithSummary("Ask HR to hire the candidate whose offer was accepted; the application is hired when HR completes the pre-hire")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/openings/{id:guid}/reject-remaining", async Task<Results<Ok<RemainingRejected>, ProblemHttpResult>> (Guid id, RejectRemainingApplicationsCommand command, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(command with { OpeningId = id }, ct);
                return result.IsSuccess ? TypedResults.Ok(new RemainingRejected(result.Value)) : (ProblemHttpResult)result.Error.ToProblem();
            })
            .RequirePermission(RecruitmentPermissions.Applications.Manage)
            .WithName("RejectRemainingApplications")
            .WithSummary("Reject the applications still in the pipeline of an opening at once; those waiting for HR to hire stay")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // The opening's hiring manager, usually its first approver, reads the offer from self service (D50, D53)
        var team = group.MapGroup("/me/hiring/offers");

        team.MapGet("/{id:guid}", async Task<Results<Ok<OfferDetails>, ProblemHttpResult>> (Guid id, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetOfferQuery(id) { AsTeam = true }, ct)))
            .RequirePermission(RecruitmentPermissions.Self.Hiring)
            .WithName("GetMyHiringOffer")
            .WithSummary("An offer of an opening I am the hiring manager of; the salary when I approve it")
            .ProducesProblem(StatusCodes.Status404NotFound);

        team.MapGet("/{id:guid}/letter", async (Guid id, HttpContext context, ISender sender, CancellationToken ct) =>
                Download(context, await sender.Send(new GetOfferLetterQuery(id) { AsTeam = true }, ct)))
            .RequirePermission(RecruitmentPermissions.Self.Hiring)
            .WithName("DownloadMyHiringOfferLetter")
            .WithSummary("The letter of an offer I approve")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    /// <summary>The candidate's page of an offer under <c>/api/public/{tenantCode}/offers/{token}</c> (D20, limited per IP).</summary>
    public static void MapPublicOfferEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // The token is the route's last segment: a group pattern with "" would end in a slash, off the D20 list
        var offers = endpoints.MapGroup("/api/public/{tenantCode}/offers")
            .WithTags("offers")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingExtensions.PublicPolicy);

        offers.MapGet("/{token}", async Task<Results<Ok<OfferResponsePage>, ProblemHttpResult>> ([FromRoute] string tenantCode, [FromRoute] string token, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetOfferResponsePageQuery(token), ct)))
            .WithName("GetOfferResponsePage")
            .WithSummary("The offer the link in the candidate's e-mail opens")
            .ProducesProblem(StatusCodes.Status404NotFound);

        offers.MapPost("/{token}", ([FromRoute] string tenantCode, [FromRoute] string token, RespondToOfferCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Token = token }, ct)))
            // Anonymous: no cookie authenticates the request, so cross-site request forgery does not apply.
            .DisableAntiforgery()
            .WithName("RespondToOffer")
            .WithSummary("Accept or decline the offer of the link until its last day")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    /// <param name="page">1-based page.</param>
    /// <param name="pageSize">Rows per page, at most 200.</param>
    /// <param name="sort">createdAt or expiresOn; a leading - sorts descending.</param>
    /// <param name="applicationId">Only the offers of this application.</param>
    /// <param name="status">Only these states (repeatable).</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<PagedResult<OfferListItem>>, ProblemHttpResult>> ListAsync(
        int? page,
        int? pageSize,
        string? sort,
        Guid? applicationId,
        OfferStatus[]? status,
        ISender sender,
        CancellationToken cancellationToken) =>
        OkOrProblem(await sender.Send(new GetOffersQuery(new PageRequest(page, pageSize, sort), applicationId, status), cancellationToken));

    private static IResult Download(HttpContext context, Result<RecruitmentFileDownload> result) =>
        result.IsSuccess
            ? FileTransfer.Content(context, result.Value.FileName, result.Value.ContentType, result.Value.Length, result.Value.Content)
            : result.Error.ToProblem();

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
