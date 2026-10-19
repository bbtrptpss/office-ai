using Microsoft.EntityFrameworkCore;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Audit;
using OfficeAI.Domain.Code;
using OfficeAI.Domain.Communication;
using OfficeAI.Domain.Finance;
using OfficeAI.Domain.Integrations;
using OfficeAI.Domain.Knowledge;
using OfficeAI.Domain.Delivery;
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
    DbSet<AiProviderCredential> AiProviderCredentials { get; }
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
    DbSet<ProjectRequirement> Requirements { get; }
    DbSet<ArchitectureDecision> ArchitectureDecisions { get; }
    DbSet<Defect> Defects { get; }
    DbSet<Release> Releases { get; }
    DbSet<Lead> Leads { get; }
    DbSet<LeadQuestion> LeadQuestions { get; }
    DbSet<RoleRate> RoleRates { get; }
    DbSet<CompanySettings> CompanySettings { get; }
    DbSet<ProjectEstimate> Estimates { get; }
    DbSet<CodeRepository> CodeRepositories { get; }
    DbSet<TestRun> TestRuns { get; }
    DbSet<MergeRequest> MergeRequests { get; }
    DbSet<BudgetAllocation> BudgetAllocations { get; }
    DbSet<KnowledgeEntry> KnowledgeEntries { get; }
    DbSet<ProjectDocument> Documents { get; }
    DbSet<IntegrationConfiguration> Integrations { get; }
    DbSet<IntegrationDelivery> IntegrationDeliveries { get; }
    DbSet<ChatChannel> ChatChannels { get; }
    DbSet<ChatMessage> ChatMessages { get; }
    DbSet<ChannelReadState> ChannelReadStates { get; }
    DbSet<Announcement> Announcements { get; }
    DbSet<AnnouncementRead> AnnouncementReads { get; }
    DbSet<Meeting> Meetings { get; }
    DbSet<MeetingAgendaItem> MeetingAgendaItems { get; }
    DbSet<MeetingParticipant> MeetingParticipants { get; }
    DbSet<MeetingActionItem> MeetingActionItems { get; }
    DbSet<ProjectMember> ProjectMembers { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<ChatReply> ChatReplies { get; }
    DbSet<ChatCommand> ChatCommands { get; }

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
    /// <summary>Carries ids only; clients re-read through the API, which checks permissions.</summary>
    Task ChatMessageAsync(Guid channelId, Guid messageId, CancellationToken ct = default);
    Task AnnouncementsChangedAsync(CancellationToken ct = default);
    Task MeetingChangedAsync(Guid meetingId, CancellationToken ct = default);
    /// <summary>An AI employee started (or stopped) writing an answer in a channel.</summary>
    Task ChatAiTypingAsync(Guid channelId, string employeeName, bool active, CancellationToken ct = default);
    /// <summary>Tells only these users that their notifications changed.</summary>
    Task NotificationsChangedAsync(IEnumerable<Guid> userIds, CancellationToken ct = default);
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

/// <summary>Hands AI chat replies to the background worker.</summary>
public interface IChatReplyQueue
{
    ValueTask EnqueueAsync(Guid replyId, CancellationToken ct = default);
}

/// <summary>Hands queued runs to the background executor.</summary>
public interface IAgentRunQueue
{
    ValueTask EnqueueAsync(Guid runId, CancellationToken ct = default);
    /// <summary>Signals a running execution to stop as soon as possible.</summary>
    void Cancel(Guid runId);
}

/// <summary>
/// Which projects the current request may see. Unrestricted by default: background work (AI runs, the workflow engine) and
/// people with project.all see everything. The API restricts it once per request for everyone else, and the database
/// applies it to every project-owned table, so hidden projects look like they don't exist.
/// </summary>
public sealed class ProjectScope
{
    public bool Restricted { get; private set; }
    public List<Guid> VisibleProjectIds { get; private set; } = [];

    public void Restrict(IEnumerable<Guid> projectIds)
    {
        VisibleProjectIds = projectIds.Distinct().ToList();
        Restricted = true;
    }

    public bool CanSee(Guid projectId) => !Restricted || VisibleProjectIds.Contains(projectId);
}
