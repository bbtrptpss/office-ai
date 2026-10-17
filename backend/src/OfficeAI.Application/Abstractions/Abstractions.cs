using Microsoft.EntityFrameworkCore;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Audit;
using OfficeAI.Domain.Organization;
using OfficeAI.Domain.Work;
using OfficeAI.Domain.Workflows;

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
    DbSet<AiModel> AiModels { get; }
    DbSet<AgentConfiguration> AgentConfigurations { get; }
    DbSet<AgentToolPermission> AgentToolPermissions { get; }
    DbSet<AgentRun> AgentRuns { get; }
    DbSet<AgentRunStep> AgentRunSteps { get; }
    DbSet<ToolExecution> ToolExecutions { get; }
    DbSet<UsageRecord> UsageRecords { get; }
    DbSet<CostRecord> CostRecords { get; }
    DbSet<Deliverable> Deliverables { get; }
    DbSet<WorkflowDefinition> WorkflowDefinitions { get; }
    DbSet<WorkflowExecution> WorkflowExecutions { get; }
    DbSet<WorkflowEvent> WorkflowEvents { get; }
    DbSet<ApprovalRequest> ApprovalRequests { get; }
    DbSet<Review> Reviews { get; }

    /// <summary>Sets the concurrency token the client last saw, so a stale update fails with <see cref="Domain.Common.ConcurrencyException"/>.</summary>
    void ExpectVersion<T>(T entity, uint version) where T : Domain.Common.Entity;

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Drops every tracked entity, then re-tracks <paramref name="keep"/> as modified. Used by long-running work to avoid stale data.</summary>
    void ResetTracking(params object[] keep);

    /// <summary>Cluster-wide lock so only one workflow engine tick runs at a time (PostgreSQL advisory lock).</summary>
    Task<bool> TryAcquireEngineLockAsync(CancellationToken ct);
    Task ReleaseEngineLockAsync();
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
    Task AgentRunChangedAsync(Guid runId, Guid taskId, Guid projectId, string status, CancellationToken ct = default);
    Task ApprovalsChangedAsync(CancellationToken ct = default);
}

/// <summary>Who is acting outside an HTTP request, e.g. an AI employee inside a background run.</summary>
public sealed record ActingIdentity(Guid? UserId, string Name, bool IsAgent, string? CorrelationId) : ICurrentUser;

public static class ActingAs
{
    private static readonly AsyncLocal<ActingIdentity?> Current = new();

    public static ActingIdentity? Identity => Current.Value;

    /// <summary>Attribute everything done in this async flow (audit, CreatedBy) to the given identity.</summary>
    public static IDisposable Use(ActingIdentity identity)
    {
        var previous = Current.Value;
        Current.Value = identity;
        return new Restore(() => Current.Value = previous);
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }
}

/// <summary>Hands queued runs to the background executor.</summary>
public interface IAgentRunQueue
{
    ValueTask EnqueueAsync(Guid runId, CancellationToken ct = default);
    /// <summary>Signals a running execution to stop as soon as possible.</summary>
    void Cancel(Guid runId);
}
