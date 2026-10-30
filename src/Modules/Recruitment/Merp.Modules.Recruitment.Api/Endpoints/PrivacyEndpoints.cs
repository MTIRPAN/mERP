using Merp.Application.Abstractions.Messaging;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Application.Applications;
using Merp.Modules.Recruitment.Application.Privacy;
using Merp.SharedKernel.Results;
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
/// KVKK (PLAN R8, D55): data subject requests on a candidate (export and erasure) and the candidate's page of the
/// consent renewal link.
/// </summary>
internal static class PrivacyEndpoints
{
    public static void MapPrivacyEndpoints(this IEndpointRouteBuilder group)
    {
        var candidates = group.MapGroup("/candidates");

        candidates.MapGet("/{id:guid}/export", async (Guid id, HttpContext context, ISender sender, CancellationToken ct) =>
                Download(context, await sender.Send(new ExportCandidateDataQuery(id), ct)))
            .RequirePermission(RecruitmentPermissions.Candidates.Privacy)
            .WithName("ExportCandidateData")
            .WithSummary("The candidate's data as a ZIP of JSON and files, for a data subject request")
            .Produces(StatusCodes.Status200OK, contentType: "application/zip")
            .ProducesProblem(StatusCodes.Status404NotFound);

        candidates.MapPost("/{id:guid}/erase", async Task<Results<NoContent, ProblemHttpResult>> (Guid id, EraseCandidateCommand command, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(command with { Id = id }, ct);
                return result.IsSuccess ? TypedResults.NoContent() : (ProblemHttpResult)result.Error.ToProblem();
            })
            .RequirePermission(RecruitmentPermissions.Candidates.Privacy)
            .WithName("EraseCandidate")
            .WithSummary("Anonymize a candidate at once on request; active applications are withdrawn")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    /// <summary>The renewal page under <c>/api/public/{tenantCode}/consents/{token}</c> (D20, limited per IP).</summary>
    public static void MapPublicConsentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // The token is the route's last segment: a group pattern with "" would end in a slash, off the D20 list
        var consents = endpoints.MapGroup("/api/public/{tenantCode}/consents")
            .WithTags("consents")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingExtensions.PublicPolicy);

        consents.MapGet("/{token}", async Task<Results<Ok<ConsentRenewalPage>, ProblemHttpResult>> ([FromRoute] string tenantCode, [FromRoute] string token, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new GetConsentRenewalPageQuery(token), ct);
                return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
            })
            .WithName("GetConsentRenewalPage")
            .WithSummary("The explicit consent text the link in the renewal e-mail asks the candidate to renew")
            .ProducesProblem(StatusCodes.Status404NotFound);

        consents.MapPost("/{token}", async Task<Results<NoContent, ProblemHttpResult>> ([FromRoute] string tenantCode, [FromRoute] string token, RespondToConsentRenewalCommand command, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(command with { Token = token }, ct);
                return result.IsSuccess ? TypedResults.NoContent() : (ProblemHttpResult)result.Error.ToProblem();
            })
            // Anonymous: no cookie authenticates the request, so cross-site request forgery does not apply.
            .DisableAntiforgery()
            .WithName("RespondToConsentRenewal")
            .WithSummary("Renew or refuse the pool consent with the link")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static IResult Download(HttpContext context, Result<RecruitmentFileDownload> result) =>
        result.IsSuccess
            ? FileTransfer.Content(context, result.Value.FileName, result.Value.ContentType, result.Value.Length, result.Value.Content)
            : result.Error.ToProblem();
}
