using Merp.Application.Persistence.Files;
using Merp.Modules.HR.Domain.Documents;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Merp.Modules.HR.Application;

/// <summary>
/// HR data for handlers. Implemented by the module DbContext in Infrastructure; commands only add or change
/// entities, the unit of work saves them (PLAN 0.5).
/// </summary>
public interface IHrDbContext
{
    DbSet<OrgUnit> OrgUnits { get; }

    DbSet<Job> Jobs { get; }

    DbSet<Position> Positions { get; }

    /// <summary>Closure table of the position hierarchy, rebuilt per company (D37).</summary>
    DbSet<ReportingLine> ReportingLines { get; }

    /// <summary>People with their employments and job history (D38).</summary>
    DbSet<Employee> Employees { get; }

    /// <summary>Encrypted special category data, one row per employee (D13).</summary>
    DbSet<EmployeeSensitiveData> EmployeeSensitiveData { get; }

    DbSet<SgkExitCode> SgkExitCodes { get; }

    /// <summary>Kinds of employee documents of the tenant (D38).</summary>
    DbSet<DocumentType> DocumentTypes { get; }

    DbSet<EmployeeDocument> EmployeeDocuments { get; }

    /// <summary>Files of HR records, e.g. employee photos (D31).</summary>
    DbSet<StoredFile> StoredFiles { get; }

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;
}
