using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Candidates;
using Merp.Modules.Recruitment.Domain.Interviews;
using Merp.Modules.Recruitment.Domain.Offers;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.Modules.Recruitment.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Merp.Modules.Recruitment.Application;

/// <summary>
/// Recruitment data for handlers. Implemented by the module DbContext in Infrastructure; commands only add or change
/// entities, the unit of work saves them (PLAN 0.5).
/// </summary>
public interface IRecruitmentDbContext
{
    /// <summary>Hiring pipelines job openings copy (D47).</summary>
    DbSet<StageTemplate> StageTemplates { get; }

    /// <summary>Rejection and withdrawal reasons, candidate sources and tags (D47).</summary>
    DbSet<RecruitmentLookup> Lookups { get; }

    /// <summary>Published versions of the KVKK texts candidates see (D47).</summary>
    DbSet<ConsentText> ConsentTexts { get; }

    /// <summary>Requests to hire people (D48).</summary>
    DbSet<JobRequisition> JobRequisitions { get; }

    /// <summary>Job openings (D49).</summary>
    DbSet<JobOpening> JobOpenings { get; }

    DbSet<OpeningScreening> OpeningScreenings { get; }

    /// <summary>People who applied, one per e-mail address (D49).</summary>
    DbSet<Candidate> Candidates { get; }

    /// <summary>Applications of candidates to openings (D49).</summary>
    DbSet<JobApplication> JobApplications { get; }

    /// <summary>The AI analyses of the applications' CVs (D51).</summary>
    DbSet<ApplicationCvAnalysis> CvAnalyses { get; }

    /// <summary>Interviews of applications (D52).</summary>
    DbSet<Interview> Interviews { get; }

    /// <summary>The interviewers' evaluation forms (D52).</summary>
    DbSet<InterviewFeedback> InterviewFeedbacks { get; }

    /// <summary>Job offers of applications (D53).</summary>
    DbSet<JobOffer> JobOffers { get; }

    /// <summary>The tenant's evaluation templates (D52).</summary>
    DbSet<EvaluationTemplate> EvaluationTemplates { get; }

    /// <summary>Files of recruitment records, e.g. the CVs of candidates.</summary>
    DbSet<Merp.Application.Persistence.Files.StoredFile> StoredFiles { get; }

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;
}
