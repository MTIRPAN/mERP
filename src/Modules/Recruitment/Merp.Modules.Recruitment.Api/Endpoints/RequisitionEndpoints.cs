using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.HR.Contracts.Employees;
using Merp.Modules.HR.Contracts.Organization;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Application.Requisitions;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.SharedKernel.Results;
using Merp.Web.Endpoints;
using Merp.Web.Errors;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.Recruitment.Api.Endpoints;

/// <summary>Requisitions (PLAN R2, D48): requests to hire people, approved through the approval engine.</summary>
internal static class RequisitionEndpoints
{
    public static void MapRequisitionEndpoints(this IEndpointRouteBuilder group)
    {
        var requisitions = group.MapGroup("/requisitions");

        requisitions.MapGet("", ListAsync)
            .RequirePermission(RecruitmentPermissions.Requisitions.Read)
            .WithName("ListRequisitions")
            .WithSummary("Requisitions of the companies in scope, newest first")
            .ProducesValidationProblem();

        requisitions.MapGet("/{id:guid}", async Task<Results<Ok<RequisitionDetails>, ProblemHttpResult>> (Guid id, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetRequisitionQuery(id), ct)))
            .RequirePermission(RecruitmentPermissions.Requisitions.Read)
            .WithName("GetRequisition")
            .WithSummary("A requisition; the salary range only for those who manage requisitions and its approvers")
            .ProducesProblem(StatusCodes.Status404NotFound);

        requisitions.MapPost("", (CreateRequisitionCommand command, ISender sender, CancellationToken ct) =>
                CreatedAsync(sender.Send(command, ct), id => $"/api/recruitment/requisitions/{id}"))
            .RequirePermission(RecruitmentPermissions.Requisitions.Manage)
            .WithName("CreateRequisition")
            .WithSummary("Open a requisition as a draft, or send it for approval at once; the position needs enough seats free")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        requisitions.MapPut("/{id:guid}", (Guid id, UpdateRequisitionCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Id = id }, ct)))
            .RequirePermission(RecruitmentPermissions.Requisitions.Manage)
            .WithName("UpdateRequisition")
            .WithSummary("Correct a draft or a returned requisition, and send it when asked")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        requisitions.MapPost("/{id:guid}/submit", (Guid id, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(new SubmitRequisitionCommand(id), ct)))
            .RequirePermission(RecruitmentPermissions.Requisitions.Manage)
            .WithName("SubmitRequisition")
            .WithSummary("Send a draft or a returned requisition for approval")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        requisitions.MapPost("/{id:guid}/cancel", (Guid id, CancelRequisitionCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Id = id }, ct)))
            .RequirePermission(RecruitmentPermissions.Requisitions.Manage)
            .WithName("CancelRequisition")
            .WithSummary("Cancel a requisition not decided yet, or approved and not filled; an open approval is closed")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        requisitions.MapGet("/org-units", async Task<Results<Ok<IReadOnlyList<OrgUnitInfo>>, ProblemHttpResult>> (Guid companyId, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetRequisitionUnitsQuery(companyId), ct)))
            .RequirePermission(RecruitmentPermissions.Requisitions.Manage)
            .WithName("ListRequisitionUnits")
            .WithSummary("Units of a company, for the requisition form")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        requisitions.MapGet("/positions", async Task<Results<Ok<IReadOnlyList<RequisitionPositionItem>>, ProblemHttpResult>> (Guid companyId, Guid? orgUnitId, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetRequisitionPositionsQuery(companyId, orgUnitId), ct)))
            .RequirePermission(RecruitmentPermissions.Requisitions.Manage)
            .WithName("ListRequisitionPositions")
            .WithSummary("Active positions of a company (of a unit and below it), with the seats free after open requisitions")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        requisitions.MapGet("/employees", async Task<Results<Ok<IReadOnlyList<EmployeeSummary>>, ProblemHttpResult>> (Guid companyId, string? search, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new SearchRequisitionEmployeesQuery(companyId, search), ct)))
            .RequirePermission(RecruitmentPermissions.Requisitions.Manage)
            .WithName("SearchRequisitionEmployees")
            .WithSummary("People of a company by name or registry number, for the person replaced (at most 20)")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        requisitions.MapGet("/users", async Task<Results<Ok<IReadOnlyList<RequisitionUserOption>>, ProblemHttpResult>> (string? search, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new SearchRequisitionUsersQuery(search), ct)))
            .RequirePermission(RecruitmentPermissions.Requisitions.Manage)
            .WithName("SearchRequisitionUsers")
            .WithSummary("Active users by name, for the hiring manager (at most 20)");
    }

    /// <param name="page">1-based page.</param>
    /// <param name="pageSize">Rows per page, at most 200.</param>
    /// <param name="sort">createdAt, number or desiredStartDate; a leading - sorts descending.</param>
    /// <param name="companyId">Only requisitions of this company.</param>
    /// <param name="status">Only these states (repeatable).</param>
    /// <param name="search">Part of the number or of the new position's title.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<PagedResult<RequisitionListItem>>, ProblemHttpResult>> ListAsync(
        int? page,
        int? pageSize,
        string? sort,
        Guid? companyId,
        RequisitionStatus[]? status,
        string? search,
        ISender sender,
        CancellationToken cancellationToken) =>
        OkOrProblem(await sender.Send(new GetRequisitionsQuery(new PageRequest(page, pageSize, sort), companyId, status, search), cancellationToken));

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
