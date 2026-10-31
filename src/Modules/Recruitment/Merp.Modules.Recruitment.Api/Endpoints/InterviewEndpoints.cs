using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Application.Applications;
using Merp.Modules.Recruitment.Application.Interviews;
using Merp.Modules.Recruitment.Application.Requisitions;
using Merp.Modules.Recruitment.Domain.Interviews;
using Merp.SharedKernel.Results;
using Merp.Web.Endpoints;
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
/// Interviews and evaluations (PLAN R5, D52): planning with invitations, conflicts with other interviews, the
/// interviewers' forms under the blind rule, and the interviewer's own list.
/// </summary>
internal static class InterviewEndpoints
{
    public static void MapInterviewEndpoints(this IEndpointRouteBuilder group)
    {
        var interviews = group.MapGroup("/interviews");

        interviews.MapGet("", ListAsync)
            .RequirePermission(RecruitmentPermissions.Interviews.Read)
            .WithName("ListInterviews")
            .WithSummary("Interviews of the companies in scope and the ones I interview in, soonest first")
            .ProducesValidationProblem();

        interviews.MapPost("", (ScheduleInterviewCommand command, ISender sender, CancellationToken ct) =>
                CreatedAsync(sender.Send(command, ct), id => $"/api/recruitment/interviews/{id}"))
            .RequirePermission(RecruitmentPermissions.Interviews.Manage)
            .WithName("ScheduleInterview")
            .WithSummary("Plan an interview and invite the candidate and the interviewers with a calendar file")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        interviews.MapGet("/conflicts", ConflictsAsync)
            .RequirePermission(RecruitmentPermissions.Interviews.Manage)
            .WithName("ListInterviewConflicts")
            .WithSummary("The interviewers' other planned interviews in a time range");

        interviews.MapGet("/users", async Task<Results<Ok<IReadOnlyList<RequisitionUserOption>>, ProblemHttpResult>> (string? search, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new SearchInterviewUsersQuery(search), ct)))
            .RequirePermission(RecruitmentPermissions.Interviews.Manage)
            .WithName("SearchInterviewUsers")
            .WithSummary("Active users by name to interview (at most 20)");

