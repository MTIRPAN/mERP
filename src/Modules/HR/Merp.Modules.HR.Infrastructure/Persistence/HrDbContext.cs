using Merp.Infrastructure.Persistence;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Domain.Documents;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Leave;
using Merp.Modules.HR.Domain.Organization;
using Merp.Modules.HR.Domain.SelfService;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Infrastructure.Persistence;

public sealed class HrDbContext(DbContextOptions<HrDbContext> options) : ModuleDbContext(options), IModuleSchema, IHrDbContext
{
    public static string SchemaName => "hr";

    public override string Schema => SchemaName;

    public DbSet<OrgUnit> OrgUnits => Set<OrgUnit>();

    public DbSet<Job> Jobs => Set<Job>();

    public DbSet<Position> Positions => Set<Position>();

    public DbSet<ReportingLine> ReportingLines => Set<ReportingLine>();

    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<EmployeeSensitiveData> EmployeeSensitiveData => Set<EmployeeSensitiveData>();

    public DbSet<SgkExitCode> SgkExitCodes => Set<SgkExitCode>();

    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();

    public DbSet<EmployeeDocument> EmployeeDocuments => Set<EmployeeDocument>();

    public DbSet<ProfileChangeRequest> ProfileChangeRequests => Set<ProfileChangeRequest>();

    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();

    public DbSet<LeavePolicy> LeavePolicies => Set<LeavePolicy>();

    public DbSet<LeavePolicyRuleSet> LeavePolicyRuleSets => Set<LeavePolicyRuleSet>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        // Unit paths are ltree: OrgPaths.IsWithin in LINQ becomes the indexed <@ operator (D37)
        optionsBuilder.AddInterceptors(OrgPathQueryInterceptor.Instance);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasPostgresExtension("ltree");

        // Lists of the employee card are jsonb columns of their row, not entities
        modelBuilder.Ignore<EmployeeEmail>();
        modelBuilder.Ignore<EmployeePhone>();
        modelBuilder.Ignore<EmployeeAddress>();
        modelBuilder.Ignore<EmergencyContact>();
        modelBuilder.Ignore<FamilyMember>();
        modelBuilder.Ignore<Education>();
        modelBuilder.Ignore<Certificate>();
        modelBuilder.Ignore<LanguageSkill>();

        // Versions and rules of leave types and policies are jsonb columns too (D41)
        modelBuilder.Ignore<LeaveTypeVersion>();
        modelBuilder.Ignore<LeavePolicyVersion>();
        modelBuilder.Ignore<AnnualLeaveRules>();
        modelBuilder.Ignore<EntitlementTier>();
        modelBuilder.Ignore<AgeMinimum>();
        modelBuilder.Ignore<ExtraDayRule>();
        modelBuilder.Ignore<PolicyCriterion>();
        modelBuilder.Ignore<PolicyAssignmentRule>();
        base.OnModelCreating(modelBuilder);
    }
}
