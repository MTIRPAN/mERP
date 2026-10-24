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

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;
}
