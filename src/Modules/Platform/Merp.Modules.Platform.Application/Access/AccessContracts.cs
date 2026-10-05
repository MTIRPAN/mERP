using Merp.Application.Abstractions.Security;
using Merp.SharedKernel.Results;

namespace Merp.Modules.Platform.Application.Access;

/// <summary>
/// Cached effective permissions and scopes (tag <c>user:{id}:perm</c>, docs/domain/yetki-ve-onay.md §1.3). Clear
/// it after the command commits (<see cref="Merp.Application.Abstractions.Messaging.IAfterCommit"/>).
/// </summary>
public interface IAccessCache
{
    /// <summary>A user's assignments, active flag or preferences changed.</summary>
    Task InvalidateUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>A role's permissions changed: every user of the tenant may be affected.</summary>
    Task InvalidateAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Users and roles are tenant level administration: their permissions only count with the Tenant scope.
/// A company scoped administrator gets 403 (docs/domain/yetki-ve-onay.md §1.3).
/// </summary>
public static class TenantAdministration
{
    public static readonly Error TenantScopeRequired = Error.Forbidden("platform.users.errors.tenant_scope_required");

    public static async Task<bool> IsAllowedAsync(IAccessScopeProvider scopes, string permission, CancellationToken cancellationToken) =>
        (await scopes.GetCurrentUserScopesAsync(permission, cancellationToken)).Any(s => s.Type == AccessScopeTypes.Tenant);
}

/// <summary>
/// User administration in the identity provider (Keycloak admin API, D5). Implementations never log personal data.
/// </summary>
public interface IIdentityAdmin
{
    /// <summary>Creates the sign-in identity with the tenant attribute; returns its subject (<c>sub</c>).</summary>
    Task<Result<string>> CreateUserAsync(NewIdentity identity, CancellationToken cancellationToken = default);

    Task<Result> SetEnabledAsync(string subject, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>E-mails the user a link to set a password (and verify the address for an invitation).</summary>
    Task<Result> SendActionsEmailAsync(string subject, IdentityEmail email, CancellationToken cancellationToken = default);
}

public sealed record NewIdentity(string UserName, string Email, string FirstName, string LastName, string TenantCode);

public enum IdentityEmail
{
    /// <summary>Verify the e-mail address and set a password.</summary>
    Invitation,

    /// <summary>Set a new password.</summary>
    PasswordReset,
}

/// <summary>Errors of the identity provider integration.</summary>
public static class IdentityErrors
{
    public static readonly Error Unavailable = Error.Unavailable("platform.users.errors.identity_unavailable");

    /// <summary>The user name or e-mail address is already taken in the identity provider.</summary>
    public static readonly Error AlreadyExists = Error.Conflict("platform.users.errors.identity_exists");
}
