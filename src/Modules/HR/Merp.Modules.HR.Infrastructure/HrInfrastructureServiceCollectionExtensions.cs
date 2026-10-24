using Merp.Application.Abstractions.Approvals;
using Merp.Application.Abstractions.CustomFields;
using Merp.Application.Abstractions.Imports;
using Merp.Application.Abstractions.Jobs;
using Merp.Application.Abstractions.Notifications;
using Merp.Application.Abstractions.Settings;
using Merp.Application.Abstractions.Numbering;
using Merp.Application.Abstractions.Security;
using Merp.Infrastructure.Localization;
using Merp.Infrastructure.Messaging;
using Merp.Infrastructure.Persistence;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.Approvals;
using Merp.Modules.HR.Application.Documents;
using Merp.Modules.HR.Application.Employees;
using Merp.Modules.HR.Application.Imports;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Application.SelfService;
using Merp.Modules.HR.Contracts.Organization;
using Merp.Modules.HR.Infrastructure.Documents;
using Merp.Modules.HR.Infrastructure.Employees;
using Merp.Modules.HR.Infrastructure.Leave;
using Merp.Modules.HR.Infrastructure.Organization;
using Merp.Modules.HR.Infrastructure.Persistence;
using Merp.Modules.Platform.Contracts.Approvals;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.HR.Infrastructure;

public static class HrInfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// HR persistence, handlers, permission catalog, scope types, the organization (D37) and its registrations in
    /// Platform. Used by the API host and the migrator.
    /// </summary>
    public static IServiceCollection AddHrInfrastructure(this IServiceCollection services)
    {
        services.AddModuleDbContext<HrDbContext>(Application.AssemblyReference.Assembly);
        services.AddScoped<IHrDbContext>(provider => provider.GetRequiredService<HrDbContext>());
        services.AddRequestHandlersFromAssembly(Application.AssemblyReference.Assembly);
        services.AddLocalizationResources(Application.AssemblyReference.Assembly);
        services.AddSingleton<IPermissionDefinitionProvider, HrPermissionDefinitions>();
        services.AddSingleton<IScopeTypeProvider, HrScopeTypes>();

        // Organization: units, positions and reporting lines (D37)
        services.AddScoped<IPositionHolders, AssignmentPositionHolders>();
        services.AddScoped<IOrgStructureStore, OrgStructureStore>();
        services.AddScoped<IReportingLines, ReportingLineReader>();
        services.AddScoped<ReportingLinesBuilder>();
        services.AddScoped<IOrgQuery, OrgQuery>();
        services.AddScoped<ReportingLinesJob>();
        services.AddSingleton(RecurringJobDefinition.For<ReportingLinesJob>(ReportingLinesJob.JobId, ReportingLinesJob.Cron));

        // Employees: numbering, custom fields, SGK codes and the hourly announcement of started assignments (D38)
        services.AddSingleton<INumberSequenceDefinitionProvider, HrNumberSequences>();
        services.AddSingleton<ICustomFieldEntityProvider, HrCustomFieldEntities>();
        services.AddSingleton<ITenantDataSeeder, HrReferenceDataSeeder>();
        services.AddSingleton<ITenantDataSeeder, LeaveSeeder>();
        services.AddScoped<AssignmentsEffectiveJob>();
        services.AddSingleton(RecurringJobDefinition.For<AssignmentsEffectiveJob>(AssignmentsEffectiveJob.JobId, AssignmentsEffectiveJob.Cron));

        // Users of employees: deactivation when nobody works any more (D39)
        services.AddSingleton<ISettingDefinitionProvider, HrSettingDefinitions>();
        services.AddScoped<UserDeactivationsJob>();
        services.AddSingleton(RecurringJobDefinition.For<UserDeactivationsJob>(UserDeactivationsJob.JobId, UserDeactivationsJob.Cron));

        // Self service: change requests approved by the approval engine (D39)
        services.AddScoped<ProfileChangeSubmitter>();
        services.AddSingleton<IApprovalDocumentTypeProvider, HrApprovalDocumentTypes>();
        services.AddScoped<IApprovalDocumentAccessPolicy, ProfileChangeAccessPolicy>();

        // Employee documents: reminders before they expire (D38)
        services.AddSingleton<INotificationTypeProvider, HrNotificationTypes>();
        services.AddScoped<DocumentRemindersJob>();
        services.AddSingleton(RecurringJobDefinition.For<DocumentRemindersJob>(DocumentRemindersJob.JobId, DocumentRemindersJob.Cron));

        // Approver resolvers and the escalation lookup of the approval engine (docs/domain/yetki-ve-onay.md §2.4)
        services.AddScoped<IApproverResolver, LineManagerApproverResolver>();
        services.AddScoped<IApproverResolver, OrgUnitManagerApproverResolver>();
        services.AddScoped<IApproverResolver, PositionApproverResolver>();
        services.AddScoped<IApproverManagerLookup, HrApproverManagerLookup>();

        // Excel imports of the organization and the employees, in the order they are loaded (PLAN H4, D40)
        services.AddScoped<IImportDefinition, JobImport>();
        services.AddScoped<IImportDefinition, OrgUnitImport>();
        services.AddScoped<IImportDefinition, PositionImport>();
        services.AddScoped<IImportDefinition, EmployeeImport>();
        return services;
    }
}
