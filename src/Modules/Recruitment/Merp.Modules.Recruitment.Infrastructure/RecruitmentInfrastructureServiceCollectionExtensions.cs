using Merp.Application.Abstractions.Approvals;
using Merp.Application.Abstractions.Notifications;
using Merp.Application.Abstractions.Numbering;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Settings;
using Merp.Infrastructure.Localization;
using Merp.Infrastructure.Messaging;
using Merp.Infrastructure.Persistence;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Application.Openings;
using Merp.Modules.Recruitment.Application.Requisitions;
using Merp.Modules.Recruitment.Application.Settings;
using Merp.Modules.Recruitment.Infrastructure.Persistence;
using Merp.Modules.Recruitment.Infrastructure.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Recruitment.Infrastructure;

public static class RecruitmentInfrastructureServiceCollectionExtensions
{
    /// <summary>Recruitment persistence, handlers, permission catalog and numbered documents. Used by the API host and the migrator.</summary>
    public static IServiceCollection AddRecruitmentInfrastructure(this IServiceCollection services)
    {
        services.AddModuleDbContext<RecruitmentDbContext>(Application.AssemblyReference.Assembly);
        services.AddScoped<IRecruitmentDbContext>(provider => provider.GetRequiredService<RecruitmentDbContext>());
        services.AddRequestHandlersFromAssembly(Application.AssemblyReference.Assembly);
        services.AddSingleton<IPermissionDefinitionProvider, RecruitmentPermissionDefinitions>();
        services.AddSingleton<INumberSequenceDefinitionProvider, RecruitmentNumberSequences>();
        services.AddLocalizationResources(Application.AssemblyReference.Assembly);

        // Settings: stage templates, lists, KVKK texts, retention and the e-mails candidates get (D47)
        services.AddSingleton<ITenantDataSeeder, RecruitmentSeeder>();
        services.AddSingleton<ISettingDefinitionProvider, RecruitmentSettingDefinitions>();
        services.AddSingleton<INotificationTypeProvider, RecruitmentNotificationTypes>();

        // Requisitions: the vacancy checks and the approval flow (D48)
        services.AddScoped<RequisitionRules>();
        services.AddScoped<RequisitionService>();
        services.AddSingleton<IApprovalDocumentTypeProvider, RecruitmentApprovalDocumentTypes>();
        services.AddSingleton<INotificationTypeProvider, RequisitionNotificationTypes>();
        services.AddScoped<IApprovalDocumentAccessPolicy, RequisitionAccessPolicy>();

        // Openings: the checks of the form and the list rows (D49)
        services.AddScoped<OpeningRules>();
        services.AddScoped<OpeningRows>();
        return services;
    }
}
