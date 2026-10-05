using Merp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Recruitment.Infrastructure.Persistence;

/// <summary>Schema <c>recruitment</c>. Business tables come with PLAN R1–R9; for now only the shared infrastructure tables.</summary>
public sealed class RecruitmentDbContext(DbContextOptions<RecruitmentDbContext> options) : ModuleDbContext(options), IModuleSchema
{
    public static string SchemaName => "recruitment";

    public override string Schema => SchemaName;
}
