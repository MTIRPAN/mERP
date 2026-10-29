using Merp.Application.Abstractions.Messaging;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Application.CvAnalysis;
using Merp.Web.Errors;
using Merp.Web.RateLimiting;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.Recruitment.Api.Endpoints;

internal static class CvAnalysisEndpoints
{
    // Room for the 5 MB file plus the other form fields; larger requests are rejected before they are read.
    private const long MaxRequestBytes = AnalyzeCvQuery.MaxFileBytes + (64 * 1024);

    public static void MapCvAnalysisEndpoints(this IEndpointRouteBuilder group)
    {
        MapStatelessAnalysis(group);

        group.MapPost("/applications/{id:guid}/cv-analysis", async Task<Results<Ok<ApplicationCvAnalysisItem>, ProblemHttpResult>> (Guid id, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new AnalyzeApplicationCvQuery(id), ct);
                return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
            })
            .RequirePermission(RecruitmentPermissions.Cv.Analyze)
            .RequireRateLimiting(RateLimitingExtensions.AiPolicy)
            .WithName("AnalyzeApplicationCv")
            .WithSummary("Analyze an application's CV against its opening with AI again and keep the result (A3, D51)")
            .WithDescription("Needs the read permission over the application's company. A failed run leaves the previous analysis. The result is a suggestion for the team.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/candidates/cv-prefill", PrefillAsync)
            .RequirePermission(RecruitmentPermissions.Candidates.Manage)
            .RequireRateLimiting(RateLimitingExtensions.AiPolicy)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            // Authentication is a bearer token, never a cookie, so cross-site request forgery does not apply.
            .DisableAntiforgery()
            .WithName("PrefillCandidateFromCv")
            .WithSummary("Read a CV for the new candidate form: contact details locally, the profile with AI when it is on (A3, D51)")
            .WithDescription("The file is not stored. The contact details never reach the AI provider.")
            .ProducesValidationProblem();
    }

    private static void MapStatelessAnalysis(IEndpointRouteBuilder group) =>
        group.MapPost("/cv-analyses", AnalyzeAsync)
            .RequirePermission(RecruitmentPermissions.Cv.Analyze)
            .RequireRateLimiting(RateLimitingExtensions.AiPolicy)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            // Authentication is a bearer token, never a cookie, so cross-site request forgery does not apply.
            .DisableAntiforgery()
            .WithName("AnalyzeCv")
            .WithSummary("Analyze a CV with AI and match it against job requirements (D24)")
            .WithDescription("The file is not stored. Contact details, identifiers, the name and sensitive fields are removed before the text is sent to the AI provider. The result is a suggestion for the recruiter.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    /// <param name="file">The CV, PDF or DOCX, at most 5 MB.</param>
    /// <param name="jobTitle">Optional position title.</param>
    /// <param name="jobRequirements">Optional requirements to match against.</param>
    /// <param name="candidateName">Optional candidate name, redacted before analysis.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<CvAnalysisResponse>, ProblemHttpResult>> AnalyzeAsync(
        IFormFile? file,
        [FromForm] string? jobTitle,
        [FromForm] string? jobRequirements,
        [FromForm] string? candidateName,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new AnalyzeCvQuery(file?.FileName ?? string.Empty, await ReadAsync(file, cancellationToken), jobTitle, jobRequirements, candidateName),
            cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
    }

    /// <param name="cv">The CV, PDF or DOCX, at most 5 MB.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<CandidateCvPrefill>, ProblemHttpResult>> PrefillAsync(IFormFile? cv, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new PrefillCandidateFromCvQuery(await ReadAsync(cv, cancellationToken)), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<byte[]> ReadAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is { Length: > AnalyzeCvQuery.MaxFileBytes })
        {
            // Not read: the validator only needs to know it is too large.
            return new byte[AnalyzeCvQuery.MaxFileBytes + 1];
        }

        if (file is not { Length: > 0 })
        {
            return [];
        }

        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }
}
