using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Application.Applications;
using Merp.Modules.Recruitment.Application.Interviews;
using Merp.Modules.Recruitment.Application.Openings;
using Merp.Modules.Recruitment.Domain.Applications;
using Merp.SharedKernel.Results;
using Merp.Web.Errors;
using Merp.Web.Files;
using Merp.Web.RateLimiting;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.Recruitment.Api.Endpoints;

/// <summary>
/// The pipeline (PLAN R4a, D50): applications of an opening by stage, moving, rejecting, withdrawals and reopening;
/// the hiring team reads its openings' applications from self service.
/// </summary>
internal static class ApplicationEndpoints
{
    public static void MapApplicationEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/openings/{id:guid}/pipeline", async Task<Results<Ok<OpeningPipeline>, ProblemHttpResult>> (Guid id, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetOpeningPipelineQuery(id), ct)))
            .RequirePermission(RecruitmentPermissions.Applications.Read)
            .WithName("GetOpeningPipeline")
            .WithSummary("The pipeline of an opening with the number of applications at each stage")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/openings/{id:guid}/stages", (Guid id, UpdateOpeningStagesCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Id = id }, ct)))
            .RequirePermission(RecruitmentPermissions.Openings.Manage)
            .WithName("UpdateOpeningStages")
            .WithSummary("Change the pipeline of an opening that is not closed; stages with applications stay")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var applications = group.MapGroup("/applications");

        applications.MapGet("", ListAsync)
            .RequirePermission(RecruitmentPermissions.Applications.Read)
            .WithName("ListApplications")
            .WithSummary("Applications to the openings of the companies in scope, newest first")
            .ProducesValidationProblem();

