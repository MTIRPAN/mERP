using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Recruitment.Infrastructure.Persistence;

/// <summary>Schema <c>recruitment</c>: the settings of R1 (D47); applications and the rest come with PLAN R2–R9.</summary>
public sealed class RecruitmentDbContext(DbContextOptions<RecruitmentDbContext> options) : ModuleDbContext(options), IModuleSchema, IRecruitmentDbContext
{
    public static string SchemaName => "recruitment";

    public override string Schema => SchemaName;

    public DbSet<StageTemplate> StageTemplates => Set<StageTemplate>();

    public DbSet<RecruitmentLookup> Lookups => Set<RecruitmentLookup>();

    public DbSet<ConsentText> ConsentTexts => Set<ConsentText>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // The stages of a template are a jsonb column of its row (D47)
        modelBuilder.Ignore<Stage>();
        base.OnModelCreating(modelBuilder);
    }
}
