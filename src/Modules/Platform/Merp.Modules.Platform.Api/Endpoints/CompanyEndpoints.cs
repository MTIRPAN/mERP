using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Companies;
using Merp.Web.Errors;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.Platform.Api.Endpoints;

internal static class CompanyEndpoints
{
    public static void MapCompanyEndpoints(this IEndpointRouteBuilder group)
    {
        var companies = group.MapGroup("/companies");

        companies.MapGet("/", ListAsync)
            .RequirePermission(PlatformPermissions.Companies.Read)
            .WithName("ListCompanies")
            .WithSummary("Companies in the caller's scope")
            .ProducesValidationProblem();

        companies.MapGet("/{id:guid}", GetAsync)
            .RequirePermission(PlatformPermissions.Companies.Read)
            .WithName("GetCompany")
            .WithSummary("A company; 404 when it is outside the caller's scope")
            .ProducesProblem(StatusCodes.Status404NotFound);

        companies.MapPost("/", CreateAsync)
            .RequirePermission(PlatformPermissions.Companies.Manage)
            .WithName("CreateCompany")
            .WithSummary("Create a company (Tenant scope required)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        companies.MapPut("/{id:guid}", UpdateAsync)
            .RequirePermission(PlatformPermissions.Companies.Manage)
            .WithName("UpdateCompany")
            .WithSummary("Update a company's details and active flag; the code never changes")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Rows per page, at most 200.</param>
    /// <param name="sort">code, shortName, title, taxNumber, isActive or createdAt; a leading "-" sorts descending.</param>
    /// <param name="search">Part of the code, short name, title or tax number.</param>
    /// <param name="isActive">Only active or only inactive companies.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<PagedResult<CompanyListItem>>, ProblemHttpResult>> ListAsync(
        int? page,
        int? pageSize,
        string? sort,
        string? search,
        bool? isActive,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetCompaniesQuery(new PageRequest(page, pageSize, sort), search, isActive), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<Ok<CompanyDetails>, ProblemHttpResult>> GetAsync(Guid id, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetCompanyQuery(id), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> CreateAsync(
        CreateCompanyCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/platform/companies/{result.Value}", new EntityCreatedResponse(result.Value))
            : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UpdateAsync(
        Guid id,
        UpdateCompanyCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command with { Id = id }, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : (ProblemHttpResult)result.Error.ToProblem();
    }
}

/// <summary>Id of a created record; its URL is in the Location header.</summary>
public sealed record EntityCreatedResponse(Guid Id);
