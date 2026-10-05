using System.ComponentModel;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Domain.Roles;
using Merp.Modules.Platform.Domain.Users;
using Merp.Modules.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Merp.Modules.Platform.Infrastructure.Access;

/// <summary>
/// Just-in-time provisioning (D6): the first request of a Keycloak identity creates its platform user, without
/// roles. The profile (name, e-mail) is refreshed from the token when the subject mapping is reloaded (hourly).
/// <para>
/// <c>Platform:Bootstrap:Users</c> gives roles to listed user names when they are created, e.g.
/// <c>"admin": ["system-admin"]</c> or <c>"calisan": ["employee:Self"]</c> (role code, then scope type; Tenant by
/// default). Development uses it for the realm's test users; production uses <c>Merp.Migrator create-admin</c>.
/// </para>
/// </summary>
internal sealed partial class CurrentUserProvisioner(
    PlatformDbContext db,
    ITenantContext tenant,
    HybridCache cache,
    IConfiguration configuration,
    ILogger<CurrentUserProvisioner> logger) : ICurrentUserProvisioner
{
    private static readonly HybridCacheEntryOptions SubjectEntry = new() { Expiration = TimeSpan.FromHours(1), LocalCacheExpiration = TimeSpan.FromHours(1) };
    private static readonly HybridCacheEntryOptions UserEntry = new() { Expiration = TimeSpan.FromMinutes(10), LocalCacheExpiration = TimeSpan.FromMinutes(10) };

    public async Task<ProvisionedUser> ProvisionAsync(IdentityClaims identity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var tenantId = tenant.RequiredTenant.Id;
        var userId = await cache.GetOrCreateAsync(
            AccessCache.SubjectKey(tenantId, identity.Subject),
            (Provisioner: this, Identity: identity),
            static (state, ct) => new ValueTask<Guid>(state.Provisioner.FindOrCreateAsync(state.Identity, ct)),
            SubjectEntry,
            cancellationToken: cancellationToken);

        var snapshot = await cache.GetOrCreateAsync(
            AccessCache.UserKey(tenantId, userId),
            (Db: db, UserId: UserId.From(userId)),
            static async (state, ct) => await state.Db.Users.AsNoTracking()
                .Where(u => u.Id == state.UserId)
                .Select(u => new UserSnapshot(u.IsActive, u.EmployeeId, u.Culture))
                .SingleAsync(ct),
            UserEntry,
            [AccessCache.UserTag(tenantId, userId)],
            cancellationToken);

        return new ProvisionedUser(userId, snapshot.IsActive, snapshot.EmployeeId, snapshot.Culture);
    }

    private async Task<Guid> FindOrCreateAsync(IdentityClaims identity, CancellationToken cancellationToken)
    {
        var profile = Profile(identity);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Subject == identity.Subject, cancellationToken);
        if (user is not null)
        {
            if (user.UpdateProfile(profile))
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            return user.Id.Value;
        }

        user = User.Create(identity.Subject, profile);
        await BootstrapAsync(user, cancellationToken);
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            LogProvisioned(logger, user.Id.Value, tenant.RequiredTenant.Code);
            return user.Id.Value;
        }
        catch (DbUpdateException)
        {
            // A parallel first request created the user (unique subject); use theirs
            db.ChangeTracker.Clear();
            return await db.Users.Where(u => u.Subject == identity.Subject).Select(u => u.Id.Value).SingleAsync(cancellationToken);
        }
    }

    private async Task BootstrapAsync(User user, CancellationToken cancellationToken)
    {
        var specs = configuration.GetSection($"Platform:Bootstrap:Users:{user.UserName}").Get<string[]>() ?? [];
        foreach (var spec in specs)
        {
            var parts = spec.Split(':', 2, StringSplitOptions.TrimEntries);
            var code = parts[0].ToUpperInvariant();
            var role = await db.Roles.Where(r => r.Code == code).Select(r => (RoleId?)r.Id).SingleOrDefaultAsync(cancellationToken);
            if (role is { } roleId)
            {
                user.Assign(roleId, new AssignmentScope(parts.Length > 1 ? parts[1] : AccessScopeTypes.Tenant), null, null);
            }
        }
    }

    private static UserProfile Profile(IdentityClaims identity) =>
        new(
            identity.UserName is { Length: > 0 } userName ? userName.ToLowerInvariant() : identity.Subject,
            identity.Email,
            identity.GivenName,
            identity.FamilyName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Provisioned user {UserId} in tenant {TenantCode}")]
    private static partial void LogProvisioned(ILogger logger, Guid userId, string tenantCode);
}

/// <summary>What every request needs to know about its user. Never mutated.</summary>
[ImmutableObject(true)]
internal sealed record UserSnapshot(bool IsActive, Guid? EmployeeId, string? Culture);
