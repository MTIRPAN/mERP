using FluentValidation;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Persistence;
using Merp.Modules.Platform.Contracts.Notifications;
using Merp.Modules.Platform.Domain.Notifications;
using Merp.Modules.Platform.Domain.Users;
using Merp.SharedKernel.Notifications;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Notifications;

// The caller's own notifications and preferences (PLAN P8). Any signed-in user may use them (D22).

/// <summary>A notification with its title and body in the caller's language.</summary>
public sealed record NotificationItem(Guid Id, string Type, string Title, string? Body, string? Link, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

/// <summary>The caller's notifications, newest first.</summary>
public sealed record GetMyNotificationsQuery(PageRequest Page, bool? UnreadOnly) : IQuery<PagedResult<NotificationItem>>;

public sealed record UnreadNotificationCount(int Count);

/// <summary>The number on the bell.</summary>
public sealed record GetUnreadNotificationCountQuery : IQuery<UnreadNotificationCount>;

public sealed record MarkNotificationReadCommand(Guid Id) : ICommand;

public sealed record MarkAllNotificationsReadCommand : ICommand;

/// <summary>A notification type with the caller's channels; mandatory types cannot be switched off.</summary>
public sealed record NotificationPreferenceItem(string Type, string Label, bool Mandatory, bool InApp, bool Email);

public sealed record GetMyNotificationPreferencesQuery : IQuery<IReadOnlyList<NotificationPreferenceItem>>;

public sealed record NotificationPreferenceInput(string? Type, bool InApp, bool Email);

/// <summary>Saves the caller's channels for the given types; types not listed keep their setting.</summary>
public sealed record UpdateMyNotificationPreferencesCommand(IReadOnlyList<NotificationPreferenceInput>? Preferences) : ICommand;

/// <summary>Sends the caller a test notification through their channels.</summary>
public sealed record SendTestNotificationCommand : ICommand;

public static class NotificationErrors
{
    public const string UnknownTypeCode = "platform.notifications.errors.unknown_type";
    public static readonly Error TenantScopeRequired = Error.Forbidden("platform.notifications.errors.tenant_scope_required");
}

internal sealed class GetMyNotificationsQueryHandler(
    IPlatformDbContext db,
    ICurrentUser currentUser,
    NotificationTypeRegistry types,
    EmailComposer composer) : IQueryHandler<GetMyNotificationsQuery, PagedResult<NotificationItem>>
{
    public async Task<Result<PagedResult<NotificationItem>>> Handle(GetMyNotificationsQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return CommonErrors.Forbidden;
        }

        var notifications = db.Notifications.AsNoTracking().Where(n => n.RecipientId == UserId.From(userId));
        if (request.UnreadOnly == true)
        {
            notifications = notifications.Where(n => n.ReadAt == null);
        }

        var page = await notifications.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).ToPagedResultAsync(request.Page, cancellationToken);
        var items = page.Items.Select(n => ToItem(n, types, composer)).ToList();
        return new PagedResult<NotificationItem>(items, page.Page, page.PageSize, page.TotalCount);
    }

    /// <summary>Texts in the caller's language; a type no module registers any more shows its code.</summary>
    private static NotificationItem ToItem(Notification n, NotificationTypeRegistry types, EmailComposer composer)
    {
        var (title, body) = types.Find(n.Type) is { } type ? composer.Texts(type, n.Args, culture: null) : (n.Type, null);
        return new NotificationItem(n.Id.Value, n.Type, title, body, n.Link, n.CreatedAt, n.ReadAt);
    }
}

internal sealed class GetUnreadNotificationCountQueryHandler(IPlatformDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetUnreadNotificationCountQuery, UnreadNotificationCount>
{
    public async Task<Result<UnreadNotificationCount>> Handle(GetUnreadNotificationCountQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return CommonErrors.Forbidden;
        }

        var count = await db.Notifications.CountAsync(n => n.RecipientId == UserId.From(userId) && n.ReadAt == null, cancellationToken);
        return new UnreadNotificationCount(count);
    }
}

internal sealed class MarkNotificationReadCommandHandler(IPlatformDbContext db, ICurrentUser currentUser, TimeProvider timeProvider)
    : ICommandHandler<MarkNotificationReadCommand>
{
    public async Task<Result> Handle(MarkNotificationReadCommand request, CancellationToken cancellationToken)
    {
        // Another user's notification looks the same as a missing one
        var notification = currentUser.UserId is { } userId
            ? await db.Notifications.SingleOrDefaultAsync(n => n.Id == NotificationId.From(request.Id) && n.RecipientId == UserId.From(userId), cancellationToken)
            : null;
        if (notification is null)
        {
            return CommonErrors.NotFound;
        }

        notification.MarkRead(timeProvider.GetUtcNow());
        return Result.Success();
    }
}

