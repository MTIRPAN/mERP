using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Approvals;
using Merp.Modules.Platform.Contracts.Approvals;
using Merp.Modules.Platform.Domain.Approvals;
using Merp.SharedKernel.Results;
using Merp.Web.Endpoints;
using Merp.Web.Errors;
using Merp.Web.RateLimiting;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.Platform.Api.Endpoints;

/// <summary>
/// The approval engine (PLAN P10b, docs/domain/yetki-ve-onay.md §2, D34). The caller's tasks, decisions, requests
/// they take part in, delegations and the preview need a signed-in user (D22); flows need
/// <c>platform.approvals.definitions.*</c> and administration <c>platform.approvals.admin</c>, both with the Tenant scope.
/// </summary>
internal static class ApprovalEndpoints
{
    public static void MapApprovalEndpoints(this IEndpointRouteBuilder group)
    {
        var approvals = group.MapGroup("/approvals");

        approvals.MapGet("/document-types", ListDocumentTypesAsync)
            .RequireAuthenticatedUser()
            .WithName("ListApprovalDocumentTypes")
            .WithSummary("Approvable document types of all modules with their facts");

        approvals.MapGet("/resolvers", ListResolversAsync)
            .RequirePermission(PlatformPermissions.ApprovalDefinitions.Read)
            .WithName("ListApproverResolvers")
            .WithSummary("Approver resolvers of all modules with their parameters")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        approvals.MapGet("/definitions", ListDefinitionsAsync)
            .RequirePermission(PlatformPermissions.ApprovalDefinitions.Read)
            .WithName("ListApprovalDefinitions")
            .WithSummary("Approval flows of a document type, or of all")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        approvals.MapGet("/definitions/{id:guid}", GetDefinitionAsync)
            .RequirePermission(PlatformPermissions.ApprovalDefinitions.Read)
            .WithName("GetApprovalDefinition")
            .WithSummary("An approval flow with its steps")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        approvals.MapPost("/definitions", CreateDefinitionAsync)
            .RequirePermission(PlatformPermissions.ApprovalDefinitions.Manage)
            .WithName("CreateApprovalDefinition")
            .WithSummary("Define an approval flow for every company or one")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        approvals.MapPut("/definitions/{id:guid}", UpdateDefinitionAsync)
            .RequirePermission(PlatformPermissions.ApprovalDefinitions.Manage)
            .WithName("UpdateApprovalDefinition")
            .WithSummary("Change an approval flow; running requests keep their steps")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        approvals.MapPost("/preview", PreviewAsync)
            .RequireAuthenticatedUser()
            .WithName("PreviewApproval")
            .WithSummary("Who would approve a document of the caller now")
            .ProducesValidationProblem();

        approvals.MapGet("/my-tasks", ListMyTasksAsync)
            .RequireAuthenticatedUser()
            .WithName("ListMyApprovalTasks")
            .WithSummary("The caller's open approval tasks and those of the approvers they act for");

        approvals.MapGet("/my-tasks/count", CountMyTasksAsync)
            .RequireAuthenticatedUser()
            .WithName("CountMyApprovalTasks")
            .WithSummary("The number on the approval badge");

        approvals.MapPost("/tasks/{id:guid}/decision", DecideAsync)
            .RequireAuthenticatedUser()
            .WithName("DecideApprovalTask")
            .WithSummary("Approve, reject or return a task as its approver or their delegate")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        approvals.MapPost("/tasks/{id:guid}/reassign", ReassignAsync)
            .RequirePermission(PlatformPermissions.Approvals.Admin)
            .WithName("ReassignApprovalTask")
            .WithSummary("Move an open task to another user")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        approvals.MapGet("/requests", ListRequestsAsync)
            .RequirePermission(PlatformPermissions.Approvals.Admin)
            .WithName("ListApprovalRequests")
            .WithSummary("Approval requests of all modules, newest first")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        approvals.MapGet("/requests/{id:guid}", GetRequestAsync)
            .RequireAuthenticatedUser()
            .WithName("GetApprovalRequest")
            .WithSummary("A request with its tasks and timeline")
            .ProducesProblem(StatusCodes.Status404NotFound);

        approvals.MapPost("/requests/{id:guid}/summary", SummarizeAsync)
            .RequireAuthenticatedUser()
            .RequireRateLimiting(RateLimitingExtensions.AiPolicy)
            .WithName("SummarizeApprovalRequest")
            .WithSummary("Summarize a request with its attention points with AI for the approver who decides it (A8, D61)")
            .WithDescription("Nothing is stored. Names are replaced and identifier facts are left out before the request reaches the AI provider.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        approvals.MapGet("/timeline", GetTimelineAsync)
            .RequireAuthenticatedUser()
            .WithName("GetApprovalTimeline")
            .WithSummary("The approval request of a document with its timeline")
            .ProducesProblem(StatusCodes.Status404NotFound);

        approvals.MapPost("/requests/{id:guid}/force-approve", ForceApproveAsync)
            .RequirePermission(PlatformPermissions.Approvals.Admin)
            .WithName("ForceApproveApprovalRequest")
            .WithSummary("Approve the current step in place of its approvers")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        approvals.MapPost("/requests/{id:guid}/approvers", AssignApproversAsync)
            .RequirePermission(PlatformPermissions.Approvals.Admin)
            .WithName("AssignApprovers")
            .WithSummary("Give approvers to a request that needs attention")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        approvals.MapGet("/delegations", ListDelegationsAsync)
            .RequireAuthenticatedUser()
            .WithName("ListMyDelegations")
            .WithSummary("Delegations the caller gave and received");

        approvals.MapPost("/delegations", CreateDelegationAsync)
            .RequireAuthenticatedUser()
            .WithName("CreateDelegation")
            .WithSummary("Hand the caller's approvals to another user for a period")
            .ProducesValidationProblem();

        approvals.MapPost("/delegations/{id:guid}/revoke", RevokeDelegationAsync)
            .RequireAuthenticatedUser()
            .WithName("RevokeDelegation")
            .WithSummary("End one of the caller's delegations")
            .ProducesProblem(StatusCodes.Status404NotFound);

        approvals.MapPost("/test-documents", SubmitTestDocumentAsync)
            .RequirePermission(PlatformPermissions.ApprovalDefinitions.Manage)
            .WithName("SubmitTestApprovalDocument")
            .WithSummary("Send a test document through the flows (only where test documents are enabled)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        approvals.MapGet("/users", SearchUsersAsync)
            .RequireAuthenticatedUser()
            .WithName("SearchApprovalUsers")
            .WithSummary("Active users to pick as a delegate or an approver");
    }

    private static async Task<Results<Ok<IReadOnlyList<ApprovalDocumentTypeItem>>, ProblemHttpResult>> ListDocumentTypesAsync(ISender sender, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetApprovalDocumentTypesQuery(), cancellationToken));

    private static async Task<Results<Ok<IReadOnlyList<ApproverResolverItem>>, ProblemHttpResult>> ListResolversAsync(ISender sender, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetApproverResolversQuery(), cancellationToken));

