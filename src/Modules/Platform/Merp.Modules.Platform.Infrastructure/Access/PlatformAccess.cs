using System.ComponentModel;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Application.Access;
using Merp.Modules.Platform.Domain.Users;
using Merp.Modules.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Merp.Modules.Platform.Infrastructure.Access;

/// <summary>
/// Permission checks and data scopes from role assignments (docs/domain/yetki-ve-onay.md §1.3): a permission is held
/// when an active user has an assignment in force today whose role grants it; the scopes are the union of those
/// assignments' scopes. The result is cached per user until the end of the day at most and cleared by tag when
/// roles or assignments change (<see cref="AccessCache"/>).
/// </summary>
internal sealed class PlatformAccess(
    PlatformDbContext db,
    ICurrentUser currentUser,
    ITenantContext tenant,
    HybridCache cache,
    TimeProvider timeProvider) : IPermissionChecker, IAccessScopeProvider
{
    private static readonly TimeSpan MaxCacheDuration = TimeSpan.FromMinutes(10);
    private readonly Dictionary<Guid, EffectiveAccess> _loaded = [];

    public async Task<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken = default) =>
        currentUser.UserId is { } userId && (await GetAsync(userId, cancellationToken)).Permissions.ContainsKey(permission);

    public async Task<IReadOnlyList<AccessScope>> GetScopesAsync(Guid userId, string permission, CancellationToken cancellationToken = default) =>
        (await GetAsync(userId, cancellationToken)).Permissions.TryGetValue(permission, out var scopes) ? scopes : [];

    public Task<IReadOnlyList<AccessScope>> GetCurrentUserScopesAsync(string permission, CancellationToken cancellationToken = default) =>
        currentUser.UserId is { } userId ? GetScopesAsync(userId, permission, cancellationToken) : Task.FromResult<IReadOnlyList<AccessScope>>([]);

    /// <summary>All permissions the user holds today, with their scopes.</summary>
    public async Task<EffectiveAccess> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (_loaded.TryGetValue(userId, out var access))
        {
            return access;
        }

        var tenantId = tenant.RequiredTenant.Id;
        var untilTomorrow = PlatformClock.EndOfToday(timeProvider) - timeProvider.GetUtcNow();
        var duration = untilTomorrow < MaxCacheDuration ? untilTomorrow : MaxCacheDuration;
        access = await cache.GetOrCreateAsync(
            AccessCache.AccessKey(tenantId, userId),
            (Access: this, UserId: userId),
            static (state, ct) => new ValueTask<EffectiveAccess>(state.Access.LoadAsync(state.UserId, ct)),
            new HybridCacheEntryOptions { Expiration = duration, LocalCacheExpiration = duration },
            [AccessCache.TenantTag(tenantId), AccessCache.UserTag(tenantId, userId)],
            cancellationToken);
        _loaded[userId] = access;
        return access;
    }

    private async Task<EffectiveAccess> LoadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().Include(u => u.Assignments).SingleOrDefaultAsync(u => u.Id == UserId.From(userId), cancellationToken);
        if (user is null)
        {
            return EffectiveAccess.None;
        }

        var assignments = user.ValidAssignments(PlatformClock.Today(timeProvider)).ToList();
        var roleIds = assignments.Select(a => a.RoleId).Distinct().ToList();
        var grants = await db.Roles.AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => new { r.Id, Permissions = r.Permissions.Select(p => p.PermissionCode).ToList() })
            .ToDictionaryAsync(r => r.Id, r => r.Permissions, cancellationToken);

        var permissions = new Dictionary<string, HashSet<AccessScope>>(StringComparer.Ordinal);
        foreach (var assignment in assignments)
        {
            var scope = new AccessScope(assignment.ScopeType, assignment.ScopeId, assignment.IncludeSubUnits);
            foreach (var permission in grants.GetValueOrDefault(assignment.RoleId) ?? [])
            {
                if (!permissions.TryGetValue(permission, out var scopes))
                {
                    permissions[permission] = scopes = [];
                }

                scopes.Add(scope);
            }
        }

        return new EffectiveAccess(permissions.ToDictionary(p => p.Key, p => (IReadOnlyList<AccessScope>)[.. p.Value], StringComparer.Ordinal));
    }
}

/// <summary>Permission code → scopes in which the user holds it. Never mutated, so the cache can hand out the same instance.</summary>
[ImmutableObject(true)]
public sealed record EffectiveAccess(IReadOnlyDictionary<string, IReadOnlyList<AccessScope>> Permissions)
{
    public static readonly EffectiveAccess None = new(new Dictionary<string, IReadOnlyList<AccessScope>>(StringComparer.Ordinal));
}

/// <summary>Cache keys and tags of access data; invalidation runs after the command commits.</summary>
internal sealed class AccessCache(HybridCache cache, ITenantContext tenant) : IAccessCache
{
    public static string AccessKey(Guid tenantId, Guid userId) => $"merp:{tenantId}:access:{userId}";

    public static string UserKey(Guid tenantId, Guid userId) => $"merp:{tenantId}:user:{userId}";

    public static string SubjectKey(Guid tenantId, string subject) => $"merp:{tenantId}:subject:{subject}";

    /// <summary>Every access entry of the tenant.</summary>
    public static string TenantTag(Guid tenantId) => $"{tenantId}:perm";

    /// <summary>The user's access and profile entries (docs: <c>user:{id}:perm</c>).</summary>
    public static string UserTag(Guid tenantId, Guid userId) => $"{tenantId}:user:{userId}:perm";

    public async Task InvalidateUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await cache.RemoveByTagAsync(UserTag(tenant.RequiredTenant.Id, userId), cancellationToken);

    public async Task InvalidateAllAsync(CancellationToken cancellationToken = default) =>
        await cache.RemoveByTagAsync(TenantTag(tenant.RequiredTenant.Id), cancellationToken);
}