        interviews.MapGet("/templates", async Task<Results<Ok<IReadOnlyList<InterviewTemplateOption>>, ProblemHttpResult>> (ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetInterviewTemplatesQuery(), ct)))
            .RequirePermission(RecruitmentPermissions.Interviews.Manage)
            .WithName("ListInterviewTemplates")
            .WithSummary("The active evaluation templates to plan an interview with");

        interviews.MapGet("/{id:guid}", async Task<Results<Ok<InterviewDetails>, ProblemHttpResult>> (Guid id, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetInterviewQuery(id), ct)))
            .RequirePermission(RecruitmentPermissions.Interviews.Read)
            .WithName("GetInterview")
            .WithSummary("An interview with its form and the forms I may see under the blind rule")
            .ProducesProblem(StatusCodes.Status404NotFound);

        interviews.MapGet("/{id:guid}/cv", async (Guid id, HttpContext context, ISender sender, CancellationToken ct) =>
                Download(context, await sender.Send(new GetInterviewCvQuery(id), ct)))
            .RequirePermission(RecruitmentPermissions.Interviews.Read)
            .WithName("DownloadInterviewCv")
            .WithSummary("The candidate's CV for the interviewers")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound);

        interviews.MapPut("/{id:guid}", (Guid id, UpdateInterviewCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Id = id }, ct)))
            .RequirePermission(RecruitmentPermissions.Interviews.Manage)
            .WithName("UpdateInterview")
            .WithSummary("Change a planned interview; everyone gets the updated invitation")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        interviews.MapPost("/{id:guid}/cancel", (Guid id, CancelInterviewCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Id = id }, ct)))
            .RequirePermission(RecruitmentPermissions.Interviews.Manage)
            .WithName("CancelInterview")
            .WithSummary("Cancel a planned interview; everyone gets the cancellation")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        interviews.MapPost("/{id:guid}/close", (Guid id, CloseInterviewCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Id = id }, ct)))
            .RequirePermission(RecruitmentPermissions.Interviews.Manage)
            .WithName("CloseInterview")
            .WithSummary("Close a planned interview: it took place (Completed) or the candidate did not come (NoShow)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        interviews.MapPut("/{id:guid}/feedback", async Task<Results<Ok<EntityCreatedResponse>, ProblemHttpResult>> (Guid id, SubmitFeedbackCommand command, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(command with { InterviewId = id }, ct);
                return result.IsSuccess ? TypedResults.Ok(new EntityCreatedResponse(result.Value)) : (ProblemHttpResult)result.Error.ToProblem();
            })
            .RequirePermission(RecruitmentPermissions.Feedback.Submit)
            .WithName("SubmitInterviewFeedback")
            .WithSummary("Send or correct my evaluation form for an interview I interview in")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        interviews.MapPost("/{id:guid}/question-suggestions", async Task<Results<Ok<InterviewQuestionsResponse>, ProblemHttpResult>> (Guid id, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new SuggestInterviewQuestionsQuery(id), ct)))
            .RequirePermission(RecruitmentPermissions.Interviews.Read)
            .RequireRateLimiting(RateLimitingExtensions.AiPolicy)
            .WithName("SuggestInterviewQuestions")
            .WithSummary("Suggest questions for an interview from its opening and its form's criteria with AI (A5, D58)")
            .WithDescription("Nothing is stored and nothing about the candidate is sent.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        interviews.MapPost("/{id:guid}/feedback-summary", async Task<Results<Ok<FeedbackSummaryResponse>, ProblemHttpResult>> (Guid id, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new SummarizeFeedbackQuery(id) { ByInterview = true }, ct)))
            .RequirePermission(RecruitmentPermissions.Interviews.Read)
            .RequireRateLimiting(RateLimitingExtensions.AiPolicy)
            .WithName("SummarizeInterviewFeedback")
            .WithSummary("Summarize the interviewers' forms of the interview's application with AI once every form is in (A5, D58)")
            .WithDescription("For those who see every form of the application: HR, the hiring manager, and its interviewers past the blind rule.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    /// <param name="page">1-based page.</param>
    /// <param name="pageSize">Rows per page, at most 200.</param>
    /// <param name="sort">startsAt; a leading - sorts descending.</param>
    /// <param name="applicationId">Only the interviews of this application.</param>
    /// <param name="openingId">Only the interviews of this opening.</param>
    /// <param name="mine">Only the interviews I interview in.</param>
    /// <param name="from">Only interviews ending after this moment.</param>
    /// <param name="to">Only interviews starting before this moment.</param>
    /// <param name="status">Only these states (repeatable).</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<PagedResult<InterviewListItem>>, ProblemHttpResult>> ListAsync(
        int? page,
        int? pageSize,
        string? sort,
        Guid? applicationId,
        Guid? openingId,
        bool? mine,
        DateTimeOffset? from,
        DateTimeOffset? to,
        InterviewStatus[]? status,
        ISender sender,
        CancellationToken cancellationToken) =>
        OkOrProblem(await sender.Send(new GetInterviewsQuery(new PageRequest(page, pageSize, sort), applicationId, openingId, mine ?? false, from, to, status), cancellationToken));

    /// <param name="startsAt">Start of the range.</param>
    /// <param name="endsAt">End of the range.</param>
    /// <param name="interviewerUserIds">The interviewers (repeatable).</param>
    /// <param name="exceptInterviewId">The interview being changed, left out.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<IReadOnlyList<InterviewConflict>>, ProblemHttpResult>> ConflictsAsync(
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        Guid[]? interviewerUserIds,
        Guid? exceptInterviewId,
        ISender sender,
        CancellationToken cancellationToken) =>
        OkOrProblem(await sender.Send(new GetInterviewConflictsQuery(startsAt, endsAt, interviewerUserIds ?? [], exceptInterviewId), cancellationToken));

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
