using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Security;
using Merp.Infrastructure.Persistence;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Access;
using Merp.Modules.Platform.Application.Roles;
using Merp.Modules.Platform.Domain.Roles;
using Merp.Modules.Platform.Infrastructure.Persistence;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Merp.Modules.Platform.Infrastructure.Access;

/// <summary>
/// After every migration (PLAN P2): synchronizes the permission catalog with the modules' definitions and seeds the
/// system roles. A permission that appears for the first time is granted to the system roles whose default patterns
/// match it; permissions removed from code disappear from the catalog and from every role. Edits made to system
/// roles are kept.
/// </summary>
internal sealed partial class PlatformTenantSeeder(ILogger<PlatformTenantSeeder> logger) : ITenantDataSeeder
{
    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var defined = services.GetServices<IPermissionDefinitionProvider>()
            .SelectMany(p => p.GetPermissions())
            .Select(p => p.Code)
            .ToHashSet(StringComparer.Ordinal);
        if (!defined.Contains(PlatformPermissions.Roles.Manage))
        {
            // A host without the module registrations must not wipe the catalog
            throw new InvalidOperationException("The permission definitions of the Platform module are not registered.");
        }

        var db = services.GetRequiredService<PlatformDbContext>();
        var localizer = services.GetRequiredService<ITextLocalizer>();

        var existing = await db.Permissions.ToListAsync(cancellationToken);
        var added = defined.Except(existing.Select(p => p.Code), StringComparer.Ordinal).ToList();
        var removed = existing.Where(p => !defined.Contains(p.Code)).ToList();
        db.Permissions.AddRange(added.Select(code => new Permission(code)));
        db.Permissions.RemoveRange(removed);

        var roles = await db.Roles.IgnoreQueryFilters().Include(r => r.Permissions).Where(r => r.IsSystem).ToListAsync(cancellationToken);
        foreach (var definition in SystemRoles.All)
        {
            var code = Code.Create(definition.Code, Role.CodeMaxLength).Value;
            var role = roles.SingleOrDefault(r => r.Code == code.Value);
            if (role is null)
            {
                db.Roles.Add(Role.Create(code, SystemRoleNames.Default(definition, localizer), null, definition.DefaultPermissions(defined), isSystem: true));
            }
            else
            {
                role.Grant(added.Where(definition.Grants));
                role.SetPermissions(role.Permissions.Select(p => p.PermissionCode).Where(defined.Contains));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        LogSynchronized(logger, added.Count, removed.Count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Permission catalog synchronized: {Added} added, {Removed} removed")]
    private static partial void LogSynchronized(ILogger logger, int added, int removed);
}
