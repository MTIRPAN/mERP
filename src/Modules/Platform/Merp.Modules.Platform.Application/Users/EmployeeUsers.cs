using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Application.Access;
using Merp.Modules.Platform.Contracts.Users;
using Merp.Modules.Platform.Domain.Roles;
using Merp.Modules.Platform.Domain.Users;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Users;

/// <summary>Error codes of the employee–user link (D39); HR shows them on the employee card.</summary>
public static class EmployeeUserErrors
{
    public const string UserNameTaken = "platform.users.errors.user_name_taken";
    public const string EmailTaken = "platform.users.errors.email_taken";
    public const string UserNotFound = "platform.users.errors.user_not_found";
    public const string UserAlreadyLinked = "platform.users.errors.user_already_linked";
    public const string EmployeeAlreadyLinked = "platform.users.errors.employee_already_linked";
    public const string IdentityFailed = "platform.users.errors.identity_failed";
}

/// <summary>Opens the system user of an employee (HR's <see cref="UserProvisioningRequested"/>, D39).</summary>
public sealed record ProvisionUserForEmployeeCommand(UserProvisioningRequested Request) : ICommand;

/// <summary>Links an existing user to an employee (HR's <see cref="UserLinkRequested"/>).</summary>
public sealed record LinkUserToEmployeeCommand(UserLinkRequested Request) : ICommand;

/// <summary>Removes the link of a user and an employee (HR's <see cref="UserUnlinkRequested"/>).</summary>
public sealed record UnlinkUserFromEmployeeCommand(UserUnlinkRequested Request) : ICommand;

/// <summary>Deactivates the user of an employee who left (HR's <see cref="UserDeactivationRequested"/>).</summary>
public sealed record DeactivateEmployeeUserCommand(UserDeactivationRequested Request) : ICommand;

internal static class EmployeeUserLinks
{
    /// <summary>The Employee system role (docs/domain/yetki-ve-onay.md §1.4), given with the Self scope.</summary>
    public static async Task GiveEmployeeRoleAsync(IPlatformDbContext db, User user, CancellationToken cancellationToken)
    {
        var code = SystemRoles.Employee.Code.ToUpperInvariant();
        var roleId = await db.Roles.Where(r => r.Code == code).Select(r => (RoleId?)r.Id).SingleOrDefaultAsync(cancellationToken);
        var scope = new AssignmentScope(AccessScopeTypes.Self);
        if (roleId is { } id && !user.Holds(id, scope))
        {
            user.Assign(id, scope, null, null);
        }
    }

    public static Task<bool> EmployeeLinkedElsewhereAsync(IPlatformDbContext db, Guid employeeId, UserId? except, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.EmployeeId == employeeId && (except == null || u.Id != except), cancellationToken);

    public static Task FailAsync(IIntegrationEventPublisher publisher, Guid employeeId, string errorCode, CancellationToken cancellationToken) =>
        publisher.PublishAsync(new UserProvisioningFailed(employeeId, errorCode), cancellationToken);
}

internal sealed class ProvisionUserForEmployeeCommandHandler(
    IPlatformDbContext db,
    IIdentityAdmin identityAdmin,
    ITenantContext tenant,
    IIntegrationEventPublisher publisher,
    IAccessCache accessCache,
    IAfterCommit afterCommit) : ICommandHandler<ProvisionUserForEmployeeCommand>
{
    public async Task<Result> Handle(ProvisionUserForEmployeeCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var userName = request.UserName.Trim().ToLowerInvariant();
        var email = Email.Create(request.Email);
        string? failure = null;
        if (await EmployeeUserLinks.EmployeeLinkedElsewhereAsync(db, request.EmployeeId, null, cancellationToken))
        {
            failure = EmployeeUserErrors.EmployeeAlreadyLinked;
        }
        else if (email.IsFailure)
        {
            failure = IdentityErrors.InvalidEmailCode;
        }
        else if (await db.Users.AnyAsync(u => u.UserName == userName, cancellationToken))
        {
            failure = EmployeeUserErrors.UserNameTaken;
        }
        else if (await db.Users.AnyAsync(u => u.Email == email.Value.Value, cancellationToken))
        {
            failure = EmployeeUserErrors.EmailTaken;
        }

        if (failure is not null)
        {
            await EmployeeUserLinks.FailAsync(publisher, request.EmployeeId, failure, cancellationToken);
            return Result.Success();
        }

        var identity = await identityAdmin.CreateUserAsync(
            new NewIdentity(userName, email.Value.Value, request.FirstName.Trim(), request.LastName.Trim(), tenant.RequiredTenant.Code),
            cancellationToken);
        if (identity.IsFailure)
        {
            // An unreachable identity provider is retried by the inbox; a refusal is the answer
            if (identity.Error.Code == IdentityErrors.Unavailable.Code)
            {
                return identity.Error;
            }

            var code = identity.Error.Code == IdentityErrors.AlreadyExists.Code ? EmployeeUserErrors.UserNameTaken : EmployeeUserErrors.IdentityFailed;
            await EmployeeUserLinks.FailAsync(publisher, request.EmployeeId, code, cancellationToken);
            return Result.Success();
        }

        var user = User.Create(identity.Value, new UserProfile(userName, email.Value.Value, request.FirstName, request.LastName));
        user.SetCulture(request.Culture);
        user.LinkEmployee(request.EmployeeId);
        await EmployeeUserLinks.GiveEmployeeRoleAsync(db, user, cancellationToken);
        db.Users.Add(user);
        await publisher.PublishAsync(new UserLinkedToEmployee(request.EmployeeId, user.Id.Value), cancellationToken);
        afterCommit.Enqueue(ct => identityAdmin.SendActionsEmailAsync(identity.Value, IdentityEmail.Invitation, ct));
        afterCommit.Enqueue(ct => accessCache.InvalidateUserAsync(user.Id.Value, ct));
        return Result.Success();
    }
}

