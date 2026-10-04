using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Domain;

// DELIBERATE VIOLATION of CLAUDE.md rule 4 (PLAN 0.10 acceptance): the domain must not use EF Core.
// This commit exists to show the architecture tests failing and is reverted by the next commit.
public sealed class ArchitectureViolationExample(DbContext context)
{
    public DbContext Context => context;
}
