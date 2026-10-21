using Merp.Infrastructure.Persistence.Conventions;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Organization;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merp.Modules.HR.Infrastructure.Persistence.Configurations;

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("employees");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Tckn).HasMaxLength(Tckn.Length);
        builder.Property(e => e.FirstName).HasMaxLength(Employee.NameMaxLength);
        builder.Property(e => e.LastName).HasMaxLength(Employee.NameMaxLength);
        builder.Property(e => e.BirthPlace).HasMaxLength(Employee.PlaceMaxLength);
        builder.Property(e => e.Gender).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Nationality).HasMaxLength(2);
        builder.Property(e => e.MaritalStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.MilitaryStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Iban).HasMaxLength(34);
        builder.Property(e => e.UserLinkState).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.UserLinkError).HasMaxLength(100);
        builder.Property(e => e.Emails).HasJson();
        builder.Property(e => e.Phones).HasJson();
        builder.Property(e => e.Addresses).HasJson();
        builder.Property(e => e.EmergencyContacts).HasJson();
        builder.Property(e => e.FamilyMembers).HasJson();
        builder.Property(e => e.Educations).HasJson();
        builder.Property(e => e.Certificates).HasJson();
        builder.Property(e => e.Languages).HasJson();
        builder.Ignore(e => e.FullName);

        builder.HasMany(e => e.Employments).WithOne().HasForeignKey(m => m.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(e => e.Employments).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(e => e.Tckn).IsUnique();
        builder.HasIndex(e => e.UserId).IsUnique().HasFilter("user_id IS NOT NULL");
        builder.HasIndex(e => new { e.LastName, e.FirstName });
    }
}

internal sealed class EmploymentConfiguration : IEntityTypeConfiguration<Employment>
{
    public void Configure(EntityTypeBuilder<Employment> builder)
    {
        builder.ToTable("employments");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.RegistryNumber).HasMaxLength(Employment.RegistryNumberMaxLength);
        builder.Property(m => m.SeniorityReason).HasMaxLength(Employment.TextMaxLength);
        builder.Property(m => m.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.WeeklyHours).HasPrecision(5, 2);
        builder.Property(m => m.SgkExitCode).HasMaxLength(Employment.SgkExitCodeMaxLength);
        builder.Property(m => m.TerminationReason).HasMaxLength(Employment.TextMaxLength);
        builder.Property(m => m.TerminationNote).HasMaxLength(Employment.TextMaxLength);
        builder.Ignore(m => m.IsOpen);
        builder.Ignore(m => m.LatestAssignment);

        builder.HasMany(m => m.Assignments).WithOne().HasForeignKey(a => a.EmploymentId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(m => m.Assignments).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(m => new { m.CompanyId, m.RegistryNumber }).IsUnique();

        // One open employment per person and company; the domain reports it first
        builder.HasIndex(m => new { m.EmployeeId, m.CompanyId })
            .IsUnique()
            .HasFilter("termination_date IS NULL")
            .HasDatabaseName("ix_employments_employee_id_company_id_open");
    }
}

internal sealed class JobAssignmentConfiguration : IEntityTypeConfiguration<JobAssignment>
{
    public void Configure(EntityTypeBuilder<JobAssignment> builder)
    {
        builder.ToTable("job_assignments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Level).HasMaxLength(JobAssignment.LevelMaxLength);
        builder.Property(a => a.CostCenterCode).HasMaxLength(JobAssignment.CostCenterCodeMaxLength);
        builder.Property(a => a.Reason).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<Position>().WithMany().HasForeignKey(a => a.PositionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Position>().WithMany().HasForeignKey(a => a.ManagerPositionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Job>().WithMany().HasForeignKey(a => a.JobId).OnDelete(DeleteBehavior.Restrict);

        // Holders of a position on a day; changes not yet announced
        builder.HasIndex(a => new { a.PositionId, a.ValidFrom });
        builder.HasIndex(a => a.ValidFrom).HasFilter("NOT announced").HasDatabaseName("ix_job_assignments_unannounced");
    }
}

internal sealed class EmployeeSensitiveDataConfiguration : IEntityTypeConfiguration<EmployeeSensitiveData>
{
    public void Configure(EntityTypeBuilder<EmployeeSensitiveData> builder)
    {
        builder.ToTable("employee_sensitive_data");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("employee_id").ValueGeneratedNever();
        builder.HasOne<Employee>().WithOne().HasForeignKey<EmployeeSensitiveData>(s => s.Id).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SgkExitCodeConfiguration : IEntityTypeConfiguration<SgkExitCode>
{
    public void Configure(EntityTypeBuilder<SgkExitCode> builder)
    {
        builder.ToTable("sgk_exit_codes");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Code).HasMaxLength(SgkExitCode.CodeMaxLength);
        builder.HasIndex(c => c.Code).IsUnique();
    }
}