        applications.MapGet("/{id:guid}", async Task<Results<Ok<ApplicationDetails>, ProblemHttpResult>> (Guid id, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetApplicationQuery(id), ct)))
            .RequirePermission(RecruitmentPermissions.Applications.Read)
            .WithName("GetApplication")
            .WithSummary("An application with its answers, CV, consent and history")
            .ProducesProblem(StatusCodes.Status404NotFound);

        applications.MapGet("/{id:guid}/cv", async (Guid id, HttpContext context, ISender sender, CancellationToken ct) =>
                Download(context, await sender.Send(new GetApplicationCvQuery(id), ct)))
            .RequirePermission(RecruitmentPermissions.Applications.Read)
            .WithName("DownloadApplicationCv")
            .WithSummary("The CV sent with an application")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound);

        applications.MapPost("/{id:guid}/move", (Guid id, MoveApplicationCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Id = id }, ct)))
            .RequirePermission(RecruitmentPermissions.Applications.Manage)
            .WithName("MoveApplication")
            .WithSummary("Move an active application to a working stage of its opening, or to Hired")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        applications.MapPost("/{id:guid}/reject", (Guid id, RejectApplicationCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Id = id }, ct)))
            .RequirePermission(RecruitmentPermissions.Applications.Manage)
            .WithName("RejectApplication")
            .WithSummary("Reject an active application for a reason; the e-mail goes now or up to 14 days later")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        applications.MapPost("/{id:guid}/withdraw", (Guid id, WithdrawApplicationCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Id = id }, ct)))
            .RequirePermission(RecruitmentPermissions.Applications.Manage)
            .WithName("WithdrawApplication")
            .WithSummary("Record that the candidate withdrew an active application, for a reason")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        applications.MapPost("/{id:guid}/reopen", (Guid id, ReopenApplicationCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Id = id }, ct)))
            .RequirePermission(RecruitmentPermissions.Applications.Manage)
            .WithName("ReopenApplication")
            .WithSummary("Bring a rejected or withdrawn application back to the stage it left from")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        applications.MapPost("/{id:guid}/feedback-summary", async Task<Results<Ok<FeedbackSummaryResponse>, ProblemHttpResult>> (Guid id, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new SummarizeFeedbackQuery(id), ct)))
            .RequirePermission(RecruitmentPermissions.Applications.Read)
            .RequireRateLimiting(RateLimitingExtensions.AiPolicy)
            .WithName("SummarizeApplicationFeedback")
            .WithSummary("Summarize the interviewers' forms of an application with AI once every form is in (A5, D58)")
            .WithDescription("Nothing is stored. The candidate's and the interviewers' names are replaced before the forms reach the AI provider. 409 while forms are missing (the blind rule).")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        applications.MapPost("/{id:guid}/rejection-email-draft", async Task<Results<Ok<CandidateEmailDraft>, ProblemHttpResult>> (Guid id, DraftCandidateEmailQuery query, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(query with { ApplicationId = id, Kind = CandidateEmailKind.Rejection }, ct)))
            .RequirePermission(RecruitmentPermissions.Applications.Manage)
            .RequireRateLimiting(RateLimitingExtensions.AiPolicy)
            .WithName("DraftRejectionEmail")
            .WithSummary("Personalize the rejection e-mail of an application with AI (A5, D58)")
            .WithDescription("Nothing is sent: the edited text goes with the rejection. The candidate's name and contact details are not sent to the AI provider; the text keeps the template's placeholders.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        applications.MapPost("/{id:guid}/invitation-email-draft", async Task<Results<Ok<CandidateEmailDraft>, ProblemHttpResult>> (Guid id, DraftCandidateEmailQuery query, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(query with { ApplicationId = id, Kind = CandidateEmailKind.InterviewInvitation }, ct)))
            .RequirePermission(RecruitmentPermissions.Interviews.Manage)
            .RequireRateLimiting(RateLimitingExtensions.AiPolicy)
            .WithName("DraftInterviewInvitationEmail")
            .WithSummary("Personalize the interview invitation e-mail of an application with AI (A5, D58)")
            .WithDescription("Nothing is sent: the edited text goes with the interview being planned. The date, time and place stay placeholders.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>The hiring manager and the recruiters of an opening read its applications (D50).</summary>
    public static void MapHiringTeamEndpoints(this IEndpointRouteBuilder group)
    {
        var hiring = group.MapGroup("/me/hiring");

        hiring.MapGet("/openings", async Task<Results<Ok<IReadOnlyList<OpeningListItem>>, ProblemHttpResult>> (ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetHiringOpeningsQuery(), ct)))
            .RequirePermission(RecruitmentPermissions.Self.Hiring)
            .WithName("ListMyHiringOpenings")
            .WithSummary("The openings whose hiring team I am in, with the applications at each stage");

        hiring.MapGet("/openings/{id:guid}/pipeline", async Task<Results<Ok<OpeningPipeline>, ProblemHttpResult>> (Guid id, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetOpeningPipelineQuery(id) { AsTeam = true }, ct)))
            .RequirePermission(RecruitmentPermissions.Self.Hiring)
            .WithName("GetMyHiringOpeningPipeline")
            .WithSummary("The pipeline of an opening of my hiring team")
            .ProducesProblem(StatusCodes.Status404NotFound);

        hiring.MapGet("/applications", ListForTeamAsync)
            .RequirePermission(RecruitmentPermissions.Self.Hiring)
            .WithName("ListMyHiringApplications")
            .WithSummary("Applications to an opening of my hiring team")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        hiring.MapGet("/applications/{id:guid}", async Task<Results<Ok<ApplicationDetails>, ProblemHttpResult>> (Guid id, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetApplicationQuery(id) { AsTeam = true }, ct)))
            .RequirePermission(RecruitmentPermissions.Self.Hiring)
            .WithName("GetMyHiringApplication")
            .WithSummary("An application to an opening of my hiring team")
            .ProducesProblem(StatusCodes.Status404NotFound);

        hiring.MapGet("/applications/{id:guid}/cv", async (Guid id, HttpContext context, ISender sender, CancellationToken ct) =>
                Download(context, await sender.Send(new GetApplicationCvQuery(id) { AsTeam = true }, ct)))
            .RequirePermission(RecruitmentPermissions.Self.Hiring)
            .WithName("DownloadMyHiringApplicationCv")
            .WithSummary("The CV sent with an application to an opening of my hiring team")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound);

        hiring.MapPost("/applications/{id:guid}/feedback-summary", async Task<Results<Ok<FeedbackSummaryResponse>, ProblemHttpResult>> (Guid id, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new SummarizeFeedbackQuery(id) { AsTeam = true }, ct)))
            .RequirePermission(RecruitmentPermissions.Self.Hiring)
            .RequireRateLimiting(RateLimitingExtensions.AiPolicy)
            .WithName("SummarizeMyHiringApplicationFeedback")
            .WithSummary("Summarize the interviewers' forms of an application to an opening of my hiring team with AI (A5, D58)")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    /// <param name="page">1-based page.</param>
    /// <param name="pageSize">Rows per page, at most 200.</param>
    /// <param name="sort">appliedAt, stageEnteredAt or name; a leading - sorts descending.</param>
    /// <param name="openingId">Only the applications to this opening.</param>
    /// <param name="stageId">Only the applications at this stage.</param>
    /// <param name="status">Only these states (repeatable).</param>
    /// <param name="knockedOut">Only applications that missed (true) or passed (false) the knockout questions.</param>
    /// <param name="search">Part of the candidate's name or e-mail.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<PagedResult<ApplicationListItem>>, ProblemHttpResult>> ListAsync(
        int? page,
        int? pageSize,
        string? sort,
        Guid? openingId,
        Guid? stageId,
        ApplicationStatus[]? status,
        bool? knockedOut,
        string? search,
        ISender sender,
        CancellationToken cancellationToken) =>
        OkOrProblem(await sender.Send(new GetApplicationsQuery(new PageRequest(page, pageSize, sort), openingId, stageId, status, knockedOut, search), cancellationToken));

    /// <param name="openingId">The opening of my hiring team.</param>
    /// <param name="page">1-based page.</param>
    /// <param name="pageSize">Rows per page, at most 200.</param>
    /// <param name="sort">appliedAt, stageEnteredAt or name; a leading - sorts descending.</param>
    /// <param name="stageId">Only the applications at this stage.</param>
    /// <param name="status">Only these states (repeatable).</param>
    /// <param name="knockedOut">Only applications that missed (true) or passed (false) the knockout questions.</param>
    /// <param name="search">Part of the candidate's name or e-mail.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<PagedResult<ApplicationListItem>>, ProblemHttpResult>> ListForTeamAsync(
        Guid? openingId,
        int? page,
        int? pageSize,
        string? sort,
        Guid? stageId,
        ApplicationStatus[]? status,
        bool? knockedOut,
        string? search,
        ISender sender,
        CancellationToken cancellationToken) =>
        OkOrProblem(await sender.Send(
            new GetApplicationsQuery(new PageRequest(page, pageSize, sort), openingId, stageId, status, knockedOut, search) { AsTeam = true },
            cancellationToken));

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
}
