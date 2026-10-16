using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Notifications;
using Merp.SharedKernel.Results;
using Merp.Web.Endpoints;
using Merp.Web.Errors;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.Platform.Api.Endpoints;

/// <summary>
/// Notifications (PLAN P8, D32). A user's own notifications and preferences only need a signed-in user (D22); e-mail
/// templates need <c>platform.notification_templates.*</c> with the Tenant scope.
/// </summary>
internal static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/notifications", ListAsync)
            .RequireAuthenticatedUser()
            .WithName("ListMyNotifications")
            .WithSummary("The caller's notifications, newest first, in their language");

        group.MapGet("/notifications/unread-count", CountUnreadAsync)
            .RequireAuthenticatedUser()
            .WithName("CountMyUnreadNotifications")
            .WithSummary("The number on the bell");

        group.MapPost("/notifications/{id:guid}/read", MarkReadAsync)
            .RequireAuthenticatedUser()
            .WithName("MarkNotificationRead")
            .WithSummary("Mark one of the caller's notifications read")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/notifications/read-all", MarkAllReadAsync)
            .RequireAuthenticatedUser()
            .WithName("MarkAllNotificationsRead")
            .WithSummary("Mark all of the caller's notifications read");

        group.MapGet("/notifications/preferences", GetPreferencesAsync)
            .RequireAuthenticatedUser()
            .WithName("GetMyNotificationPreferences")
            .WithSummary("Notification types with the caller's channels");

        group.MapPut("/notifications/preferences", UpdatePreferencesAsync)
            .RequireAuthenticatedUser()
            .WithName("UpdateMyNotificationPreferences")
            .WithSummary("Switch channels of notification types on or off for the caller")
            .ProducesValidationProblem();

        group.MapPost("/notifications/test", SendTestAsync)
            .RequireAuthenticatedUser()
            .WithName("SendTestNotification")
            .WithSummary("Send the caller a test notification through their channels");

        group.MapGet("/notification-types", ListTypesAsync)
            .RequirePermission(PlatformPermissions.NotificationTemplates.Read)
            .WithName("ListNotificationTypes")
            .WithSummary("Notification types of all modules");

        var templates = group.MapGroup("/notification-templates");

        templates.MapGet("/", ListTemplatesAsync)
            .RequirePermission(PlatformPermissions.NotificationTemplates.Read)
            .WithName("ListNotificationTemplates")
            .WithSummary("E-mail templates of the tenant and the built-in layout")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        templates.MapPost("/", CreateTemplateAsync)
            .RequirePermission(PlatformPermissions.NotificationTemplates.Manage)
            .WithName("CreateNotificationTemplate")
            .WithSummary("Add an e-mail template for a type (or all), a company (or all) and a culture")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        templates.MapPut("/{id:guid}", UpdateTemplateAsync)
            .RequirePermission(PlatformPermissions.NotificationTemplates.Manage)
            .WithName("UpdateNotificationTemplate")
            .WithSummary("Change an e-mail template's subject and body")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        templates.MapDelete("/{id:guid}", DeleteTemplateAsync)
            .RequirePermission(PlatformPermissions.NotificationTemplates.Manage)
            .WithName("DeleteNotificationTemplate")
            .WithSummary("Remove an e-mail template; the more general one applies again")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Rows per page, at most 200.</param>
    /// <param name="unreadOnly">Only unread notifications.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<PagedResult<NotificationItem>>, ProblemHttpResult>> ListAsync(
        int? page,
        int? pageSize,
        bool? unreadOnly,
        ISender sender,
        CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetMyNotificationsQuery(new PageRequest(page, pageSize), unreadOnly), cancellationToken));

    private static async Task<Results<Ok<UnreadNotificationCount>, ProblemHttpResult>> CountUnreadAsync(ISender sender, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetUnreadNotificationCountQuery(), cancellationToken));

    private static async Task<Results<NoContent, ProblemHttpResult>> MarkReadAsync(Guid id, ISender sender, CancellationToken cancellationToken) =>
        NoContentOrProblem(await sender.Send(new MarkNotificationReadCommand(id), cancellationToken));

    private static async Task<Results<NoContent, ProblemHttpResult>> MarkAllReadAsync(ISender sender, CancellationToken cancellationToken) =>
        NoContentOrProblem(await sender.Send(new MarkAllNotificationsReadCommand(), cancellationToken));

    private static async Task<Results<Ok<IReadOnlyList<NotificationPreferenceItem>>, ProblemHttpResult>> GetPreferencesAsync(ISender sender, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetMyNotificationPreferencesQuery(), cancellationToken));

    private static async Task<Results<NoContent, ProblemHttpResult>> UpdatePreferencesAsync(
        UpdateMyNotificationPreferencesCommand command,
        ISender sender,
        CancellationToken cancellationToken) =>
        NoContentOrProblem(await sender.Send(command, cancellationToken));

    private static async Task<Results<NoContent, ProblemHttpResult>> SendTestAsync(ISender sender, CancellationToken cancellationToken) =>
        NoContentOrProblem(await sender.Send(new SendTestNotificationCommand(), cancellationToken));

    private static async Task<Results<Ok<IReadOnlyList<NotificationTypeItem>>, ProblemHttpResult>> ListTypesAsync(ISender sender, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetNotificationTypesQuery(), cancellationToken));

    private static async Task<Results<Ok<NotificationTemplateList>, ProblemHttpResult>> ListTemplatesAsync(ISender sender, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetNotificationTemplatesQuery(), cancellationToken));

    private static async Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> CreateTemplateAsync(
        CreateNotificationTemplateCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/platform/notification-templates/{result.Value}", new EntityCreatedResponse(result.Value))
            : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UpdateTemplateAsync(
        Guid id,
        UpdateNotificationTemplateCommand command,
        ISender sender,
        CancellationToken cancellationToken) =>
        NoContentOrProblem(await sender.Send(command with { Id = id }, cancellationToken));

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteTemplateAsync(Guid id, ISender sender, CancellationToken cancellationToken) =>
        NoContentOrProblem(await sender.Send(new DeleteNotificationTemplateCommand(id), cancellationToken));

    private static Results<Ok<T>, ProblemHttpResult> Respond<T>(Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();

    private static Results<NoContent, ProblemHttpResult> NoContentOrProblem(Result result) =>
        result.IsSuccess ? TypedResults.NoContent() : (ProblemHttpResult)result.Error.ToProblem();
}
