using Merp.Application.Abstractions.Exports;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Branches;
using Merp.Modules.Platform.Application.Exports;
using Merp.Modules.Platform.Domain.Branches;
using Merp.Web.Endpoints;
using Merp.Web.Errors;
using Merp.Web.Files;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.Platform.Api.Endpoints;

internal static class BranchEndpoints
{
    public static void MapBranchEndpoints(this IEndpointRouteBuilder group)
    {
        var branches = group.MapGroup("/branches");

        branches.MapGet("/", ListAsync)
            .RequirePermission(PlatformPermissions.Branches.Read)
            .WithName("ListBranches")
            .WithSummary("Branches in the caller's scope, narrowed by the active company (X-Company-Id)")
            .ProducesValidationProblem();

        branches.MapGet("/export", ExportAsync)
            .RequirePermission(PlatformPermissions.Branches.Read)
            .WithName("ExportBranches")
            .WithSummary("The branch list as Excel, with the list's filters and sort and the chosen columns (D35)")
            .ProducesValidationProblem();

        branches.MapGet("/{id:guid}", GetAsync)
            .RequirePermission(PlatformPermissions.Branches.Read)
            .WithName("GetBranch")
            .WithSummary("A branch; 404 when it is outside the caller's scope")
            .ProducesProblem(StatusCodes.Status404NotFound);

        branches.MapPost("/", CreateAsync)
            .RequirePermission(PlatformPermissions.Branches.Manage)
            .WithName("CreateBranch")
            .WithSummary("Create a branch of a company in the caller's scope")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        branches.MapPut("/{id:guid}", UpdateAsync)
            .RequirePermission(PlatformPermissions.Branches.Manage)
            .WithName("UpdateBranch")
            .WithSummary("Update a branch's details and active flag; the company and the code never change")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Rows per page, at most 200.</param>
    /// <param name="sort">code, name, type, isActive or createdAt; a leading "-" sorts descending.</param>
    /// <param name="companyId">Only the branches of this company.</param>
    /// <param name="search">Part of the code or name.</param>
    /// <param name="type">Only branches of this type.</param>
    /// <param name="provinceCode">Only branches in this province (plate number).</param>
    /// <param name="isActive">Only active or only inactive branches.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<PagedResult<BranchListItem>>, ProblemHttpResult>> ListAsync(
        int? page,
        int? pageSize,
        string? sort,
        Guid? companyId,
        string? search,
        BranchType? type,
        int? provinceCode,
        bool? isActive,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetBranchesQuery(new PageRequest(page, pageSize, sort), companyId, search, type, provinceCode, isActive),
            cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
    }

    /// <param name="columns">Column keys, comma separated (code,name); every column when empty.</param>
    /// <param name="sort">The list's sort.</param>
    /// <param name="companyId">The list's filter.</param>
    /// <param name="search">The list's filter.</param>
    /// <param name="type">The list's filter.</param>
    /// <param name="provinceCode">The list's filter.</param>
    /// <param name="isActive">The list's filter.</param>
    /// <param name="exporter">Writes the file.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> ExportAsync(
        string? columns,
        string? sort,
        Guid? companyId,
        string? search,
        BranchType? type,
        int? provinceCode,
        bool? isActive,
        IListExporter exporter,
        CancellationToken cancellationToken) =>
        (await exporter.ExportAsync(
            new ExportList<BranchListItem>(
                "web.platform.branches.title",
                typeof(Branch),
                PlatformExports.Branches,
                paging => new GetBranchesQuery(paging, companyId, search, type, provinceCode, isActive),
                sort,
                ExportRules.ParseColumns(columns),
                new Dictionary<string, object?>
                {
                    ["companyId"] = companyId,
                    ["search"] = search,
                    ["type"] = type,
                    ["provinceCode"] = provinceCode,
                    ["isActive"] = isActive,
                }),
            cancellationToken)).ToFile();

    private static async Task<Results<Ok<BranchDetails>, ProblemHttpResult>> GetAsync(Guid id, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetBranchQuery(id), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> CreateAsync(
        CreateBranchCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/platform/branches/{result.Value}", new EntityCreatedResponse(result.Value))
            : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UpdateAsync(
        Guid id,
        UpdateBranchCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command with { Id = id }, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : (ProblemHttpResult)result.Error.ToProblem();
    }
}
