using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.HR.Contracts.Employees;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Application.Applications;
using Merp.Modules.Recruitment.Application.Candidates;
using Merp.Modules.Recruitment.Application.Careers;
using Merp.Modules.Recruitment.Application.Requisitions;
using Merp.SharedKernel.Results;
using Merp.Web.Endpoints;
using Merp.Web.Errors;
using Merp.Web.Files;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.Recruitment.Api.Endpoints;

/// <summary>
/// The candidate pool (PLAN R4b, D50): search, the card, profile and tags, CVs, notes with mentions, merging duplicates
/// and adding a candidate to another opening; the hiring team reads and notes the candidates of its openings.
/// </summary>
internal static class CandidateEndpoints
{
    public static void MapCandidateEndpoints(this IEndpointRouteBuilder group)
    {
        var candidates = group.MapGroup("/candidates");

        candidates.MapGet("", ListAsync)
            .RequirePermission(RecruitmentPermissions.Candidates.Read)
            .WithName("ListCandidates")
            .WithSummary("The candidate pool, newest first; the search covers names, profile, tags, notes and CV text")
            .ProducesValidationProblem();

        candidates.MapPost("", (CreateCandidateCommand command, ISender sender, CancellationToken ct) =>
                CreatedAsync(sender.Send(command, ct), id => $"/api/recruitment/candidates/{id}"))
            .RequirePermission(RecruitmentPermissions.Candidates.Manage)
            .WithName("CreateCandidate")
            .WithSummary("Add a candidate to the pool; the e-mail of another candidate is refused")
            .ProducesValidationProblem();

        candidates.MapGet("/options", async Task<Results<Ok<CandidateOptions>, ProblemHttpResult>> (ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetCandidateOptionsQuery(), ct)))
            .RequirePermission(RecruitmentPermissions.Candidates.Read)
            .WithName("GetCandidateOptions")
            .WithSummary("The active tags and candidate sources to choose from");

