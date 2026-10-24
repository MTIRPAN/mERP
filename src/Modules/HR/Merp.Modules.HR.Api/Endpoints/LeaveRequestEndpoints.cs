using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.Employees;
using Merp.Modules.HR.Application.Leave;
using Merp.Modules.HR.Domain.Leave;
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

namespace Merp.Modules.HR.Api.Endpoints;

/// <summary>Leave requests (PLAN L4, D44): the employee's own under <c>/me</c>, HR's under <c>/leave-requests</c>.</summary>
internal static class LeaveRequestEndpoints
{
    public static void MapLeaveRequestEndpoints(this IEndpointRouteBuilder group)
    {
        MapMyLeave(group.MapGroup("/me"), group);
        MapHrLeave(group.MapGroup("/leave-requests"), group);
    }

    private static void MapMyLeave(RouteGroupBuilder me, IEndpointRouteBuilder root)
    {
        me.MapGet("/leave-balances", async Task<Results<Ok<IReadOnlyList<LeaveBalanceItem>>, ProblemHttpResult>> (Guid? employmentId, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetMyLeaveBalancesQuery(employmentId), ct)))
            .RequirePermission(HrPermissions.Self.LeaveRead)
            .WithName("ListMyLeaveBalances")
            .WithSummary("The user's leave balances; those of the main employment unless one is named")
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapGet("/leave-types", async Task<Results<Ok<IReadOnlyList<LeaveTypeOption>>, ProblemHttpResult>> (Guid? employmentId, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetMyLeaveTypesQuery(employmentId), ct)))
            .RequirePermission(HrPermissions.Self.LeaveRequest)
            .WithName("ListMyLeaveTypes")
            .WithSummary("The leave types the user may ask for; those of the main employment unless one is named")
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapGet("/colleagues", async Task<Results<Ok<IReadOnlyList<ColleagueItem>>, ProblemHttpResult>> (string? search, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetMyColleaguesQuery(search), ct)))
            .RequirePermission(HrPermissions.Self.LeaveRequest)
            .WithName("SearchMyColleagues")
            .WithSummary("Colleagues working in the user's companies, by name, to stand in during a leave (at most 20)")
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapGet("/leave-requests", ListMineAsync)
            .RequirePermission(HrPermissions.Self.LeaveRead)
            .WithName("ListMyLeaveRequests")
            .WithSummary("The user's leave requests, newest first")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapGet("/leave-requests/{id:guid}", async Task<Results<Ok<LeaveRequestDetails>, ProblemHttpResult>> (Guid id, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetMyLeaveRequestQuery(id), ct)))
            .RequirePermission(HrPermissions.Self.LeaveRead)
            .WithName("GetMyLeaveRequest")
            .WithSummary("One of the user's leave requests with its files and the user's actions")
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapPost("/leave-requests/preview", async Task<Results<Ok<LeaveRequestPreview>, ProblemHttpResult>> (PreviewMyLeaveRequestQuery query, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(query, ct)))
            .RequirePermission(HrPermissions.Self.LeaveRequest)
            .WithName("PreviewMyLeaveRequest")
            .WithSummary("Check a leave request without saving it: length, balance after it, problems, warnings and approvers")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapPost("/leave-requests", (CreateMyLeaveRequestCommand command, ISender sender, CancellationToken ct) =>
                CreatedAsync(sender.Send(command, ct), id => $"/api/hr/me/leave-requests/{id}"))
            .RequirePermission(HrPermissions.Self.LeaveRequest)
            .WithName("CreateMyLeaveRequest")
            .WithSummary("Ask for leave: a draft, or sent for approval at once with submit")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapPut("/leave-requests/{id:guid}", (Guid id, UpdateMyLeaveRequestCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Id = id }, ct)))
            .RequirePermission(HrPermissions.Self.LeaveRequest)
            .WithName("UpdateMyLeaveRequest")
            .WithSummary("Correct a draft or a returned request; with submit, send it")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        me.MapPost("/leave-requests/{id:guid}/submit", (Guid id, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(new SubmitMyLeaveRequestCommand(id), ct)))
            .RequirePermission(HrPermissions.Self.LeaveRequest)
            .WithName("SubmitMyLeaveRequest")
            .WithSummary("Send a draft or a returned request for approval")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        me.MapPost("/leave-requests/{id:guid}/cancel", (Guid id, CancelMyLeaveRequestCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Id = id }, ct)))
            .RequirePermission(HrPermissions.Self.LeaveRequest)
            .WithName("CancelMyLeaveRequest")
            .WithSummary("Cancel a draft or a request not decided yet")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        me.MapPost("/leave-requests/{id:guid}/cancellation", (Guid id, RequestMyLeaveCancellationCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Id = id }, ct)))
            .RequirePermission(HrPermissions.Self.LeaveRequest)
            .WithName("RequestMyLeaveCancellation")
            .WithSummary("Ask to cancel an approved leave before it starts; the cancellation goes through approval")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        me.MapPost("/leave-requests/{id:guid}/files", UploadMineAsync)
            .RequirePermission(HrPermissions.Self.LeaveRequest)
            .WithMetadata(new RequestSizeLimitAttribute(FileTransfer.AttachmentRequestLimit(root)))
            // Authentication is a bearer token, never a cookie, so cross-site request forgery does not apply.
            .DisableAntiforgery()
            .WithName("UploadMyLeaveRequestFile")
            .WithSummary("Attach a file to an open request; Files:AllowedExtensions and Files:MaxBytes apply")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        me.MapDelete("/leave-requests/{id:guid}/files/{fileId:guid}", (Guid id, Guid fileId, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(new DeleteMyLeaveRequestFileCommand(id, fileId), ct)))
            .RequirePermission(HrPermissions.Self.LeaveRequest)
            .WithName("DeleteMyLeaveRequestFile")
            .WithSummary("Delete a file of a draft or a returned request")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        me.MapGet("/leave-requests/{id:guid}/files/{fileId:guid}", async (Guid id, Guid fileId, HttpContext context, ISender sender, CancellationToken ct) =>
                Download(context, await sender.Send(new GetMyLeaveRequestFileQuery(id, fileId), ct)))
            .RequirePermission(HrPermissions.Self.LeaveRead)
            .WithName("DownloadMyLeaveRequestFile")
            .WithSummary("A file of one of the user's requests")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapHrLeave(RouteGroupBuilder requests, IEndpointRouteBuilder root)
    {
        requests.MapGet("/", ListAsync)
            .RequirePermission(HrPermissions.Leave.Requests.Read)
            .WithName("ListLeaveRequests")
            .WithSummary("The leave requests of the employees in the caller's scope; drafts stay with their requesters")
            .ProducesValidationProblem();

        requests.MapGet("/leave-types", async Task<Results<Ok<IReadOnlyList<LeaveTypeOption>>, ProblemHttpResult>> (Guid employmentId, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetLeaveRequestTypesQuery(employmentId), ct)))
            .RequirePermission(HrPermissions.Leave.Requests.Read)
            .WithName("ListLeaveRequestTypes")
            .WithSummary("The leave types an employment in the caller's scope may take")
            .ProducesProblem(StatusCodes.Status404NotFound);

        requests.MapGet("/{id:guid}", async Task<Results<Ok<LeaveRequestDetails>, ProblemHttpResult>> (Guid id, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetLeaveRequestQuery(id), ct)))
            .RequirePermission(HrPermissions.Leave.Requests.Read)
            .WithName("GetLeaveRequest")
            .WithSummary("A leave request in the caller's scope with its files and the caller's actions")
            .ProducesProblem(StatusCodes.Status404NotFound);

        requests.MapPost("/preview", async Task<Results<Ok<LeaveRequestPreview>, ProblemHttpResult>> (PreviewLeaveRequestQuery query, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(query, ct)))
            .RequirePermission(HrPermissions.Leave.Requests.Manage)
            .WithName("PreviewLeaveRequest")
            .WithSummary("Check a request HR would send on an employee's behalf, without saving it")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        requests.MapPost("/", (CreateLeaveRequestOnBehalfCommand command, ISender sender, CancellationToken ct) =>
                CreatedAsync(sender.Send(command, ct), id => $"/api/hr/leave-requests/{id}"))
            .RequirePermission(HrPermissions.Leave.Requests.Manage)
            .WithName("CreateLeaveRequestOnBehalf")
            .WithSummary("Ask for leave on an employee's behalf; it is sent for approval at once")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        requests.MapPost("/records/preview", async Task<Results<Ok<LeaveRequestPreview>, ProblemHttpResult>> (PreviewLeaveRecordQuery query, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(query, ct)))
            .RequirePermission(HrPermissions.Leave.Requests.Record)
            .WithName("PreviewLeaveRecord")
            .WithSummary("Check a leave HR would record, without saving it")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        requests.MapPost("/records", (RecordLeaveCommand command, ISender sender, CancellationToken ct) =>
                CreatedAsync(sender.Send(command, ct), id => $"/api/hr/leave-requests/{id}"))
            .RequirePermission(HrPermissions.Leave.Requests.Record)
            .WithName("RecordLeave")
            .WithSummary("Record a leave approved without a flow, e.g. one taken in the past")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        requests.MapPost("/{id:guid}/cancel", (Guid id, CancelLeaveRequestCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Id = id }, ct)))
            .RequirePermission(HrPermissions.Leave.Requests.Manage)
            .WithName("CancelLeaveRequest")
            .WithSummary("Cancel a request not decided yet, or an approved one that has not started (its days come back)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        requests.MapPost("/{id:guid}/early-return", (Guid id, ReturnFromLeaveEarlyCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Id = id }, ct)))
            .RequirePermission(HrPermissions.Leave.Requests.Manage)
            .WithName("ReturnFromLeaveEarly")
            .WithSummary("End a started leave early; the days no longer taken come back")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        requests.MapPost("/bulk/preview", async Task<Results<Ok<IReadOnlyList<BulkLeaveRow>>, ProblemHttpResult>> (PreviewBulkLeaveQuery query, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(query, ct)))
            .RequirePermission(HrPermissions.Leave.Bulk)
            .WithName("PreviewBulkLeave")
            .WithSummary("Check a bulk leave for each person: the leave counted, the balance after it, problems and warnings")
            .ProducesValidationProblem();

        requests.MapPost("/bulk", CreateBulkAsync)
            .RequirePermission(HrPermissions.Leave.Bulk)
            .WithName("CreateBulkLeave")
            .WithSummary("Record a bulk leave approved for each person without a problem; each person is saved on its own")
            .ProducesValidationProblem();

        requests.MapPost("/{id:guid}/files", UploadAsync)
            .RequirePermission(HrPermissions.Leave.Requests.Manage)
            .WithMetadata(new RequestSizeLimitAttribute(FileTransfer.AttachmentRequestLimit(root)))
            // Authentication is a bearer token, never a cookie, so cross-site request forgery does not apply.
            .DisableAntiforgery()
            .WithName("UploadLeaveRequestFile")
            .WithSummary("Attach a file to a request that was not refused or cancelled")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        requests.MapDelete("/{id:guid}/files/{fileId:guid}", (Guid id, Guid fileId, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(new DeleteLeaveRequestFileCommand(id, fileId), ct)))
            .RequirePermission(HrPermissions.Leave.Requests.Manage)
            .WithName("DeleteLeaveRequestFile")
            .WithSummary("Delete a file of a request")
            .ProducesProblem(StatusCodes.Status404NotFound);

        requests.MapGet("/{id:guid}/files/{fileId:guid}", async (Guid id, Guid fileId, HttpContext context, ISender sender, CancellationToken ct) =>
                Download(context, await sender.Send(new GetLeaveRequestFileQuery(id, fileId), ct)))
            .RequirePermission(HrPermissions.Leave.Requests.Read)
            .WithName("DownloadLeaveRequestFile")
            .WithSummary("A file of a request in the caller's scope")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    /// <param name="page">1-based page.</param>
    /// <param name="pageSize">Rows per page, at most 200.</param>
    /// <param name="sort">startDate, createdAt or number; a leading - sorts descending.</param>
    /// <param name="status">Only these states (repeatable).</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<PagedResult<LeaveRequestListItem>>, ProblemHttpResult>> ListMineAsync(
        int? page,
        int? pageSize,
        string? sort,
        LeaveRequestStatus[]? status,
        ISender sender,
        CancellationToken cancellationToken) =>
        OkOrProblem(await sender.Send(new GetMyLeaveRequestsQuery(new PageRequest(page, pageSize, sort), status), cancellationToken));

    /// <param name="page">1-based page.</param>
    /// <param name="pageSize">Rows per page, at most 200.</param>
    /// <param name="sort">startDate, createdAt, submittedAt or number; a leading - sorts descending.</param>
    /// <param name="companyId">Only requests of this company.</param>
    /// <param name="employeeId">Only requests of this person.</param>
    /// <param name="status">Only these states (repeatable).</param>
    /// <param name="leaveTypeCode">Only requests of this type.</param>
    /// <param name="from">Only requests that end on or after this day.</param>
    /// <param name="to">Only requests that start on or before this day.</param>
    /// <param name="search">Part of the number or of the person's name.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<PagedResult<LeaveRequestListItem>>, ProblemHttpResult>> ListAsync(
        int? page,
        int? pageSize,
        string? sort,
        Guid? companyId,
        Guid? employeeId,
        LeaveRequestStatus[]? status,
        string? leaveTypeCode,
        DateOnly? from,
        DateOnly? to,
        string? search,
        ISender sender,
        CancellationToken cancellationToken) =>
        OkOrProblem(await sender.Send(
            new GetLeaveRequestsQuery(new PageRequest(page, pageSize, sort), companyId, employeeId, status, leaveTypeCode, from, to, search),
            cancellationToken));

    /// <summary>Checks the bulk leave, then records it for each person without a problem, each in its own transaction (rule 7).</summary>
    private static async Task<Results<Ok<IReadOnlyList<BulkLeaveRow>>, ProblemHttpResult>> CreateBulkAsync(
        PreviewBulkLeaveQuery query,
        ISender sender,
        ITextLocalizer localizer,
        CancellationToken cancellationToken)
    {
        var preview = await sender.Send(query, cancellationToken);
        if (preview.IsFailure)
        {
            return (ProblemHttpResult)preview.Error.ToProblem();
        }

        var rows = new List<BulkLeaveRow>();
        foreach (var row in preview.Value)
        {
            if (row.Problem is not null)
            {
                rows.Add(row);
                continue;
            }

            var created = await sender.Send(
                new CreateBulkLeaveCommand(row.EmploymentId, query.LeaveTypeId, query.StartDate, query.EndDate, query.StartPart, query.EndPart, query.Note),
                cancellationToken);
            var field = created.IsFailure && created.Error.Fields.Count > 0 ? created.Error.Fields[0] : null;
            rows.Add(created.IsSuccess
                ? row with { RequestId = created.Value }
                : row with
                {
                    Problem = field is null
                        ? new LeaveRequestProblem(created.Error.Code, localizer.Get(created.Error.Code, created.Error.Args), null)
                        : new LeaveRequestProblem(field.Code, localizer.Get(field.Code, field.Args), field.Field),
                });
        }

        return TypedResults.Ok<IReadOnlyList<BulkLeaveRow>>(rows);
    }

    /// <param name="id">The request.</param>
    /// <param name="file">The file.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> UploadMineAsync(
        Guid id,
        IFormFile? file,
        ISender sender,
        CancellationToken cancellationToken) =>
        CreatedAsync(sender.Send(new UploadMyLeaveRequestFileCommand(id, file.ToUpload()), cancellationToken), fileId => $"/api/hr/me/leave-requests/{id}/files/{fileId}");

    /// <param name="id">The request.</param>
    /// <param name="file">The file.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> UploadAsync(
        Guid id,
        IFormFile? file,
        ISender sender,
        CancellationToken cancellationToken) =>
        CreatedAsync(sender.Send(new UploadLeaveRequestFileCommand(id, file.ToUpload()), cancellationToken), fileId => $"/api/hr/leave-requests/{id}/files/{fileId}");

    private static IResult Download(HttpContext context, Result<HrFileDownload> result) =>
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
