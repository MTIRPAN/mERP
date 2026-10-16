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

internal static class OrgUnitEndpoints
{
    public static void MapOrgUnitEndpoints(this IEndpointRouteBuilder group)
    {
        var units = group.MapGroup("/org-units");

        units.MapGet("/tree", GetTreeAsync)
            .RequirePermission(HrPermissions.Org.Read)
            .WithName("GetOrgUnitTree")
            .WithSummary("The units of a company in the caller's scope with their staffing, for the organization tree")
            .ProducesValidationProblem();

        units.MapGet("/", ListAsync)
            .RequirePermission(HrPermissions.Org.Read)
            .WithName("ListOrgUnits")
            .WithSummary("Units in the caller's scope, narrowed by the active company (X-Company-Id)")
            .ProducesValidationProblem();

        units.MapGet("/{id:guid}", GetAsync)
            .RequirePermission(HrPermissions.Org.Read)
            .WithName("GetOrgUnit")
            .WithSummary("A unit; 404 when it is outside the caller's scope")
            .ProducesProblem(StatusCodes.Status404NotFound);

        units.MapPost("/", CreateAsync)
            .RequirePermission(HrPermissions.Org.Manage)
            .WithName("CreateOrgUnit")
            .WithSummary("Create a unit of a company in the caller's scope, at the top or under a parent unit")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        units.MapPut("/{id:guid}", UpdateAsync)
            .RequirePermission(HrPermissions.Org.Manage)
            .WithName("UpdateOrgUnit")
            .WithSummary("Update a unit's details and manager position; the company, the code and the parent do not change")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        units.MapPost("/{id:guid}/move", MoveAsync)
            .RequirePermission(HrPermissions.Org.Manage)
            .WithName("MoveOrgUnit")
            .WithSummary("Move a unit with its sub units under another unit of the company, or to the top")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    /// <param name="companyId">The company; the active company (X-Company-Id) when not given.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<OrgUnitTree>, ProblemHttpResult>> GetTreeAsync(Guid? companyId, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetOrgUnitTreeQuery(companyId), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
    }

    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Rows per page, at most 200.</param>
    /// <param name="sort">code, name, type or createdAt; a leading "-" sorts descending.</param>
    /// <param name="companyId">Only the units of this company.</param>
    /// <param name="search">Part of the code or name.</param>
    /// <param name="type">Only units of this type.</param>
    /// <param name="parentId">Only the direct sub units of this unit.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<PagedResult<OrgUnitListItem>>, ProblemHttpResult>> ListAsync(
        int? page,
        int? pageSize,
        string? sort,
        Guid? companyId,
        string? search,
        OrgUnitType? type,
        Guid? parentId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetOrgUnitsQuery(new PageRequest(page, pageSize, sort), companyId, search, type, parentId), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<Ok<OrgUnitDetails>, ProblemHttpResult>> GetAsync(Guid id, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetOrgUnitQuery(id), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> CreateAsync(
        CreateOrgUnitCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/hr/org-units/{result.Value}", new EntityCreatedResponse(result.Value))
            : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UpdateAsync(
        Guid id,
        UpdateOrgUnitCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command with { Id = id }, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> MoveAsync(
        Guid id,
        MoveOrgUnitCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command with { Id = id }, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : (ProblemHttpResult)result.Error.ToProblem();
    }
}
