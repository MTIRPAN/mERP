using Merp.Infrastructure.Persistence;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Domain.Approvals;
using Merp.Modules.Platform.Domain.Imports;
using Merp.Modules.Platform.Domain.Branches;
using Merp.Modules.Platform.Domain.Calendars;
using Merp.Modules.Platform.Domain.Companies;
using Merp.Modules.Platform.Domain.CustomFields;
using Merp.Modules.Platform.Domain.Localization;
using Merp.Modules.Platform.Domain.Notifications;
using Merp.Modules.Platform.Domain.Numbering;
using Merp.Modules.Platform.Domain.Locations;
using Merp.Modules.Platform.Domain.Roles;
using Merp.Modules.Platform.Domain.Settings;
using Merp.Modules.Platform.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Infrastructure.Persistence;

public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : ModuleDbContext(options), IModuleSchema, IPlatformDbContext
{
    public static string SchemaName => "platform";

    public override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Options are stored as jsonb by a value converter, not as entities
        modelBuilder.Ignore<CustomFieldOption>();
        modelBuilder.Ignore<NotificationPreference>();
        modelBuilder.Ignore<WorkDay>();
        modelBuilder.Ignore<ApprovalStep>();
        modelBuilder.Ignore<ApproverRule>();
        modelBuilder.Ignore<ImportJobError>();
        base.OnModelCreating(modelBuilder);
    }

    public DbSet<Company> Companies => Set<Company>();

    public DbSet<Branch> Branches => Set<Branch>();

    public DbSet<Province> Provinces => Set<Province>();

    public DbSet<District> Districts => Set<District>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<Culture> Cultures => Set<Culture>();

    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();

    public DbSet<CustomFieldDefinition> CustomFieldDefinitions => Set<CustomFieldDefinition>();

    public DbSet<HolidayCalendar> HolidayCalendars => Set<HolidayCalendar>();

    public DbSet<WorkSchedule> WorkSchedules => Set<WorkSchedule>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<EmailMessage> EmailMessages => Set<EmailMessage>();

    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();

    public DbSet<ApprovalDefinition> ApprovalDefinitions => Set<ApprovalDefinition>();

    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();

    public DbSet<Delegation> Delegations => Set<Delegation>();

    public DbSet<SettingsProfile> SettingsProfiles => Set<SettingsProfile>();

    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
}