        candidates.MapGet("/referrers", async Task<Results<Ok<IReadOnlyList<EmployeeSummary>>, ProblemHttpResult>> (Guid companyId, string? search, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new SearchCandidateReferrersQuery(companyId, search), ct)))
            .RequirePermission(RecruitmentPermissions.Candidates.Manage)
            .WithName("SearchCandidateReferrers")
            .WithSummary("Employees of a company in scope by name or registry number, to pick the referrer (at most 20)")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        candidates.MapGet("/mention-users", async Task<Results<Ok<IReadOnlyList<RequisitionUserOption>>, ProblemHttpResult>> (string? search, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new SearchMentionUsersQuery(search), ct)))
            .RequirePermission(RecruitmentPermissions.Candidates.Read)
            .WithName("SearchCandidateMentionUsers")
            .WithSummary("Active users by name to mention in a note (at most 20)");

        candidates.MapGet("/{id:guid}", async Task<Results<Ok<CandidateDetails>, ProblemHttpResult>> (Guid id, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetCandidateQuery(id), ct)))
            .RequirePermission(RecruitmentPermissions.Candidates.Read)
            .WithName("GetCandidate")
            .WithSummary("A candidate's card: profile, tags, consent, applications in scope, CVs, notes, possible duplicates, timeline")
            .ProducesProblem(StatusCodes.Status404NotFound);

        candidates.MapPut("/{id:guid}", (Guid id, UpdateCandidateCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Id = id }, ct)))
            .RequirePermission(RecruitmentPermissions.Candidates.Manage)
            .WithName("UpdateCandidate")
            .WithSummary("Correct a candidate's contact details, profile, source and tags")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        candidates.MapPost("/{id:guid}/cvs", UploadCvAsync)
            .RequirePermission(RecruitmentPermissions.Candidates.Manage)
            .WithMetadata(new RequestSizeLimitAttribute(FileTransfer.RequestLimit(CareerCatalog.CvRules.MaxBytes)))
            // Authentication is a bearer token, never a cookie, so cross-site request forgery does not apply.
            .DisableAntiforgery()
            .WithName("UploadCandidateCv")
            .WithSummary("Upload a newer CV (PDF or DOCX, at most 5 MB); its text is searched once it is read")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        candidates.MapGet("/{id:guid}/cvs/{fileId:guid}", async (Guid id, Guid fileId, HttpContext context, ISender sender, CancellationToken ct) =>
                Download(context, await sender.Send(new GetCandidateFileQuery(id, fileId), ct)))
            .RequirePermission(RecruitmentPermissions.Candidates.Read)
            .WithName("DownloadCandidateCv")
            .WithSummary("A CV of the candidate")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound);

        candidates.MapPost("/{id:guid}/notes", (Guid id, AddCandidateNoteCommand command, ISender sender, CancellationToken ct) =>
                CreatedAsync(sender.Send(command with { Id = id }, ct), _ => $"/api/recruitment/candidates/{id}"))
            .RequirePermission(RecruitmentPermissions.Candidates.Manage)
            .WithName("AddCandidateNote")
            .WithSummary("Add a note; the users mentioned are notified")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        candidates.MapDelete("/{id:guid}/notes/{noteId:guid}", (Guid id, Guid noteId, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(new RemoveCandidateNoteCommand(id, noteId), ct)))
            .RequirePermission(RecruitmentPermissions.Candidates.Manage)
            .WithName("RemoveCandidateNote")
            .WithSummary("Remove a note; only its author does")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        candidates.MapPost("/{id:guid}/merge", (Guid id, MergeCandidatesCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Id = id }, ct)))
            .RequirePermission(RecruitmentPermissions.Candidates.Manage)
            .WithName("MergeCandidates")
            .WithSummary("Take a duplicate into this candidate: its applications, CVs and notes move, then it is removed")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        candidates.MapPost("/{id:guid}/applications", (Guid id, AddCandidateToOpeningCommand command, ISender sender, CancellationToken ct) =>
                CreatedAsync(sender.Send(command with { Id = id }, ct), applicationId => $"/api/recruitment/applications/{applicationId}"))
            .RequirePermission(RecruitmentPermissions.Applications.Manage)
            .WithName("AddCandidateToOpening")
            .WithSummary("Add the candidate to an opening (published or on hold) of a company in scope")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    /// <summary>The hiring team reads and notes the candidates of its openings (D50).</summary>
    public static void MapHiringTeamCandidateEndpoints(this IEndpointRouteBuilder group)
    {
        var hiring = group.MapGroup("/me/hiring");

        hiring.MapGet("/candidates/{id:guid}", async Task<Results<Ok<CandidateDetails>, ProblemHttpResult>> (Guid id, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetCandidateQuery(id) { AsTeam = true }, ct)))
            .RequirePermission(RecruitmentPermissions.Self.Hiring)
            .WithName("GetMyHiringCandidate")
            .WithSummary("A candidate of an opening of my hiring team, with the applications to the team's openings")
            .ProducesProblem(StatusCodes.Status404NotFound);

        hiring.MapPost("/candidates/{id:guid}/notes", (Guid id, AddCandidateNoteCommand command, ISender sender, CancellationToken ct) =>
                CreatedAsync(sender.Send(command with { Id = id, AsTeam = true }, ct), _ => $"/api/recruitment/me/hiring/candidates/{id}"))
            .RequirePermission(RecruitmentPermissions.Self.Hiring)
            .WithName("AddMyHiringCandidateNote")
            .WithSummary("Add a note on a candidate of an opening of my hiring team")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        hiring.MapDelete("/candidates/{id:guid}/notes/{noteId:guid}", (Guid id, Guid noteId, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(new RemoveCandidateNoteCommand(id, noteId) { AsTeam = true }, ct)))
            .RequirePermission(RecruitmentPermissions.Self.Hiring)
            .WithName("RemoveMyHiringCandidateNote")
            .WithSummary("Remove my note on a candidate of an opening of my hiring team")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        hiring.MapGet("/mention-users", async Task<Results<Ok<IReadOnlyList<RequisitionUserOption>>, ProblemHttpResult>> (string? search, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new SearchMentionUsersQuery(search) { AsTeam = true }, ct)))
            .RequirePermission(RecruitmentPermissions.Self.Hiring)
            .WithName("SearchMyHiringMentionUsers")
            .WithSummary("Active users by name to mention in a note (at most 20)");
    }

    /// <param name="page">1-based page.</param>
    /// <param name="pageSize">Rows per page, at most 200.</param>
    /// <param name="sort">createdAt, lastAppliedAt or name; a leading - sorts descending.</param>
    /// <param name="search">Words of the name, profile, tags, notes or CV text, or a part of the name or e-mail.</param>
    /// <param name="tag">Only candidates with this tag code.</param>
    /// <param name="source">Only candidates from this source code.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<PagedResult<CandidateListItem>>, ProblemHttpResult>> ListAsync(
        int? page,
        int? pageSize,
        string? sort,
        string? search,
        string? tag,
        string? source,
        ISender sender,
        CancellationToken cancellationToken) =>
        OkOrProblem(await sender.Send(new GetCandidatesQuery(new PageRequest(page, pageSize, sort), search, tag, source), cancellationToken));

    /// <param name="id">The candidate.</param>
    /// <param name="cv">The CV.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> UploadCvAsync(
        Guid id,
        IFormFile? cv,
        ISender sender,
        CancellationToken cancellationToken) =>
        CreatedAsync(sender.Send(new UploadCandidateCvCommand(id, cv.ToUpload()), cancellationToken), fileId => $"/api/recruitment/candidates/{id}/cvs/{fileId}");

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
