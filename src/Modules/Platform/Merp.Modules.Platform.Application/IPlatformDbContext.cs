using Merp.Application.Persistence.Files;
using Merp.Modules.Platform.Domain.Branches;
using Merp.Modules.Platform.Domain.Companies;
using Merp.Modules.Platform.Domain.CustomFields;
using Merp.Modules.Platform.Domain.Localization;
using Merp.Modules.Platform.Domain.Notifications;
using Merp.Modules.Platform.Domain.Numbering;
using Merp.Modules.Platform.Domain.Locations;
using Merp.Modules.Platform.Domain.Roles;
using Merp.Modules.Platform.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Merp.Modules.Platform.Application;

/// <summary>
/// Platform data for handlers. Implemented by the module DbContext in Infrastructure; commands only add or change
/// entities, the unit of work saves them (PLAN 0.5).
/// </summary>
public interface IPlatformDbContext
{
    DbSet<Company> Companies { get; }

    DbSet<Branch> Branches { get; }

    DbSet<Province> Provinces { get; }

    DbSet<District> Districts { get; }

    DbSet<User> Users { get; }

    DbSet<Role> Roles { get; }

    DbSet<Permission> Permissions { get; }

    DbSet<Culture> Cultures { get; }

    DbSet<NumberSequence> NumberSequences { get; }

    DbSet<CustomFieldDefinition> CustomFieldDefinitions { get; }

    DbSet<Notification> Notifications { get; }

    DbSet<EmailMessage> EmailMessages { get; }

    DbSet<NotificationTemplate> NotificationTemplates { get; }

    /// <summary>Files of Platform records: company logos and attachments (D31).</summary>
    DbSet<StoredFile> StoredFiles { get; }

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;
}
