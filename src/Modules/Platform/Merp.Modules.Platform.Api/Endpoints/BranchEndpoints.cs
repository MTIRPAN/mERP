using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Branches;
using Merp.Modules.Platform.Domain.Branches;
using Merp.Web.Errors;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.Platform.Api.Endpoints;

internal static class BranchEndpoints
{
    private const string Path = "/api/platform/branches";

    public static void MapBranchEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/branches", ListAsync)
            .RequirePermission(PlatformPermissions.Branches.Read)
            .WithName("ListBranches")
            .WithSummary("Branches in the caller's scope, narrowed by companyId and the active company")
            .ProducesValidationProblem();

        group.MapGet("/branches/{id:guid}", GetAsync)
            .RequirePermission(PlatformPermissions.Branches.Read)
            .WithName("GetBranch")
            .WithSummary("A branch; 404 when missing or outside the caller's scope");

        group.MapPost("/branches", CreateAsync)
            .RequirePermission(PlatformPermissions.Branches.Manage)
            .WithName("CreateBranch")
            .WithSummary("Creates a branch of a company in the caller's scope")
            .ProducesValidationProblem();

        group.MapPut("/branches/{id:guid}", UpdateAsync)
            .RequirePermission(PlatformPermissions.Branches.Manage)
            .WithName("UpdateBranch")
            .WithSummary("Updates a branch; 409 when the version is stale")
            .ProducesValidationProblem();
    }

    private static async Task<Results<Ok<PagedResult<BranchListItem>>, ProblemHttpResult>> ListAsync(
        [AsParameters] ListBranchesQuery query, ISender sender, CancellationToken cancellationToken) =>
        (await sender.Send(query, cancellationToken)).ToOkOrProblem();

    private static async Task<Results<Ok<BranchDto>, ProblemHttpResult>> GetAsync(Guid id, ISender sender, CancellationToken cancellationToken) =>
        (await sender.Send(new GetBranchQuery(id), cancellationToken)).ToOkOrProblem();

    private static async Task<Results<Created<CreatedResponse>, ProblemHttpResult>> CreateAsync(
        CreateBranchCommand command, ISender sender, CancellationToken cancellationToken) =>
        (await sender.Send(command, cancellationToken)).ToCreatedOrProblem(Path);

    private static async Task<Results<NoContent, ProblemHttpResult>> UpdateAsync(
        Guid id, UpdateBranchRequest request, ISender sender, CancellationToken cancellationToken) =>
        (await sender.Send(request.ToCommand(id), cancellationToken)).ToNoContentOrProblem();
}

/// <summary>Body of <c>PUT /api/platform/branches/{id}</c>; the company and code cannot change.</summary>
public sealed record UpdateBranchRequest(
    uint Version,
    string Name,
    BranchType Type,
    string AddressLine1,
    string? AddressLine2,
    int ProvinceCode,
    int DistrictId,
    string? PostalCode,
    string? SgkWorkplaceNo,
    bool IsActive)
{
    public UpdateBranchCommand ToCommand(Guid id) => new(
        id, Version, Name, Type, AddressLine1, AddressLine2, ProvinceCode, DistrictId, PostalCode, SgkWorkplaceNo, IsActive);
}