internal sealed class MarkAllNotificationsReadCommandHandler(IPlatformDbContext db, ICurrentUser currentUser, TimeProvider timeProvider)
    : ICommandHandler<MarkAllNotificationsReadCommand>
{
    public async Task<Result> Handle(MarkAllNotificationsReadCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return CommonErrors.Forbidden;
        }

        var now = timeProvider.GetUtcNow();
        await db.Notifications
            .Where(n => n.RecipientId == UserId.From(userId) && n.ReadAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(n => n.ReadAt, now), cancellationToken);
        return Result.Success();
    }
}

internal sealed class GetMyNotificationPreferencesQueryHandler(
    IPlatformDbContext db,
    ICurrentUser currentUser,
    NotificationTypeRegistry types,
    ITextLocalizer localizer) : IQueryHandler<GetMyNotificationPreferencesQuery, IReadOnlyList<NotificationPreferenceItem>>
{
    public async Task<Result<IReadOnlyList<NotificationPreferenceItem>>> Handle(GetMyNotificationPreferencesQuery request, CancellationToken cancellationToken)
    {
        var user = currentUser.UserId is { } userId
            ? await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == UserId.From(userId), cancellationToken)
            : null;
        if (user is null)
        {
            return CommonErrors.Forbidden;
        }

        IReadOnlyList<NotificationPreferenceItem> items =
        [
            .. types.All
                .Where(t => !t.External)
                .Select(t => new NotificationPreferenceItem(
                    t.Type,
                    localizer.Get(t.LabelKey),
                    t.Mandatory,
                    NotificationTypeRegistry.Receives(user, t, NotificationChannel.InApp),
                    NotificationTypeRegistry.Receives(user, t, NotificationChannel.Email)))
                .OrderBy(t => t.Type, StringComparer.Ordinal),
        ];
        return Result.Success(items);
    }
}

internal sealed class UpdateMyNotificationPreferencesCommandValidator : AbstractValidator<UpdateMyNotificationPreferencesCommand>
{
    public UpdateMyNotificationPreferencesCommandValidator(NotificationTypeRegistry types)
    {
        RuleFor(c => c.Preferences).NotNull();
        RuleForEach(c => c.Preferences).ChildRules(preference =>
            preference.RuleFor(p => p.Type).Must(type => types.Find(type) is { External: false }).WithErrorCode(NotificationErrors.UnknownTypeCode));
    }
}

internal sealed class UpdateMyNotificationPreferencesCommandHandler(IPlatformDbContext db, ICurrentUser currentUser, NotificationTypeRegistry types)
    : ICommandHandler<UpdateMyNotificationPreferencesCommand>
{
    public async Task<Result> Handle(UpdateMyNotificationPreferencesCommand request, CancellationToken cancellationToken)
    {
        var user = currentUser.UserId is { } userId
            ? await db.Users.SingleOrDefaultAsync(u => u.Id == UserId.From(userId), cancellationToken)
            : null;
        if (user is null)
        {
            return CommonErrors.Forbidden;
        }

        foreach (var preference in request.Preferences!)
        {
            var type = types.Find(preference.Type)!;
            if (type.Mandatory)
            {
                continue;
            }

            user.SetNotificationPreference(type.Type, NotificationChannel.InApp, preference.InApp, type.DefaultChannels.Contains(NotificationChannel.InApp));
            user.SetNotificationPreference(type.Type, NotificationChannel.Email, preference.Email, type.DefaultChannels.Contains(NotificationChannel.Email));
        }

        return Result.Success();
    }
}

internal sealed class SendTestNotificationCommandHandler(
    IPlatformDbContext db,
    ICurrentUser currentUser,
    ICompanyContext companyContext,
    IIntegrationEventPublisher publisher) : ICommandHandler<SendTestNotificationCommand>
{
    public async Task<Result> Handle(SendTestNotificationCommand request, CancellationToken cancellationToken)
    {
        var user = currentUser.UserId is { } userId
            ? await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == UserId.From(userId), cancellationToken)
            : null;
        if (user is null)
        {
            return CommonErrors.Forbidden;
        }

        await publisher.PublishAsync(
            new NotificationRequested(
                PlatformNotificationTypes.Test,
                [user.Id.Value],
                new Dictionary<string, string> { ["name"] = user.DisplayName },
                "/profile")
            {
                CompanyId = companyContext.ActiveCompanyId,
            },
            cancellationToken);
        return Result.Success();
    }
}
