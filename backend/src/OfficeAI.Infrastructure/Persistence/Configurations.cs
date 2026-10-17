using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OfficeAI.Domain.Audit;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Organization;
using OfficeAI.Domain.Work;
using OfficeAI.Infrastructure.Identity;

namespace OfficeAI.Infrastructure.Persistence;

internal static class EntityConfig
{
    public static void Base<T>(EntityTypeBuilder<T> b) where T : Entity
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Version).IsRowVersion(); // maps to PostgreSQL xmin
    }
}

internal class DepartmentConfig : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> b)
    {
        EntityConfig.Base(b);
        b.ToTable("Departments");
        b.Property(d => d.Code).HasMaxLength(50);
        b.Property(d => d.Name).HasMaxLength(200);
        b.HasIndex(d => d.Code).IsUnique();
        b.HasOne(d => d.Parent).WithMany().HasForeignKey(d => d.ParentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class RoleDefinitionConfig : IEntityTypeConfiguration<EmployeeRoleDefinition>
{
    public void Configure(EntityTypeBuilder<EmployeeRoleDefinition> b)
    {
        EntityConfig.Base(b);
        b.ToTable("EmployeeRoleDefinitions");
        b.Property(r => r.Code).HasMaxLength(80);
        b.Property(r => r.Title).HasMaxLength(200);
        b.Property(r => r.Level).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(r => r.Code).IsUnique();
        b.HasOne(r => r.Department).WithMany().HasForeignKey(r => r.DepartmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class EmployeeConfig : IEntityTypeConfiguration<EmployeeProfile>
{
    public void Configure(EntityTypeBuilder<EmployeeProfile> b)
    {
        EntityConfig.Base(b);
        b.ToTable("EmployeeProfiles", t =>
            t.HasCheckConstraint("CK_EmployeeProfiles_HumanHasUser", "\"Kind\" <> 'Human' OR \"UserId\" IS NOT NULL"));
        b.Property(e => e.DisplayName).HasMaxLength(200);
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(10);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(30);
        b.HasIndex(e => e.UserId).IsUnique();
        b.HasIndex(e => e.DepartmentId);
        b.HasIndex(e => e.ManagerId);
        b.HasOne(e => e.Department).WithMany().HasForeignKey(e => e.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(e => e.RoleDefinition).WithMany().HasForeignKey(e => e.RoleDefinitionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(e => e.Manager).WithMany().HasForeignKey(e => e.ManagerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithOne().HasForeignKey<EmployeeProfile>(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class ProjectConfig : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> b)
    {
        EntityConfig.Base(b);
        b.ToTable("Projects", t => t.HasCheckConstraint("CK_Projects_Budget", "\"EstimatedBudget\" IS NULL OR \"EstimatedBudget\" >= 0"));
        b.Property(p => p.Code).HasMaxLength(50);
        b.Property(p => p.Name).HasMaxLength(200);
        b.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(p => p.EstimatedBudget).HasPrecision(18, 4);
        b.HasIndex(p => p.Code).IsUnique();
        b.HasOne<EmployeeProfile>().WithMany().HasForeignKey(p => p.OwnerEmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(p => p.Milestones).WithOne().HasForeignKey(m => m.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class MilestoneConfig : IEntityTypeConfiguration<Milestone>
{
    public void Configure(EntityTypeBuilder<Milestone> b)
    {
        EntityConfig.Base(b);
        b.ToTable("Milestones");
        b.Property(m => m.Name).HasMaxLength(200);
        b.HasIndex(m => m.ProjectId);
    }
}

internal class WorkTaskConfig : IEntityTypeConfiguration<WorkTask>
{
    public void Configure(EntityTypeBuilder<WorkTask> b)
    {
        EntityConfig.Base(b);
        b.ToTable("Tasks");
        b.Property(t => t.Title).HasMaxLength(300);
        b.Property(t => t.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(t => t.Priority).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(t => new { t.ProjectId, t.Status });
        b.HasOne(t => t.Project).WithMany().HasForeignKey(t => t.ProjectId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Milestone>().WithMany().HasForeignKey(t => t.MilestoneId).OnDelete(DeleteBehavior.SetNull);
        b.HasMany(t => t.Assignments).WithOne().HasForeignKey(a => a.TaskId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class TaskDependencyConfig : IEntityTypeConfiguration<TaskDependency>
{
    public void Configure(EntityTypeBuilder<TaskDependency> b)
    {
        b.ToTable("TaskDependencies", t => t.HasCheckConstraint("CK_TaskDependencies_NotSelf", "\"TaskId\" <> \"DependsOnTaskId\""));
        b.HasKey(d => new { d.TaskId, d.DependsOnTaskId });
        b.HasOne<WorkTask>().WithMany().HasForeignKey(d => d.TaskId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<WorkTask>().WithMany().HasForeignKey(d => d.DependsOnTaskId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(d => d.DependsOnTaskId);
    }
}

internal class TaskAssignmentConfig : IEntityTypeConfiguration<TaskAssignment>
{
    public void Configure(EntityTypeBuilder<TaskAssignment> b)
    {
        EntityConfig.Base(b);
        b.ToTable("TaskAssignments");
        b.Ignore(a => a.IsActive);
        b.HasIndex(a => new { a.EmployeeId, a.UnassignedAt });
        // At most one active assignment per (task, employee).
        b.HasIndex(a => new { a.TaskId, a.EmployeeId }).IsUnique().HasFilter("\"UnassignedAt\" IS NULL");
        b.HasOne<EmployeeProfile>().WithMany().HasForeignKey(a => a.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class AuditLogConfig : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("AuditLogs");
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).UseIdentityAlwaysColumn();
        b.Property(a => a.Action).HasMaxLength(50);
        b.Property(a => a.EntityType).HasMaxLength(100);
        b.Property(a => a.EntityId).HasMaxLength(200);
        b.Property(a => a.ActorName).HasMaxLength(200);
        b.Property(a => a.CorrelationId).HasMaxLength(100);
        b.Property(a => a.OldValues).HasColumnType("jsonb");
        b.Property(a => a.NewValues).HasColumnType("jsonb");
        b.HasIndex(a => new { a.EntityType, a.EntityId, a.OccurredAt });
        b.HasIndex(a => a.OccurredAt);
    }
}

internal class PermissionConfig : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> b)
    {
        b.ToTable("Permissions");
        b.Property(p => p.Code).HasMaxLength(100);
        b.HasIndex(p => p.Code).IsUnique();
    }
}

internal class RolePermissionConfig : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> b)
    {
        b.ToTable("RolePermissions");
        b.HasKey(rp => new { rp.RoleId, rp.PermissionId });
        b.HasOne<AppRole>().WithMany().HasForeignKey(rp => rp.RoleId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Permission>().WithMany().HasForeignKey(rp => rp.PermissionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class AppUserConfig : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.ToTable("Users");
        b.Property(u => u.DisplayName).HasMaxLength(200);
    }
}

internal class AppRoleConfig : IEntityTypeConfiguration<AppRole>
{
    public void Configure(EntityTypeBuilder<AppRole> b) => b.ToTable("Roles");
}