internal sealed class LinkUserToEmployeeCommandHandler(
    IPlatformDbContext db,
    IIntegrationEventPublisher publisher,
    IAccessCache accessCache,
    IAfterCommit afterCommit) : ICommandHandler<LinkUserToEmployeeCommand>
{
    public async Task<Result> Handle(LinkUserToEmployeeCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var id = UserId.From(request.UserId);
        var user = await db.Users.Include(u => u.Assignments).SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
        var failure = user switch
        {
            null => EmployeeUserErrors.UserNotFound,
            { EmployeeId: { } linked } when linked != request.EmployeeId => EmployeeUserErrors.UserAlreadyLinked,
            _ when await EmployeeUserLinks.EmployeeLinkedElsewhereAsync(db, request.EmployeeId, id, cancellationToken) => EmployeeUserErrors.EmployeeAlreadyLinked,
            _ => null,
        };
        if (failure is not null)
        {
            await EmployeeUserLinks.FailAsync(publisher, request.EmployeeId, failure, cancellationToken);
            return Result.Success();
        }

        user!.LinkEmployee(request.EmployeeId);
        await EmployeeUserLinks.GiveEmployeeRoleAsync(db, user, cancellationToken);
        await publisher.PublishAsync(new UserLinkedToEmployee(request.EmployeeId, user.Id.Value), cancellationToken);
        afterCommit.Enqueue(ct => accessCache.InvalidateUserAsync(user.Id.Value, ct));
        return Result.Success();
    }
}

internal sealed class UnlinkUserFromEmployeeCommandHandler(
    IPlatformDbContext db,
    IIntegrationEventPublisher publisher,
    IAccessCache accessCache,
    IAfterCommit afterCommit) : ICommandHandler<UnlinkUserFromEmployeeCommand>
{
    public async Task<Result> Handle(UnlinkUserFromEmployeeCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var id = UserId.From(request.UserId);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user?.EmployeeId == request.EmployeeId)
        {
            user.UnlinkEmployee();
            afterCommit.Enqueue(ct => accessCache.InvalidateUserAsync(request.UserId, ct));
        }

        // HR clears its side whatever the user's state, so both sides agree again
        await publisher.PublishAsync(new UserUnlinkedFromEmployee(request.EmployeeId, request.UserId), cancellationToken);
        return Result.Success();
    }
}

internal sealed class DeactivateEmployeeUserCommandHandler(
    IPlatformDbContext db,
    IIdentityAdmin identityAdmin,
    IAccessCache accessCache,
    IAfterCommit afterCommit,
    TimeProvider timeProvider) : ICommandHandler<DeactivateEmployeeUserCommand>
{
    public async Task<Result> Handle(DeactivateEmployeeUserCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var id = UserId.From(request.UserId);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, cancellationToken);

        // Only the user still linked to the person; the last administrator is never locked out
        if (user is not { IsActive: true } || user.EmployeeId != request.EmployeeId
            || await UserRules.WouldLeaveNoAdministratorAsync(db, PlatformClock.Today(timeProvider), id, null, cancellationToken))
        {
            return Result.Success();
        }

        var disabled = await identityAdmin.SetEnabledAsync(user.Subject, false, cancellationToken);
        if (disabled.IsFailure)
        {
            return disabled.Error;
        }

        user.Deactivate();
        afterCommit.Enqueue(ct => accessCache.InvalidateUserAsync(request.UserId, ct));
        return Result.Success();
    }
}

/// <summary>Inbox: HR asked for a system user of an employee (D39).</summary>
internal sealed class UserProvisioningRequestedHandler(ISender sender) : IIntegrationEventHandler<UserProvisioningRequested>
{
    public Task Handle(UserProvisioningRequested integrationEvent, CancellationToken cancellationToken) =>
        EmployeeUserInbox.SendAsync(sender, new ProvisionUserForEmployeeCommand(integrationEvent), cancellationToken);
}

/// <summary>Inbox: HR links an employee to a user.</summary>
internal sealed class UserLinkRequestedHandler(ISender sender) : IIntegrationEventHandler<UserLinkRequested>
{
    public Task Handle(UserLinkRequested integrationEvent, CancellationToken cancellationToken) =>
        EmployeeUserInbox.SendAsync(sender, new LinkUserToEmployeeCommand(integrationEvent), cancellationToken);
}

/// <summary>Inbox: HR removes the link of an employee and a user.</summary>
internal sealed class UserUnlinkRequestedHandler(ISender sender) : IIntegrationEventHandler<UserUnlinkRequested>
{
    public Task Handle(UserUnlinkRequested integrationEvent, CancellationToken cancellationToken) =>
        EmployeeUserInbox.SendAsync(sender, new UnlinkUserFromEmployeeCommand(integrationEvent), cancellationToken);
}

/// <summary>Inbox: an employee left and no employment remains.</summary>
internal sealed class UserDeactivationRequestedHandler(ISender sender) : IIntegrationEventHandler<UserDeactivationRequested>
{
    public Task Handle(UserDeactivationRequested integrationEvent, CancellationToken cancellationToken) =>
        EmployeeUserInbox.SendAsync(sender, new DeactivateEmployeeUserCommand(integrationEvent), cancellationToken);
}

internal static class EmployeeUserInbox
{
    /// <summary>A failure here is a technical one (the identity provider is down): the inbox retries the message.</summary>
    public static async Task SendAsync(ISender sender, ICommand command, CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{command.GetType().Name} failed: {result.Error.Code}");
        }
    }
}
