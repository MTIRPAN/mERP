using Merp.Application.Abstractions.Jobs;
using Merp.Application.Abstractions.Security;
using Merp.Infrastructure.Localization;
using Merp.Infrastructure.Messaging;
using Merp.Infrastructure.Persistence;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.Approvals;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Contracts.Organization;
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
        services.AddScoped<IPositionHolders, NoPositionHolders>();
        services.AddScoped<IOrgStructureStore, OrgStructureStore>();
        services.AddScoped<IReportingLines, ReportingLineReader>();
        services.AddScoped<ReportingLinesBuilder>();
        services.AddScoped<IOrgQuery, OrgQuery>();
        services.AddScoped<ReportingLinesJob>();
        services.AddSingleton(RecurringJobDefinition.For<ReportingLinesJob>(ReportingLinesJob.JobId, ReportingLinesJob.Cron));

        // Approver resolvers and the escalation lookup of the approval engine (docs/domain/yetki-ve-onay.md §2.4)
        services.AddScoped<IApproverResolver, LineManagerApproverResolver>();
        services.AddScoped<IApproverResolver, OrgUnitManagerApproverResolver>();
        services.AddScoped<IApproverResolver, PositionApproverResolver>();
        services.AddScoped<IApproverManagerLookup, HrApproverManagerLookup>();
        return services;
    }
}
