using Merp.Modules.HR.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merp.Modules.HR.Infrastructure.Persistence.Configurations;

internal sealed class OrgUnitConfiguration : IEntityTypeConfiguration<OrgUnit>
{
    public void Configure(EntityTypeBuilder<OrgUnit> builder)
    {
        builder.ToTable("org_units");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.Code).HasMaxLength(OrgUnit.CodeMaxLength);
        builder.Property(u => u.Name).HasMaxLength(OrgUnit.NameMaxLength);
        builder.Property(u => u.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(u => u.CostCenterCode).HasMaxLength(OrgUnit.CostCenterCodeMaxLength);
        builder.Property(u => u.Path).HasColumnType("ltree");

        builder.HasOne<OrgUnit>().WithMany().HasForeignKey(u => u.ParentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Position>().WithMany().HasForeignKey(u => u.ManagerPositionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(u => new { u.CompanyId, u.Code }).IsUnique();

        // Subtree queries: path <@ ancestor (D37)
        builder.HasIndex(u => u.Path).HasMethod("gist");
    }
}

internal sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.ToTable("jobs");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).ValueGeneratedNever();
        builder.Property(j => j.Code).HasMaxLength(Job.CodeMaxLength);
        builder.Property(j => j.Name).HasMaxLength(Job.NameMaxLength);
        builder.Property(j => j.Family).HasMaxLength(Job.FamilyMaxLength);
        builder.Property(j => j.Level).HasMaxLength(Job.LevelMaxLength);
        builder.HasIndex(j => j.Code).IsUnique();
    }
}

internal sealed class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public void Configure(EntityTypeBuilder<Position> builder)
    {
        builder.ToTable("positions");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Code).HasMaxLength(Position.CodeMaxLength);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<OrgUnit>().WithMany().HasForeignKey(p => p.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Job>().WithMany().HasForeignKey(p => p.JobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Position>().WithMany().HasForeignKey(p => p.ReportsToPositionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => new { p.CompanyId, p.Code }).IsUnique();
    }
}

internal sealed class ReportingLineConfiguration : IEntityTypeConfiguration<ReportingLine>
{
    public void Configure(EntityTypeBuilder<ReportingLine> builder)
    {
        builder.ToTable("reporting_lines");
        builder.HasKey(r => new { r.ManagerPositionId, r.SubordinatePositionId });

        // No foreign keys: the rows are derived and are rebuilt by a domain event handler, which runs before the unit
        // of work inserts a new position (D37). Positions are never deleted.

        // A position's managers, nearest first; the rebuild replaces a company's rows
        builder.HasIndex(r => new { r.SubordinatePositionId, r.Depth });
        builder.HasIndex(r => r.CompanyId);
    }
}
