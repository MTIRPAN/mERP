using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Validation;
using Merp.Modules.Platform.Application.Access;
using Merp.Modules.Platform.Domain.Branches;
using Merp.Modules.Platform.Domain.Companies;
using Merp.Modules.Platform.Domain.Roles;
using Merp.Modules.Platform.Domain.Users;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Users;

/// <summary>
/// Creates a user: the Keycloak identity with the tenant attribute, its platform mirror and, after the commit, the
/// invitation e-mail to set a password. Roles are assigned separately.
/// </summary>
public sealed record CreateUserCommand(string? UserName, string? Email, string? FirstName, string? LastName, string? Culture)
    : ICommand<Guid>, IRequirePermission
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.Users.Manage;
}

/// <summary>Activates or deactivates a user here and in Keycloak. A user cannot deactivate themselves.</summary>
public sealed record SetUserActiveCommand(bool IsActive) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => PlatformPermissions.Users.Manage;
}

/// <summary>E-mails the user a link to set a new password (Keycloak).</summary>
public sealed record SendPasswordResetCommand(Guid Id) : ICommand, IRequirePermission
{
    public string Permission => PlatformPermissions.Users.Manage;
}

/// <summary>Gives a user a role in a data scope, optionally for a period (both ends inclusive).</summary>
/// <remarks>ScopeId names the company, branch or unit for scope types that require one.</remarks>
public sealed record AssignRoleCommand(
    Guid RoleId,
    string? ScopeType,
    Guid? ScopeId,
    bool IncludeSubUnits,
    DateOnly? ValidFrom,
    DateOnly? ValidTo) : ICommand<Guid>, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid UserId { get; init; }

    [JsonIgnore]
    public string Permission => PlatformPermissions.Users.Manage;
}

/// <summary>Changes the period of an assignment; ending it today or earlier withdraws the role.</summary>
public sealed record ChangeAssignmentCommand(DateOnly? ValidFrom, DateOnly? ValidTo) : ICommand, IRequirePermission
{
    [JsonIgnore]
    public Guid UserId { get; init; }

    [JsonIgnore]
    public Guid AssignmentId { get; init; }

    [JsonIgnore]
    public string Permission => PlatformPermissions.Users.Manage;
}

public sealed record RemoveAssignmentCommand(Guid UserId, Guid AssignmentId) : ICommand, IRequirePermission
{
    public string Permission => PlatformPermissions.Users.Manage;
}

/// <summary>The signed-in user's own preferences; needs no permission (D22).</summary>
/// <remarks>Culture is one of the active cultures, or null to follow the browser.</remarks>
public sealed record UpdateMyPreferencesCommand(string? Culture) : ICommand;

internal static class UserRules
{
    public static readonly Error CannotDeactivateSelf = Error.Conflict("platform.users.errors.cannot_deactivate_self");
    public static readonly Error LastAdministrator = Error.Conflict("platform.users.errors.last_administrator");
    public const string UnknownRoleCode = "platform.users.errors.unknown_role";
    public const string UnknownScopeTypeCode = "platform.users.errors.unknown_scope_type";
    public const string UnknownScopeCode = "platform.users.errors.unknown_scope";

    /// <summary>
    /// True when, without <paramref name="exceptUser"/> (or only its assignment <paramref name="exceptAssignment"/>),
    /// no active user would keep the System Administrator role with the Tenant scope today. Prevents lock-out.
    /// </summary>
    public static async Task<bool> WouldLeaveNoAdministratorAsync(
        IPlatformDbContext db,
        DateOnly today,
        UserId exceptUser,
        RoleAssignmentId? exceptAssignment,
        CancellationToken cancellationToken)
    {
        var adminCode = SystemRoles.SystemAdministrator.Code.ToUpperInvariant();
        var adminRole = await db.Roles.Where(r => r.Code == adminCode).Select(r => (RoleId?)r.Id).SingleOrDefaultAsync(cancellationToken);
        if (adminRole is not { } roleId)
        {
            return false;
        }

        var admins = await db.Users.AsNoTracking()
            .Where(u => u.IsActive)
            .SelectMany(u => u.Assignments.Select(a => new { User = u.Id, Assignment = a.Id, a.RoleId, a.ScopeType, a.ValidFrom, a.ValidTo }))
            .Where(a => a.RoleId == roleId && a.ScopeType == AccessScopeTypes.Tenant
                && (a.ValidFrom == null || a.ValidFrom <= today) && (a.ValidTo == null || a.ValidTo >= today))
            .ToListAsync(cancellationToken);

        var holdersBefore = admins.Select(a => a.User).Distinct().Count();
        if (holdersBefore == 0)
        {
            // Nobody administers the tenant yet (bootstrap); nothing to protect
            return false;
        }

        return !admins.Any(a => exceptAssignment is { } assignment ? a.Assignment != assignment : a.User != exceptUser);
    }
}