    private static async Task<Results<Ok<IReadOnlyList<ApprovalDefinitionListItem>>, ProblemHttpResult>> ListDefinitionsAsync(
        string? documentType,
        ISender sender,
        CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetApprovalDefinitionsQuery(documentType), cancellationToken));

    private static async Task<Results<Ok<ApprovalDefinitionDetails>, ProblemHttpResult>> GetDefinitionAsync(Guid id, ISender sender, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetApprovalDefinitionQuery(id), cancellationToken));

    private static async Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> CreateDefinitionAsync(
        CreateApprovalDefinitionCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/platform/approvals/definitions/{result.Value}", new EntityCreatedResponse(result.Value))
            : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UpdateDefinitionAsync(
        Guid id,
        UpdateApprovalDefinitionCommand command,
        ISender sender,
        CancellationToken cancellationToken) =>
        NoContentOrProblem(await sender.Send(command with { Id = id }, cancellationToken));

    private static async Task<Results<Ok<ApprovalPreview>, ProblemHttpResult>> PreviewAsync(
        PreviewApprovalQuery query,
        ISender sender,
        CancellationToken cancellationToken) =>
        Respond(await sender.Send(query, cancellationToken));

    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Rows per page, at most 200.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<PagedResult<MyApprovalTaskItem>>, ProblemHttpResult>> ListMyTasksAsync(
        int? page,
        int? pageSize,
        ISender sender,
        CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetMyApprovalTasksQuery(new PageRequest(page, pageSize)), cancellationToken));

    private static async Task<Results<Ok<ApprovalTaskCount>, ProblemHttpResult>> CountMyTasksAsync(ISender sender, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetMyApprovalTaskCountQuery(), cancellationToken));

    private static async Task<Results<NoContent, ProblemHttpResult>> DecideAsync(
        Guid id,
        DecideApprovalTaskCommand command,
        ISender sender,
        CancellationToken cancellationToken) =>
        NoContentOrProblem(await sender.Send(command with { TaskId = id }, cancellationToken));

    private static async Task<Results<NoContent, ProblemHttpResult>> ReassignAsync(
        Guid id,
        ReassignApprovalTaskCommand command,
        ISender sender,
        CancellationToken cancellationToken) =>
        NoContentOrProblem(await sender.Send(command with { TaskId = id }, cancellationToken));

    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Rows per page, at most 200.</param>
    /// <param name="status">Only requests in this state, e.g. NeedsAttention.</param>
    /// <param name="documentType">Only requests of this document type.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<PagedResult<ApprovalRequestListItem>>, ProblemHttpResult>> ListRequestsAsync(
        int? page,
        int? pageSize,
        ApprovalStatus? status,
        string? documentType,
        ISender sender,
        CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetApprovalRequestsQuery(new PageRequest(page, pageSize), status, documentType), cancellationToken));

    private static async Task<Results<Ok<ApprovalRequestDetails>, ProblemHttpResult>> GetRequestAsync(Guid id, ISender sender, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetApprovalRequestQuery(id), cancellationToken));

    private static async Task<Results<Ok<ApprovalSummaryResponse>, ProblemHttpResult>> SummarizeAsync(Guid id, ISender sender, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new SummarizeApprovalRequestQuery(id), cancellationToken));

    private static async Task<Results<Ok<ApprovalRequestDetails>, ProblemHttpResult>> GetTimelineAsync(
        string? documentType,
        string? documentId,
        ISender sender,
        CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetApprovalTimelineQuery(documentType, documentId), cancellationToken));

    private static async Task<Results<NoContent, ProblemHttpResult>> ForceApproveAsync(
        Guid id,
        ForceApproveApprovalCommand command,
        ISender sender,
        CancellationToken cancellationToken) =>
        NoContentOrProblem(await sender.Send(command with { RequestId = id }, cancellationToken));

    private static async Task<Results<NoContent, ProblemHttpResult>> AssignApproversAsync(
        Guid id,
        AssignApproversCommand command,
        ISender sender,
        CancellationToken cancellationToken) =>
        NoContentOrProblem(await sender.Send(command with { RequestId = id }, cancellationToken));

    private static async Task<Results<Ok<IReadOnlyList<DelegationItem>>, ProblemHttpResult>> ListDelegationsAsync(ISender sender, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetMyDelegationsQuery(), cancellationToken));

    private static async Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> CreateDelegationAsync(
        CreateDelegationCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created("/api/platform/approvals/delegations", new EntityCreatedResponse(result.Value))
            : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RevokeDelegationAsync(Guid id, ISender sender, CancellationToken cancellationToken) =>
        NoContentOrProblem(await sender.Send(new RevokeDelegationCommand { Id = id }, cancellationToken));

    private static async Task<Results<Ok<IReadOnlyList<ApprovalUserOption>>, ProblemHttpResult>> SearchUsersAsync(
        string? search,
        Guid[]? ids,
        ISender sender,
        CancellationToken cancellationToken) =>
        Respond(await sender.Send(new SearchApprovalUsersQuery(search, ids), cancellationToken));

    private static async Task<Results<Ok<TestApprovalDocumentResponse>, ProblemHttpResult>> SubmitTestDocumentAsync(
        SubmitTestApprovalCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(new TestApprovalDocumentResponse(result.Value)) : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static Results<Ok<T>, ProblemHttpResult> Respond<T>(Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();

    private static Results<NoContent, ProblemHttpResult> NoContentOrProblem(Result result) =>
        result.IsSuccess ? TypedResults.NoContent() : (ProblemHttpResult)result.Error.ToProblem();
}

/// <summary>Id of the test document sent for approval.</summary>
public sealed record TestApprovalDocumentResponse(string DocumentId);
