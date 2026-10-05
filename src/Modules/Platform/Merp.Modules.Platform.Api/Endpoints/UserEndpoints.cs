using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Users;
using Merp.Web.Errors;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.Platform.Api.Endpoints;

/// <summary>User administration (PLAN P2): tenant level, so the permissions need the Tenant scope.</summary>
internal static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder group)
    {
        var users = group.MapGroup("/users");

        users.MapGet("/", ListAsync)
            .RequirePermission(PlatformPermissions.Users.Read)
            .WithName("ListUsers")
            .WithSummary("Users of the tenant with the roles in force today")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        users.MapGet("/{id:guid}", GetAsync)
            .RequirePermission(PlatformPermissions.Users.Read)
            .WithName("GetUser")
            .WithSummary("A user with role assignments")
            .ProducesProblem(StatusCodes.Status404NotFound);

        users.MapPost("/", CreateAsync)
            .RequirePermission(PlatformPermissions.Users.Manage)
            .WithName("CreateUser")
            .WithSummary("Create a user in Keycloak and send the invitation e-mail")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        users.MapPut("/{id:guid}/active", SetActiveAsync)
            .RequirePermission(PlatformPermissions.Users.Manage)
            .WithName("SetUserActive")
            .WithSummary("Activate or deactivate a user (also in Keycloak)")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        users.MapPost("/{id:guid}/password-reset", SendPasswordResetAsync)
            .RequirePermission(PlatformPermissions.Users.Manage)
            .WithName("SendPasswordReset")
            .WithSummary("E-mail the user a link to set a new password")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        users.MapPost("/{id:guid}/assignments", AssignAsync)
            .RequirePermission(PlatformPermissions.Users.Manage)
            .WithName("AssignRole")
            .WithSummary("Give the user a role in a data scope, optionally for a period")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        users.MapPut("/{id:guid}/assignments/{assignmentId:guid}", ChangeAssignmentAsync)
            .RequirePermission(PlatformPermissions.Users.Manage)
            .WithName("ChangeAssignment")
            .WithSummary("Change the period of a role assignment")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        users.MapDelete("/{id:guid}/assignments/{assignmentId:guid}", RemoveAssignmentAsync)
            .RequirePermission(PlatformPermissions.Users.Manage)
            .WithName("RemoveAssignment")
            .WithSummary("Withdraw a role assignment")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Rows per page, at most 200.</param>
    /// <param name="sort">userName, email, firstName, lastName, isActive or createdAt; a leading "-" sorts descending.</param>
    /// <param name="search">Part of the user name, e-mail address or name.</param>
    /// <param name="isActive">Only active or only inactive users.</param>
    /// <param name="roleId">Only holders of this role.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<PagedResult<UserListItem>>, ProblemHttpResult>> ListAsync(
        int? page,
        int? pageSize,
        string? sort,
        string? search,
        bool? isActive,
        Guid? roleId,
        ISender sender,
        CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetUsersQuery(new PageRequest(page, pageSize, sort), search, isActive, roleId), cancellationToken));

    private static async Task<Results<Ok<UserDetails>, ProblemHttpResult>> GetAsync(Guid id, ISender sender, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetUserQuery(id), cancellationToken));

    private static async Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> CreateAsync(
        CreateUserCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/platform/users/{result.Value}", new EntityCreatedResponse(result.Value))
            : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> SetActiveAsync(
        Guid id,
        SetUserActiveCommand command,
        ISender sender,
        CancellationToken cancellationToken) =>
        NoContent(await sender.Send(command with { Id = id }, cancellationToken));

    private static async Task<Results<NoContent, ProblemHttpResult>> SendPasswordResetAsync(Guid id, ISender sender, CancellationToken cancellationToken) =>
        NoContent(await sender.Send(new SendPasswordResetCommand(id), cancellationToken));

    private static async Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> AssignAsync(
        Guid id,
        AssignRoleCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command with { UserId = id }, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/platform/users/{id}", new EntityCreatedResponse(result.Value))
            : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ChangeAssignmentAsync(
        Guid id,
        Guid assignmentId,
        ChangeAssignmentCommand command,
        ISender sender,
        CancellationToken cancellationToken) =>
        NoContent(await sender.Send(command with { UserId = id, AssignmentId = assignmentId }, cancellationToken));

    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveAssignmentAsync(
        Guid id,
        Guid assignmentId,
        ISender sender,
        CancellationToken cancellationToken) =>
        NoContent(await sender.Send(new RemoveAssignmentCommand(id, assignmentId), cancellationToken));

    private static Results<Ok<T>, ProblemHttpResult> Respond<T>(Merp.SharedKernel.Results.Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();

    private static Results<NoContent, ProblemHttpResult> NoContent(Merp.SharedKernel.Results.Result result) =>
        result.IsSuccess ? TypedResults.NoContent() : (ProblemHttpResult)result.Error.ToProblem();
}