internal sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(c => c.UserName).Cascade(CascadeMode.Stop).NotEmpty().MaximumLength(User.UserNameMaxLength)
            .Matches("^[A-Za-z0-9._@-]+$").WithErrorCode("platform.users.errors.invalid_user_name");
        RuleFor(c => c.Email).Cascade(CascadeMode.Stop).NotEmpty().MustBeValid(Email.Create);
        RuleFor(c => c.FirstName).NotEmpty().MaximumLength(User.NameMaxLength);
        RuleFor(c => c.LastName).NotEmpty().MaximumLength(User.NameMaxLength);
    }
}

internal sealed class CreateUserCommandHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    IIdentityAdmin identityAdmin,
    ITenantContext tenant,
    ISupportedCultures cultures,
    IAfterCommit afterCommit) : ICommandHandler<CreateUserCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return TenantAdministration.TenantScopeRequired;
        }

        var userName = request.UserName!.Trim().ToLowerInvariant();
        var email = Email.Create(request.Email).Value.Value;
        var errors = new List<FieldError>();
        if (await db.Users.AnyAsync(u => u.UserName == userName, cancellationToken))
        {
            errors.Add(new FieldError("userName", CommonErrors.Duplicate.Code));
        }

        if (await db.Users.AnyAsync(u => u.Email == email, cancellationToken))
        {
            errors.Add(new FieldError("email", CommonErrors.Duplicate.Code));
        }

        if (request.Culture is { } culture && !cultures.IsSupported(culture))
        {
            errors.Add(new FieldError("culture", CommonErrors.InvalidCulture.Code));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var identity = await identityAdmin.CreateUserAsync(
            new NewIdentity(userName, email, request.FirstName!.Trim(), request.LastName!.Trim(), tenant.RequiredTenant.Code),
            cancellationToken);
        if (identity.IsFailure)
        {
            return identity.Error;
        }

        var user = User.Create(identity.Value, new UserProfile(userName, email, request.FirstName, request.LastName));
        user.SetCulture(request.Culture);
        db.Users.Add(user);
        afterCommit.Enqueue(ct => identityAdmin.SendActionsEmailAsync(identity.Value, IdentityEmail.Invitation, ct));
        return user.Id.Value;
    }
}

internal sealed class SetUserActiveCommandHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    IIdentityAdmin identityAdmin,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    IAccessCache accessCache,
    IAfterCommit afterCommit) : ICommandHandler<SetUserActiveCommand>
{
    public async Task<Result> Handle(SetUserActiveCommand request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return TenantAdministration.TenantScopeRequired;
        }

        var id = UserId.From(request.Id);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return CommonErrors.NotFound;
        }

        if (user.IsActive == request.IsActive)
        {
            return Result.Success();
        }

        if (!request.IsActive)
        {
            if (currentUser.UserId == request.Id)
            {
                return UserRules.CannotDeactivateSelf;
            }

            if (await UserRules.WouldLeaveNoAdministratorAsync(db, PlatformClock.Today(timeProvider), id, null, cancellationToken))
            {
                return UserRules.LastAdministrator;
            }
        }

        var enabled = await identityAdmin.SetEnabledAsync(user.Subject, request.IsActive, cancellationToken);
        if (enabled.IsFailure)
        {
            return enabled.Error;
        }

        if (request.IsActive)
        {
            user.Activate();
        }
        else
        {
            user.Deactivate();
        }

        afterCommit.Enqueue(ct => accessCache.InvalidateUserAsync(request.Id, ct));
        return Result.Success();
    }
}

