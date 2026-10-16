using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.Organization;
using Merp.Web.Endpoints;
using Merp.Web.Errors;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.HR.Api.Endpoints;

internal static class JobEndpoints
{
    public static void MapJobEndpoints(this IEndpointRouteBuilder group)
    {
        var jobs = group.MapGroup("/jobs");

        jobs.MapGet("/", ListAsync)
            .RequirePermission(HrPermissions.Org.Read)
            .WithName("ListJobs")
            .WithSummary("The tenant's job catalog, shared by all companies")
            .ProducesValidationProblem();

        jobs.MapGet("/{id:guid}", GetAsync)
            .RequirePermission(HrPermissions.Org.Read)
            .WithName("GetJob")
            .WithSummary("A job of the catalog")
            .ProducesProblem(StatusCodes.Status404NotFound);

        jobs.MapPost("/", CreateAsync)
            .RequirePermission(HrPermissions.Org.Manage)
            .WithName("CreateJob")
            .WithSummary("Add a job to the catalog; needs the Tenant scope")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        jobs.MapPut("/{id:guid}", UpdateAsync)
            .RequirePermission(HrPermissions.Org.Manage)
            .WithName("UpdateJob")
            .WithSummary("Update a job's details and active flag; needs the Tenant scope, the code never changes")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Rows per page, at most 200.</param>
    /// <param name="sort">code, name, family, level, isActive or createdAt; a leading "-" sorts descending.</param>
    /// <param name="search">Part of the code or name.</param>
    /// <param name="family">Only the jobs of this family.</param>
    /// <param name="isActive">Only active or only inactive jobs.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<PagedResult<JobListItem>>, ProblemHttpResult>> ListAsync(
        int? page,
        int? pageSize,
        string? sort,
        string? search,
        string? family,
        bool? isActive,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetJobsQuery(new PageRequest(page, pageSize, sort), search, family, isActive), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<Ok<JobDetails>, ProblemHttpResult>> GetAsync(Guid id, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetJobQuery(id), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> CreateAsync(
        CreateJobCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/hr/jobs/{result.Value}", new EntityCreatedResponse(result.Value))
            : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UpdateAsync(
        Guid id,
        UpdateJobCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command with { Id = id }, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : (ProblemHttpResult)result.Error.ToProblem();
    }
}
