using Merp.Application.Abstractions.Context;

namespace Merp.Application.Abstractions.Security;

public static class AccessScopeExtensions
{
    private static readonly IReadOnlyList<AccessScope> TenantWide = [new(AccessScopeTypes.Tenant)];

    /// <summary>Scopes of the current caller for <paramref name="permission"/>; system processes see the whole tenant.</summary>
    public static Task<IReadOnlyList<AccessScope>> GetScopesAsync(
        this IAccessScopeProvider provider,
        ICurrentUser user,
        string permission,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(user);
        return user.IsSystem
            ? Task.FromResult(TenantWide)
            : provider.GetScopesAsync(user.UserId ?? Guid.Empty, permission, cancellationToken);
    }
}
