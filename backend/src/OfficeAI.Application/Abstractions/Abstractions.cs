using Microsoft.EntityFrameworkCore;
using OfficeAI.Domain.Audit;
using OfficeAI.Domain.Organization;
using OfficeAI.Domain.Work;

namespace OfficeAI.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<Department> Departments { get; }
    DbSet<EmployeeRoleDefinition> RoleDefinitions { get; }
    DbSet<EmployeeProfile> Employees { get; }
    DbSet<Project> Projects { get; }
    DbSet<Milestone> Milestones { get; }
    DbSet<WorkTask> Tasks { get; }
    DbSet<TaskDependency> TaskDependencies { get; }
    DbSet<TaskAssignment> TaskAssignments { get; }
    DbSet<AuditLog> AuditLogs { get; }

    /// <summary>Sets the concurrency token the client last saw, so a stale update fails with <see cref="Domain.Common.ConcurrencyException"/>.</summary>
    void ExpectVersion<T>(T entity, uint version) where T : Domain.Common.Entity;

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Name { get; }

    /// <summary>True when the caller is an AI employee's service account.</summary>
    bool IsAgent { get; }
    string? CorrelationId { get; }
}

/// <summary>Pushes change notifications to connected clients (SignalR in the API).</summary>
public interface IRealtimeNotifier
{
    Task TaskChangedAsync(Guid projectId, Guid taskId, CancellationToken ct = default);
    Task OrganizationChangedAsync(CancellationToken ct = default);
}
