using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Roles;
using Merp.SharedKernel.Results;
using Merp.Web.Errors;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.Platform.Api.Endpoints;

/// <summary>Roles, the permission catalog and scope types (PLAN P2); tenant level, the Tenant scope is required.</summary>
internal static class RoleEndpoints
{
    public static void MapRoleEndpoints(this IEndpointRouteBuilder group)
    {
        var roles = group.MapGroup("/roles");

        roles.MapGet("/", ListAsync)
            .RequirePermission(PlatformPermissions.Roles.Read)
            .WithName("ListRoles")
            .WithSummary("Roles of the tenant")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        roles.MapGet("/{id:guid}", GetAsync)
            .RequirePermission(PlatformPermissions.Roles.Read)
            .WithName("GetRole")
            .WithSummary("A role with its permissions")
            .ProducesProblem(StatusCodes.Status404NotFound);

        roles.MapPost("/", CreateAsync)
            .RequirePermission(PlatformPermissions.Roles.Manage)
            .WithName("CreateRole")
            .WithSummary("Create a custom role")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        roles.MapPut("/{id:guid}", UpdateAsync)
            .RequirePermission(PlatformPermissions.Roles.Manage)
            .WithName("UpdateRole")
            .WithSummary("Rename a role and replace its permissions; holders are affected from their next request")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        roles.MapDelete("/{id:guid}", DeleteAsync)
            .RequirePermission(PlatformPermissions.Roles.Manage)
            .WithName("DeleteRole")
            .WithSummary("Delete a custom role nobody holds")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        roles.MapPost("/{id:guid}/reset", ResetAsync)
            .RequirePermission(PlatformPermissions.Roles.Manage)
            .WithName("ResetRole")
            .WithSummary("Reset a system role to its default name and permissions")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/permissions", GetPermissionTreeAsync)
            .RequirePermission(PlatformPermissions.Roles.Read)
            .WithName("GetPermissionTree")
            .WithSummary("Permission catalog as a module → resource → permission tree with localized labels");

        group.MapGet("/scope-types", GetScopeTypesAsync)
            .RequirePermission(PlatformPermissions.Users.Read)
            .WithName("ListScopeTypes")
            .WithSummary("Data scope types a role assignment can use");
    }

    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Rows per page, at most 200.</param>
    /// <param name="sort">code, name, permissionCount or userCount; a leading "-" sorts descending.</param>
    /// <param name="search">Part of the code or name.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<PagedResult<RoleListItem>>, ProblemHttpResult>> ListAsync(
        int? page,
        int? pageSize,
        string? sort,
        string? search,
        ISender sender,
        CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetRolesQuery(new PageRequest(page, pageSize, sort), search), cancellationToken));

    private static async Task<Results<Ok<RoleDetails>, ProblemHttpResult>> GetAsync(Guid id, ISender sender, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetRoleQuery(id), cancellationToken));

    private static async Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> CreateAsync(
        CreateRoleCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/platform/roles/{result.Value}", new EntityCreatedResponse(result.Value))
            : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UpdateAsync(
        Guid id,
        UpdateRoleCommand command,
        ISender sender,
        CancellationToken cancellationToken) =>
        NoContent(await sender.Send(command with { Id = id }, cancellationToken));

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(Guid id, ISender sender, CancellationToken cancellationToken) =>
        NoContent(await sender.Send(new DeleteRoleCommand(id), cancellationToken));

    private static async Task<Results<NoContent, ProblemHttpResult>> ResetAsync(Guid id, ISender sender, CancellationToken cancellationToken) =>
        NoContent(await sender.Send(new ResetRoleCommand(id), cancellationToken));

    private static async Task<Results<Ok<IReadOnlyList<PermissionModuleNode>>, ProblemHttpResult>> GetPermissionTreeAsync(
        ISender sender,
        CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetPermissionTreeQuery(), cancellationToken));

    private static async Task<Results<Ok<IReadOnlyList<ScopeTypeItem>>, ProblemHttpResult>> GetScopeTypesAsync(
        ISender sender,
        CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetScopeTypesQuery(), cancellationToken));

    private static Results<Ok<T>, ProblemHttpResult> Respond<T>(Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();

    private static Results<NoContent, ProblemHttpResult> NoContent(Result result) =>
        result.IsSuccess ? TypedResults.NoContent() : (ProblemHttpResult)result.Error.ToProblem();
}
