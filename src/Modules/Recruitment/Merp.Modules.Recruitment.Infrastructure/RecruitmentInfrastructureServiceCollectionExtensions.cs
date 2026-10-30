using Merp.Application.Abstractions.Approvals;
using Merp.Application.Abstractions.Documents;
using Merp.Application.Abstractions.Jobs;
using Merp.Application.Abstractions.Notifications;
using Merp.Application.Abstractions.Numbering;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Settings;
using Merp.Infrastructure.Localization;
using Merp.Infrastructure.Messaging;
using Merp.Infrastructure.Persistence;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Application.Applications;
using Merp.Modules.Recruitment.Application.Candidates;
using Merp.Modules.Recruitment.Application.Careers;
using Merp.Modules.Recruitment.Application.CvAnalysis;
using Merp.Modules.Recruitment.Application.Interviews;
using Merp.Modules.Recruitment.Application.Offers;
using Merp.Modules.Recruitment.Application.Openings;
using Merp.Modules.Recruitment.Application.Requisitions;
using Merp.Modules.Recruitment.Application.Settings;
using Merp.Modules.Recruitment.Infrastructure.Applications;
using Merp.Modules.Recruitment.Infrastructure.Candidates;
using Merp.Modules.Recruitment.Infrastructure.Interviews;
using Merp.Modules.Recruitment.Infrastructure.Offers;
using Merp.Modules.Recruitment.Infrastructure.Persistence;
using Merp.Modules.Recruitment.Infrastructure.Privacy;
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

        // Career page and applications: anonymous and from self service (D49)
        services.AddScoped<ConsentTextResolver>();
        services.AddScoped<CareerCatalog>();
        services.AddScoped<ApplicationIntake>();
        services.AddScoped<SelfApplicant>();

        // Pipeline: who reaches the applications, their rows and the rejection e-mails, now or later (D50)
        services.AddScoped<ApplicationAccess>();
        services.AddScoped<ApplicationRows>();
        services.AddScoped<RejectionEmails>();
        services.AddSingleton(RecurringJobDefinition.For<RejectionEmailsJob>(RejectionEmailsJob.JobId, RejectionEmailsJob.Cron));

        // Candidate pool: the form's checks, the full text search, mentions in notes and the CV text job (D50)
        services.AddScoped<CandidateRules>();
        services.AddScoped<ICandidateSearch, CandidateSearch>();
        services.AddSingleton<INotificationTypeProvider, CandidateNotificationTypes>();
        services.AddSingleton(RecurringJobDefinition.For<CandidateCvTextJob>(CandidateCvTextJob.JobId, CandidateCvTextJob.Cron));

        // CV analysis of applications in the background and on request (A3, D51)
        services.AddScoped<ApplicationCvAnalyzer>();
        services.AddSingleton(RecurringJobDefinition.For<ApplicationCvAnalysisJob>(ApplicationCvAnalysisJob.JobId, ApplicationCvAnalysisJob.Cron));

        // Interviews: who reaches them, their rows, invitations with the calendar file and reminders (D52)
        services.AddScoped<InterviewAccess>();
        services.AddScoped<InterviewRows>();
        services.AddScoped<InterviewInvitations>();
        services.AddSingleton<IInterviewCalendar, IcalInterviewCalendar>();
        services.AddSingleton<INotificationTypeProvider, InterviewNotificationTypes>();
        services.AddSingleton(RecurringJobDefinition.For<InterviewRemindersJob>(InterviewRemindersJob.JobId, InterviewRemindersJob.Cron));

        // Offers: who reaches them, the approval flow, the letter, the notifications and the expiry job (D53)
        services.AddScoped<OfferAccess>();
        services.AddScoped<OfferRows>();
        services.AddScoped<OfferLetters>();
        services.AddSingleton<IApprovalDocumentTypeProvider, OfferApprovalDocumentTypes>();
        services.AddSingleton<INotificationTypeProvider, OfferNotificationTypes>();
        services.AddSingleton<IDocumentTypeProvider, OfferLetterType>();
        services.AddScoped<IApprovalDocumentAccessPolicy, OfferAccessPolicy>();
        services.AddSingleton(RecurringJobDefinition.For<OfferExpiryJob>(OfferExpiryJob.JobId, OfferExpiryJob.Cron));

        // KVKK: the nightly retention with the consent renewal e-mails (D55)
        services.AddSingleton(RecurringJobDefinition.For<CandidateRetentionJob>(CandidateRetentionJob.JobId, CandidateRetentionJob.Cron));
        return services;
    }
}
