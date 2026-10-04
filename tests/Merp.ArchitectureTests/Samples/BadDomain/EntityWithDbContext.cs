using Microsoft.EntityFrameworkCore;

namespace Merp.ArchitectureTests.Samples.BadDomain;

/// <summary>Deliberate violation: a "domain" type that depends on EF Core (rule 4). See RuleSelfTests.</summary>
public sealed class EntityWithDbContext(DbContext context)
{
    public DbContext Context => context;
}
