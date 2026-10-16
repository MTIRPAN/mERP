using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Domain.Organization;
using Merp.Web.Endpoints;
using Merp.Web.Errors;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.HR.Api.Endpoints;

internal static class PositionEndpoints
{
    public static void MapPositionEndpoints(this IEndpointRouteBuilder group)
    {
        var positions = group.MapGroup("/positions");

        positions.MapGet("/", ListAsync)
            .RequirePermission(HrPermissions.Org.Read)
            .WithName("ListPositions")
            .WithSummary("Positions in the caller's scope with their seats; vacantOnly=true gives the vacancy report")
            .ProducesValidationProblem();

        positions.MapGet("/{id:guid}", GetAsync)
            .RequirePermission(HrPermissions.Org.Read)
            .WithName("GetPosition")
            .WithSummary("A position with its manager and holders; 404 when it is outside the caller's scope")
            .ProducesProblem(StatusCodes.Status404NotFound);

        positions.MapPost("/", CreateAsync)
            .RequirePermission(HrPermissions.Org.Manage)
            .WithName("CreatePosition")
            .WithSummary("Create an active position in a unit of a company in the caller's scope")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        positions.MapPut("/{id:guid}", UpdateAsync)
            .RequirePermission(HrPermissions.Org.Manage)
            .WithName("UpdatePosition")
            .WithSummary("Update a position's unit, job, manager, seats, status and manager flag; the code never changes")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Rows per page, at most 200.</param>
    /// <param name="sort">code, norm, status or createdAt; a leading "-" sorts descending.</param>
    /// <param name="companyId">Only the positions of this company.</param>
    /// <param name="orgUnitId">Only the positions of this unit.</param>
    /// <param name="includeSubUnits">With orgUnitId: also the positions of its sub units.</param>
    /// <param name="jobId">Only the positions with this job.</param>
    /// <param name="status">Only positions in this state.</param>
    /// <param name="search">Part of the position code or of the job's name.</param>
    /// <param name="vacantOnly">Only active positions with empty seats.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<PagedResult<PositionListItem>>, ProblemHttpResult>> ListAsync(
        int? page,
        int? pageSize,
        string? sort,
        Guid? companyId,
        Guid? orgUnitId,
        bool? includeSubUnits,
        Guid? jobId,
        PositionStatus? status,
        string? search,
        bool? vacantOnly,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetPositionsQuery(
                new PageRequest(page, pageSize, sort),
                companyId,
                orgUnitId,
                includeSubUnits ?? false,
                jobId,
                status,
                search,
                vacantOnly ?? false),
            cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<Ok<PositionDetails>, ProblemHttpResult>> GetAsync(Guid id, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetPositionQuery(id), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> CreateAsync(
        CreatePositionCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/hr/positions/{result.Value}", new EntityCreatedResponse(result.Value))
            : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UpdateAsync(
        Guid id,
        UpdatePositionCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command with { Id = id }, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : (ProblemHttpResult)result.Error.ToProblem();
    }
}
