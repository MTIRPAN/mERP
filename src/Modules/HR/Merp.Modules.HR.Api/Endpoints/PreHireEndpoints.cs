using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.PreHires;
using Merp.Modules.HR.Domain.PreHires;
using Merp.SharedKernel.Results;
using Merp.Web.Errors;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.HR.Api.Endpoints;

/// <summary>
/// Pre-hires (PLAN R7, D54): candidates Recruitment hires, waiting for HR. They are completed with the hire, rehire or
/// assignment change endpoints of employees (<c>preHireId</c>) and may be called off here.
/// </summary>
internal static class PreHireEndpoints
{
    public static void MapPreHireEndpoints(this IEndpointRouteBuilder group)
    {
        var preHires = group.MapGroup("/pre-hires");

        preHires.MapGet("", ListAsync)
            .RequirePermission(HrPermissions.Employees.Create)
            .WithName("ListPreHires")
            .WithSummary("Pre-hires of the companies in scope, by start date")
            .ProducesValidationProblem();

        preHires.MapGet("/{id:guid}", async Task<Results<Ok<PreHireItem>, ProblemHttpResult>> (Guid id, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetPreHireQuery(id), ct)))
            .RequirePermission(HrPermissions.Employees.Create)
            .WithName("GetPreHire")
            .WithSummary("A pre-hire with the person it may be for")
            .ProducesProblem(StatusCodes.Status404NotFound);

        preHires.MapPost("/{id:guid}/cancel", async Task<Results<NoContent, ProblemHttpResult>> (Guid id, CancelPreHireCommand command, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(command with { Id = id }, ct);
                return result.IsSuccess ? TypedResults.NoContent() : (ProblemHttpResult)result.Error.ToProblem();
            })
            .RequirePermission(HrPermissions.Employees.Create)
            .WithName("CancelPreHire")
            .WithSummary("Call a waiting pre-hire off; the application goes back to the pipeline")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    /// <param name="page">1-based page.</param>
    /// <param name="pageSize">Rows per page, at most 200.</param>
    /// <param name="sort">startDate or createdAt; a leading - sorts descending.</param>
    /// <param name="status">Only these states (repeatable).</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<PagedResult<PreHireItem>>, ProblemHttpResult>> ListAsync(
        int? page,
        int? pageSize,
        string? sort,
        PreHireStatus[]? status,
        ISender sender,
        CancellationToken cancellationToken) =>
        OkOrProblem(await sender.Send(new GetPreHiresQuery(new PageRequest(page, pageSize, sort), status), cancellationToken));

    private static Results<Ok<T>, ProblemHttpResult> OkOrProblem<T>(Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
}
