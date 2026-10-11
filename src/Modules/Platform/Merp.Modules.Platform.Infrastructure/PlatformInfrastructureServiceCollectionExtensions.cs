using Merp.Application.Abstractions.CustomFields;
using Merp.Application.Abstractions.Files;
using Merp.Application.Abstractions.Jobs;
using Merp.Application.Abstractions.Notifications;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Numbering;
using Merp.Application.Abstractions.Security;
using Merp.Infrastructure.AI;
using Merp.Infrastructure.Localization;
using Merp.Infrastructure.Messaging;
using Merp.Infrastructure.Persistence;
using Merp.Modules.Platform.Contracts.Approvals;
using Merp.Modules.Platform.Contracts.Calendars;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Access;
using Merp.Modules.Platform.Application.Approvals;
using Merp.Modules.Platform.Application.Calendars;
using Merp.Modules.Platform.Application.CustomFields;
using Merp.Modules.Platform.Application.Files;
using Merp.Modules.Platform.Application.Localization;
using Merp.Modules.Platform.Application.Notifications;
using Merp.Modules.Platform.Application.Numbering;
using Merp.Modules.Platform.Application.Organization;
using Merp.Modules.Platform.Infrastructure.Access;
using Merp.Modules.Platform.Infrastructure.Approvals;
using Merp.Modules.Platform.Infrastructure.Calendars;
using Merp.Modules.Platform.Infrastructure.AI;
using Merp.Modules.Platform.Infrastructure.CustomFields;
using Merp.Modules.Platform.Infrastructure.Identity;
using Merp.Modules.Platform.Infrastructure.Localization;
using Merp.Modules.Platform.Infrastructure.Notifications;
using Merp.Modules.Platform.Infrastructure.Numbering;
using Merp.Modules.Platform.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Merp.Modules.Platform.Infrastructure;

public static class PlatformInfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Platform persistence, handlers, permission checks and data scopes (PLAN P2), the permission catalog and the
    /// tenant seeder. Used by the API host and the migrator.
    /// </summary>
    public static IServiceCollection AddPlatformInfrastructure(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.AddModuleDbContext<PlatformDbContext>(Application.AssemblyReference.Assembly);
        services.AddScoped<IPlatformDbContext>(provider => provider.GetRequiredService<PlatformDbContext>());
        services.AddRequestHandlersFromAssembly(Application.AssemblyReference.Assembly);
        services.AddScoped<AddressResolver>();
        services.AddLocalizationResources(Application.AssemblyReference.Assembly);

        // Users, roles and permissions (docs/domain/yetki-ve-onay.md §1)
        services.AddHybridCache();
        services.AddSingleton<IPermissionDefinitionProvider, PlatformPermissionDefinitions>();
        services.AddSingleton<IScopeTypeProvider, PlatformScopeTypes>();
        services.TryAddSingleton<IScopeTypeRegistry, ScopeTypeRegistry>();
        services.AddScoped<PlatformAccess>();
        services.AddScoped<IPermissionChecker>(provider => provider.GetRequiredService<PlatformAccess>());
        services.AddScoped<IAccessScopeProvider>(provider => provider.GetRequiredService<PlatformAccess>());
        services.AddScoped<IAccessCache, AccessCache>();
        services.AddScoped<ICurrentUserProvisioner, CurrentUserProvisioner>();
        services.AddSingleton<ITenantDataSeeder, PlatformTenantSeeder>();

        // Cultures and translations of the tenant (D11, D28)
        services.Replace(ServiceDescriptor.Scoped<ILocalizationSource, PlatformLocalizationSource>());
        services.AddScoped<ILocalizationCache, LocalizationCache>();
        services.AddSingleton<ITenantDataSeeder, LocalizationSeeder>();

        // Number patterns of all modules; the counters stay in each module (D29)
        services.Replace(ServiceDescriptor.Scoped<INumberSequenceSettingsSource, PlatformNumberSequences>());
        services.AddScoped<INumberingCache, NumberingCache>();
        services.AddSingleton<ITenantDataSeeder, NumberingSeeder>();

        // Custom field definitions; the values stay in each module's records (D12, D30)
        services.AddSingleton<ICustomFieldEntityProvider, PlatformCustomFieldEntities>();
        services.AddScoped<ICustomFieldValidator, PlatformCustomFieldValidator>();
        services.AddScoped<ICustomFieldCache, CustomFieldCache>();

        // Public holidays and work schedules; other modules read them through IWorkingCalendar (PLAN P9, D33)
        services.AddScoped<IWorkingCalendar, PlatformWorkingCalendar>();
        services.AddScoped<ICalendarCache, CalendarCache>();
        services.AddSingleton<ITenantDataSeeder, CalendarSeeder>();
        services.AddScoped<HolidayReminderJob>();
        services.AddSingleton(RecurringJobDefinition.For<HolidayReminderJob>(HolidayReminderJob.JobId, HolidayReminderJob.Cron));

        // Notifications of all modules: bell, e-mail queue and templates (PLAN P8, D32)
        services.AddSingleton<INotificationTypeProvider, PlatformNotificationTypes>();
        services.TryAddSingleton<NotificationTypeRegistry>();
        services.AddScoped<EmailComposer>();
        var notificationSettings = new NotificationSettings();
        configuration?.GetSection(NotificationSettings.SectionName).Bind(notificationSettings);
        services.AddSingleton(notificationSettings);
        services.AddScoped<EmailRetryJob>();
        services.AddSingleton(RecurringJobDefinition.For<EmailRetryJob>(EmailRetryJob.JobId, "* * * * *"));

        // Approval engine of all modules: document types and resolvers come from the modules (docs/domain/yetki-ve-onay.md §2, D34)
        services.AddScoped<ApprovalDocumentTypeRegistry>();
        services.AddScoped<ApproverResolverRegistry>();
        services.AddScoped<IApproverResolver, UserApproverResolver>();
        services.AddScoped<IApproverResolver, RoleApproverResolver>();
        services.AddScoped<IApproverResolver, FactUserApproverResolver>();
        services.AddScoped<ApprovalEngine>();
        services.AddScoped<ApprovalEscalationJob>();
        services.AddSingleton(RecurringJobDefinition.For<ApprovalEscalationJob>(ApprovalEscalationJob.JobId, ApprovalEscalationJob.Cron));

        // Files of Platform records follow the records' permissions (D31)
        services.AddScoped<IFileAccessPolicy, CompanyFileAccessPolicy>();

        var keycloak = services.AddOptions<KeycloakAdminOptions>();
        if (configuration is not null)
        {
            keycloak.Bind(configuration.GetSection(KeycloakAdminOptions.SectionName));
        }

        services.AddSingleton<KeycloakTokenCache>();
        services.AddHttpClient<IIdentityAdmin, KeycloakIdentityAdmin>(client => client.Timeout = TimeSpan.FromSeconds(15));

        // Platform owns the AI usage ledger of all modules (D24).
        services.Replace(ServiceDescriptor.Scoped<IAiUsageLedger, PlatformAiUsageLedger>());
        services.AddScoped<AiUsageReader>();
        return services;
    }
}
