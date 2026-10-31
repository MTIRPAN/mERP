using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Candidates;
using Merp.Modules.Recruitment.Domain.Interviews;
using Merp.Modules.Recruitment.Domain.Offers;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Recruitment.Infrastructure.Persistence;

/// <summary>
/// Schema <c>recruitment</c>: the settings of R1 (D47), the requisitions of R2 (D48) and the openings, candidates
/// and applications of R3 (D49), the pipeline and pool of R4 (D50, D51) and the interviews of R5 (D52); the rest comes with PLAN R6–R9.
/// </summary>
public sealed class RecruitmentDbContext(DbContextOptions<RecruitmentDbContext> options) : ModuleDbContext(options), IModuleSchema, IRecruitmentDbContext
{
    public static string SchemaName => "recruitment";

    public override string Schema => SchemaName;

    public DbSet<StageTemplate> StageTemplates => Set<StageTemplate>();

    public DbSet<RecruitmentLookup> Lookups => Set<RecruitmentLookup>();

    public DbSet<ConsentText> ConsentTexts => Set<ConsentText>();

    public DbSet<JobRequisition> JobRequisitions => Set<JobRequisition>();

    public DbSet<JobOpening> JobOpenings => Set<JobOpening>();

    public DbSet<OpeningScreening> OpeningScreenings => Set<OpeningScreening>();

    public DbSet<Candidate> Candidates => Set<Candidate>();

    public DbSet<JobApplication> JobApplications => Set<JobApplication>();

    public DbSet<ApplicationCvAnalysis> CvAnalyses => Set<ApplicationCvAnalysis>();

    public DbSet<Interview> Interviews => Set<Interview>();

    public DbSet<InterviewFeedback> InterviewFeedbacks => Set<InterviewFeedback>();

    public DbSet<JobOffer> JobOffers => Set<JobOffer>();

    public DbSet<EvaluationTemplate> EvaluationTemplates => Set<EvaluationTemplate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // The stages of a template and an opening, and an opening's team and form, are jsonb columns of their rows
        modelBuilder.Ignore<Stage>();
        modelBuilder.Ignore<OpeningTeam>();
        modelBuilder.Ignore<ApplicationForm>();
        modelBuilder.Ignore<OpeningQuestion>();
        modelBuilder.Ignore<ApplicationAnswer>();
        modelBuilder.Ignore<ApplicationConsent>();
        modelBuilder.Ignore<CandidateEmailText>();
        modelBuilder.Ignore<ScreeningItem>();
        modelBuilder.Ignore<CandidateProfile>();
        modelBuilder.Ignore<CandidateConsentChange>();
        modelBuilder.Ignore<EvaluationForm>();
        modelBuilder.Ignore<EvaluationSection>();
        modelBuilder.Ignore<EvaluationCriterion>();
        modelBuilder.Ignore<EvaluationQuestion>();
        modelBuilder.Ignore<CriterionScore>();
        modelBuilder.Ignore<QuestionAnswer>();
        base.OnModelCreating(modelBuilder);
    }
}
