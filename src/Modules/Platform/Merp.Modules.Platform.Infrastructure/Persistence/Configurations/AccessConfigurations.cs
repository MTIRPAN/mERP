using Merp.Modules.Platform.Domain.Roles;
using Merp.Modules.Platform.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merp.Modules.Platform.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.Subject).HasMaxLength(User.SubjectMaxLength);
        builder.Property(u => u.UserName).HasMaxLength(User.UserNameMaxLength);
        builder.Property(u => u.Email).HasMaxLength(User.UserNameMaxLength);
        builder.Property(u => u.FirstName).HasMaxLength(User.NameMaxLength);
        builder.Property(u => u.LastName).HasMaxLength(User.NameMaxLength);
        builder.Property(u => u.Culture).HasMaxLength(20);
        builder.Ignore(u => u.DisplayName);
        builder.HasMany(u => u.Assignments).WithOne().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(u => u.Assignments).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(u => u.Subject).IsUnique();
        builder.HasIndex(u => u.UserName).IsUnique();
        builder.HasIndex(u => u.Email);
        builder.HasIndex(u => u.EmployeeId).IsUnique().HasFilter("employee_id IS NOT NULL");
    }
}

internal sealed class RoleAssignmentConfiguration : IEntityTypeConfiguration<RoleAssignment>
{
    public void Configure(EntityTypeBuilder<RoleAssignment> builder)
    {
        builder.ToTable("role_assignments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.ScopeType).HasMaxLength(AssignmentScope.TypeMaxLength);
        builder.Ignore(a => a.Scope);
        builder.HasOne<Role>().WithMany().HasForeignKey(a => a.RoleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => new { a.RoleId, a.ScopeType });
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Code).HasMaxLength(Role.CodeMaxLength);
        builder.Property(r => r.Description).HasMaxLength(Role.DescriptionMaxLength);
        builder.HasMany(r => r.Permissions).WithOne().HasForeignKey(p => p.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Deleted roles keep their row (D9); their code can be reused
        builder.HasIndex(r => r.Code).IsUnique().HasFilter("is_deleted = false");
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions");
        builder.HasKey(p => new { p.RoleId, p.PermissionCode });
        builder.Property(p => p.PermissionCode).HasMaxLength(Permission.CodeMaxLength);

        // Removing a permission from the catalog withdraws it from every role
        builder.HasOne<Permission>().WithMany().HasForeignKey(p => p.PermissionCode).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions");
        builder.HasKey(p => p.Code);
        builder.Property(p => p.Code).HasMaxLength(Permission.CodeMaxLength);
        builder.Property(p => p.Module).HasMaxLength(50);
    }
}