internal sealed class SendPasswordResetCommandHandler(IPlatformDbContext db, IAccessScopeProvider scopes, IIdentityAdmin identityAdmin)
    : ICommandHandler<SendPasswordResetCommand>
{
    public async Task<Result> Handle(SendPasswordResetCommand request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return TenantAdministration.TenantScopeRequired;
        }

        var id = UserId.From(request.Id);
        var user = await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => new { u.Subject, u.IsActive }).SingleOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return CommonErrors.NotFound;
        }

        return user.IsActive
            ? await identityAdmin.SendActionsEmailAsync(user.Subject, IdentityEmail.PasswordReset, cancellationToken)
            : Error.Conflict("platform.users.errors.inactive");
    }
}

internal sealed class AssignRoleCommandValidator : AbstractValidator<AssignRoleCommand>
{
    public AssignRoleCommandValidator()
    {
        RuleFor(c => c.RoleId).NotEmpty();
        RuleFor(c => c.ScopeType).NotEmpty();
    }
}

internal sealed class AssignRoleCommandHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    IScopeTypeRegistry scopeTypes,
    IAccessCache accessCache,
    IAfterCommit afterCommit) : ICommandHandler<AssignRoleCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AssignRoleCommand request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return TenantAdministration.TenantScopeRequired;
        }

        var user = await db.Users.Include(u => u.Assignments).SingleOrDefaultAsync(u => u.Id == UserId.From(request.UserId), cancellationToken);
        if (user is null)
        {
            return CommonErrors.NotFound;
        }

        var errors = new List<FieldError>();
        var roleId = RoleId.From(request.RoleId);
        if (!await db.Roles.AnyAsync(r => r.Id == roleId, cancellationToken))
        {
            errors.Add(new FieldError("roleId", UserRules.UnknownRoleCode));
        }

        errors.AddRange(await ValidateScopeAsync(request, cancellationToken));
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var scope = new AssignmentScope(request.ScopeType!, request.ScopeId, request.IncludeSubUnits);
        var assignment = user.Assign(roleId, scope, request.ValidFrom, request.ValidTo);
        if (assignment.IsFailure)
        {
            return assignment.Error;
        }

        afterCommit.Enqueue(ct => accessCache.InvalidateUserAsync(request.UserId, ct));
        return assignment.Value.Id.Value;
    }

    private async Task<List<FieldError>> ValidateScopeAsync(AssignRoleCommand request, CancellationToken cancellationToken)
    {
        var type = scopeTypes.Find(request.ScopeType!);
        if (type is null)
        {
            return [new FieldError("scopeType", UserRules.UnknownScopeTypeCode)];
        }

        var errors = new List<FieldError>();
        if (type.RequiresId != request.ScopeId is not null)
        {
            errors.Add(new FieldError("scopeId", type.RequiresId ? CommonErrors.Required.Code : "common.errors.invalid_value"));
        }

        if (request.IncludeSubUnits && !type.SupportsSubUnits)
        {
            errors.Add(new FieldError("includeSubUnits", "common.errors.invalid_value"));
        }

        // Platform checks its own scope records; other modules' records are checked when they resolve the scope
        if (errors.Count == 0 && request.ScopeId is { } scopeId)
        {
            var exists = type.Code switch
            {
                AccessScopeTypes.Company => await db.Companies.AnyAsync(c => c.Id == CompanyId.From(scopeId), cancellationToken),
                AccessScopeTypes.Branch => await db.Branches.AnyAsync(b => b.Id == BranchId.From(scopeId), cancellationToken),
                _ => true,
            };
            if (!exists)
            {
                errors.Add(new FieldError("scopeId", UserRules.UnknownScopeCode));
            }
        }

        return errors;
    }
}

internal sealed class ChangeAssignmentCommandHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    TimeProvider timeProvider,
    IAccessCache accessCache,
    IAfterCommit afterCommit) : ICommandHandler<ChangeAssignmentCommand>
{
    public async Task<Result> Handle(ChangeAssignmentCommand request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return TenantAdministration.TenantScopeRequired;
        }

        var userId = UserId.From(request.UserId);
        var user = await db.Users.Include(u => u.Assignments).SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return CommonErrors.NotFound;
        }

        var assignmentId = RoleAssignmentId.From(request.AssignmentId);
        var today = PlatformClock.Today(timeProvider);
        var endsBeforeToday = (request.ValidTo is { } to && to < today) || (request.ValidFrom is { } from && from > today);
        if (endsBeforeToday && await UserRules.WouldLeaveNoAdministratorAsync(db, today, userId, assignmentId, cancellationToken))
        {
            return UserRules.LastAdministrator;
        }

        var changed = user.ChangeValidity(assignmentId, request.ValidFrom, request.ValidTo);
        if (changed.IsSuccess)
        {
            afterCommit.Enqueue(ct => accessCache.InvalidateUserAsync(request.UserId, ct));
        }

        return changed;
    }
}

internal sealed class RemoveAssignmentCommandHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    TimeProvider timeProvider,
    IAccessCache accessCache,
    IAfterCommit afterCommit) : ICommandHandler<RemoveAssignmentCommand>
{
    public async Task<Result> Handle(RemoveAssignmentCommand request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return TenantAdministration.TenantScopeRequired;
        }

        var userId = UserId.From(request.UserId);
        var user = await db.Users.Include(u => u.Assignments).SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return CommonErrors.NotFound;
        }

        var assignmentId = RoleAssignmentId.From(request.AssignmentId);
        if (await UserRules.WouldLeaveNoAdministratorAsync(db, PlatformClock.Today(timeProvider), userId, assignmentId, cancellationToken))
        {
            return UserRules.LastAdministrator;
        }

        var removed = user.Unassign(assignmentId);
        if (removed.IsSuccess)
        {
            afterCommit.Enqueue(ct => accessCache.InvalidateUserAsync(request.UserId, ct));
        }

        return removed;
    }
}

internal sealed class UpdateMyPreferencesCommandHandler(
    IPlatformDbContext db,
    ICurrentUser currentUser,
    ISupportedCultures cultures,
    IAccessCache accessCache,
    IAfterCommit afterCommit) : ICommandHandler<UpdateMyPreferencesCommand>
{
    public async Task<Result> Handle(UpdateMyPreferencesCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return CommonErrors.Forbidden;
        }

        if (request.Culture is { } culture && !cultures.IsSupported(culture))
        {
            return Error.Validation([new FieldError("culture", CommonErrors.InvalidCulture.Code)]);
        }

        var user = await db.Users.SingleAsync(u => u.Id == UserId.From(userId), cancellationToken);
        user.SetCulture(request.Culture);
        afterCommit.Enqueue(ct => accessCache.InvalidateUserAsync(userId, ct));
        return Result.Success();
    }
}

/// <summary>
/// Creates the first administrator of a tenant (<c>Merp.Migrator create-admin</c>): a user with the System
/// Administrator role in the Tenant scope and the invitation e-mail. Only system callers may send it.
/// </summary>
public sealed record CreateAdministratorCommand(string UserName, string Email, string FirstName, string LastName) : ICommand<Guid>;

internal sealed class CreateAdministratorCommandHandler(
    IPlatformDbContext db,
    ICurrentUser currentUser,
    IIdentityAdmin identityAdmin,
    ITenantContext tenant,
    IAfterCommit afterCommit) : ICommandHandler<CreateAdministratorCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateAdministratorCommand request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsSystem)
        {
            return CommonErrors.Forbidden;
        }

        var adminCode = SystemRoles.SystemAdministrator.Code.ToUpperInvariant();
        var role = await db.Roles.SingleAsync(r => r.Code == adminCode, cancellationToken);
        var userName = request.UserName.Trim().ToLowerInvariant();
        var email = Email.Create(request.Email);
        if (email.IsFailure)
        {
            return email.Error.ForField("email");
        }

        if (await db.Users.AnyAsync(u => u.UserName == userName || u.Email == email.Value.Value, cancellationToken))
        {
            return Error.Validation([new FieldError("userName", CommonErrors.Duplicate.Code)]);
        }

        var identity = await identityAdmin.CreateUserAsync(
            new NewIdentity(userName, email.Value.Value, request.FirstName.Trim(), request.LastName.Trim(), tenant.RequiredTenant.Code),
            cancellationToken);
        if (identity.IsFailure)
        {
            return identity.Error;
        }

        var user = User.Create(identity.Value, new UserProfile(userName, email.Value.Value, request.FirstName, request.LastName));
        user.Assign(role.Id, new AssignmentScope(AccessScopeTypes.Tenant), null, null);
        db.Users.Add(user);
        afterCommit.Enqueue(ct => identityAdmin.SendActionsEmailAsync(identity.Value, IdentityEmail.Invitation, ct));
        return user.Id.Value;
    }
}
