using Merp.Modules.HR.Domain.Organization;
using Merp.Modules.HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.UnitTests.Persistence;

/// <summary>The SQL of subtree queries, generated without a database.</summary>
public class OrgPathTranslationTests
{
    [Fact]
    public void IsWithin_AgainstAnotherUnit_UsesTheLtreeOperator()
    {
        using var db = new HrDbContextDesignTimeFactory().CreateDbContext([]);
        var root = OrgUnitId.From(Guid.CreateVersion7());

        var sql = db.OrgUnits.Where(u => db.OrgUnits.Any(r => r.Id == root && OrgPaths.IsWithin(u.Path, r.Path))).ToQueryString();

        sql.ShouldContain("o.path::ltree <@ o0.path::ltree");
    }

    [Fact]
    public void IsWithin_AgainstAPath_UsesTheLtreeOperator()
    {
        using var db = new HrDbContextDesignTimeFactory().CreateDbContext([]);
        var path = OrgPaths.Label(OrgUnitId.From(Guid.CreateVersion7()));

        var sql = db.OrgUnits.Where(u => OrgPaths.IsWithin(u.Path, path)).ToQueryString();

        sql.ShouldContain("o.path::ltree <@ @path::ltree");
    }
}
