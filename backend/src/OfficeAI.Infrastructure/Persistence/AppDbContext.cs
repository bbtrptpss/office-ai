using System.Text.Json;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using OfficeAI.Application.Abstractions;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Audit;
using OfficeAI.Domain.Code;
using OfficeAI.Domain.Communication;
using OfficeAI.Domain.Finance;
using OfficeAI.Domain.Integrations;
using OfficeAI.Domain.Knowledge;
using OfficeAI.Domain.Delivery;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Organization;
using OfficeAI.Domain.Work;
using OfficeAI.Domain.Workflows;
using OfficeAI.Infrastructure.Identity;

namespace OfficeAI.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser, TimeProvider clock, ProjectScope? projectScope = null)
    : IdentityDbContext<AppUser, AppRole, Guid>(options), IAppDbContext, IDataProtectionKeyContext
{
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<EmployeeRoleDefinition> RoleDefinitions => Set<EmployeeRoleDefinition>();
    public DbSet<EmployeeProfile> Employees => Set<EmployeeProfile>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Milestone> Milestones => Set<Milestone>();
    public DbSet<WorkTask> Tasks => Set<WorkTask>();
    public DbSet<TaskDependency> TaskDependencies => Set<TaskDependency>();
    public DbSet<TaskAssignment> TaskAssignments => Set<TaskAssignment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AiModel> AiModels => Set<AiModel>();
    public DbSet<AiProviderCredential> AiProviderCredentials => Set<AiProviderCredential>();
    public DbSet<AgentConfiguration> AgentConfigurations => Set<AgentConfiguration>();
    public DbSet<AgentToolPermission> AgentToolPermissions => Set<AgentToolPermission>();
    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();
    public DbSet<AgentRunStep> AgentRunSteps => Set<AgentRunStep>();
    public DbSet<ToolExecution> ToolExecutions => Set<ToolExecution>();
    public DbSet<UsageRecord> UsageRecords => Set<UsageRecord>();
    public DbSet<CostRecord> CostRecords => Set<CostRecord>();
    public DbSet<Deliverable> Deliverables => Set<Deliverable>();
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowExecution> WorkflowExecutions => Set<WorkflowExecution>();
    public DbSet<WorkflowEvent> WorkflowEvents => Set<WorkflowEvent>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<ProjectRequirement> Requirements => Set<ProjectRequirement>();
    public DbSet<ArchitectureDecision> ArchitectureDecisions => Set<ArchitectureDecision>();
    public DbSet<Defect> Defects => Set<Defect>();
    public DbSet<Release> Releases => Set<Release>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<DeliveryPackage> DeliveryPackages => Set<DeliveryPackage>();
    public DbSet<LeadQuestion> LeadQuestions => Set<LeadQuestion>();
    public DbSet<RoleRate> RoleRates => Set<RoleRate>();
    public DbSet<CompanySettings> CompanySettings => Set<CompanySettings>();
    public DbSet<ProjectEstimate> Estimates => Set<ProjectEstimate>();
    public DbSet<CodeRepository> CodeRepositories => Set<CodeRepository>();
    public DbSet<TestRun> TestRuns => Set<TestRun>();
    public DbSet<MergeRequest> MergeRequests => Set<MergeRequest>();
    public DbSet<BudgetAllocation> BudgetAllocations => Set<BudgetAllocation>();
    public DbSet<OperatingCost> OperatingCosts => Set<OperatingCost>();
    public DbSet<KnowledgeEntry> KnowledgeEntries => Set<KnowledgeEntry>();
    public DbSet<KnowledgeEmbedding> KnowledgeEmbeddings => Set<KnowledgeEmbedding>();
    public DbSet<ProjectDocument> Documents => Set<ProjectDocument>();
    public DbSet<IntegrationConfiguration> Integrations => Set<IntegrationConfiguration>();
    public DbSet<IntegrationDelivery> IntegrationDeliveries => Set<IntegrationDelivery>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<EmailDelivery> EmailDeliveries => Set<EmailDelivery>();
    public DbSet<ChatChannel> ChatChannels => Set<ChatChannel>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ChannelReadState> ChannelReadStates => Set<ChannelReadState>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<AnnouncementRead> AnnouncementReads => Set<AnnouncementRead>();
    public DbSet<Meeting> Meetings => Set<Meeting>();
    public DbSet<MeetingAgendaItem> MeetingAgendaItems => Set<MeetingAgendaItem>();
    public DbSet<MeetingParticipant> MeetingParticipants => Set<MeetingParticipant>();
    public DbSet<MeetingActionItem> MeetingActionItems => Set<MeetingActionItem>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ChatReply> ChatReplies => Set<ChatReply>();
    public DbSet<ChatCommand> ChatCommands => Set<ChatCommand>();
    public DbSet<SeedMarker> SeedMarkers => Set<SeedMarker>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<Domain.Security.RetentionPolicy> RetentionPolicies => Set<Domain.Security.RetentionPolicy>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        b.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles");
        b.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
        b.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
        b.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");
        b.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims");
        ApplyProjectScope(b);
    }

    // Read per query from this context instance, so each request gets its own scope (EF parameterizes context members in filters).
    private bool ProjectsRestricted => projectScope?.Restricted ?? false;
    private List<Guid> VisibleProjectIds => projectScope?.VisibleProjectIds ?? [];

    /// <summary>
    /// Hides other projects' data from people without project.all. Tables that hang off a task, run, repository or
    /// workflow follow their parent, so nothing project-owned can be reached by id either. Chat channels and meetings keep
    /// their own membership rules (see CommunicationService / MeetingService).
    /// </summary>
    private void ApplyProjectScope(ModelBuilder b)
    {
        b.Entity<Project>().HasQueryFilter(x => !ProjectsRestricted || VisibleProjectIds.Contains(x.Id));
        b.Entity<Milestone>().HasQueryFilter(x => !ProjectsRestricted || VisibleProjectIds.Contains(x.ProjectId));
        b.Entity<WorkTask>().HasQueryFilter(x => !ProjectsRestricted || VisibleProjectIds.Contains(x.ProjectId));
        b.Entity<AgentRun>().HasQueryFilter(x => !ProjectsRestricted || VisibleProjectIds.Contains(x.ProjectId));
        b.Entity<UsageRecord>().HasQueryFilter(x => !ProjectsRestricted || VisibleProjectIds.Contains(x.ProjectId));
        b.Entity<WorkflowExecution>().HasQueryFilter(x => !ProjectsRestricted || VisibleProjectIds.Contains(x.ProjectId));
        b.Entity<ProjectRequirement>().HasQueryFilter(x => !ProjectsRestricted || VisibleProjectIds.Contains(x.ProjectId));
        b.Entity<ArchitectureDecision>().HasQueryFilter(x => !ProjectsRestricted || VisibleProjectIds.Contains(x.ProjectId));
        b.Entity<Defect>().HasQueryFilter(x => !ProjectsRestricted || VisibleProjectIds.Contains(x.ProjectId));
        b.Entity<Release>().HasQueryFilter(x => !ProjectsRestricted || VisibleProjectIds.Contains(x.ProjectId));
        b.Entity<DeliveryPackage>().HasQueryFilter(x => !ProjectsRestricted || VisibleProjectIds.Contains(x.ProjectId));
        b.Entity<ProjectEstimate>().HasQueryFilter(x => !ProjectsRestricted || VisibleProjectIds.Contains(x.ProjectId));
        b.Entity<CodeRepository>().HasQueryFilter(x => !ProjectsRestricted || VisibleProjectIds.Contains(x.ProjectId));
        b.Entity<MergeRequest>().HasQueryFilter(x => !ProjectsRestricted || VisibleProjectIds.Contains(x.ProjectId));
        b.Entity<ProjectDocument>().HasQueryFilter(x => !ProjectsRestricted || VisibleProjectIds.Contains(x.ProjectId));
        b.Entity<ProjectMember>().HasQueryFilter(x => !ProjectsRestricted || VisibleProjectIds.Contains(x.ProjectId));

        // Company-wide rows (no project) stay visible.
        b.Entity<ApprovalRequest>().HasQueryFilter(x => !ProjectsRestricted || x.ProjectId == null || VisibleProjectIds.Contains(x.ProjectId.Value));
        b.Entity<CostRecord>().HasQueryFilter(x => !ProjectsRestricted || x.ProjectId == null || VisibleProjectIds.Contains(x.ProjectId.Value));
        b.Entity<KnowledgeEntry>().HasQueryFilter(x => !ProjectsRestricted || x.ProjectId == null || VisibleProjectIds.Contains(x.ProjectId.Value));

        // Children follow their (already filtered) parent.
        b.Entity<TaskAssignment>().HasQueryFilter(x => !ProjectsRestricted || Tasks.Any(t => t.Id == x.TaskId));
        b.Entity<TaskDependency>().HasQueryFilter(x => !ProjectsRestricted || Tasks.Any(t => t.Id == x.TaskId));
        b.Entity<Deliverable>().HasQueryFilter(x => !ProjectsRestricted || Tasks.Any(t => t.Id == x.TaskId));
        b.Entity<Review>().HasQueryFilter(x => !ProjectsRestricted || Tasks.Any(t => t.Id == x.TaskId));
        b.Entity<AgentRunStep>().HasQueryFilter(x => !ProjectsRestricted || AgentRuns.Any(r => r.Id == x.RunId));
        b.Entity<ToolExecution>().HasQueryFilter(x => !ProjectsRestricted || AgentRuns.Any(r => r.Id == x.RunId));
        b.Entity<TestRun>().HasQueryFilter(x => !ProjectsRestricted || CodeRepositories.Any(r => r.Id == x.RepositoryId));
        b.Entity<WorkflowEvent>().HasQueryFilter(x => !ProjectsRestricted || WorkflowExecutions.Any(w => w.Id == x.WorkflowExecutionId));
        b.Entity<EstimateLineItem>().HasQueryFilter(x => !ProjectsRestricted || Estimates.Any(e => e.Id == x.EstimateId));
    }

    private const long EngineLockKey = 0x0FF1CEA1;

    public async Task<bool> TryAcquireEngineLockAsync(CancellationToken ct)
    {
        await Database.OpenConnectionAsync(ct); // session lock: keep the connection until released
        var got = await Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock({EngineLockKey}) AS \"Value\"").SingleAsync(ct);
        if (!got) await Database.CloseConnectionAsync();
        return got;
    }

    public async Task ReleaseEngineLockAsync()
    {
        await Database.SqlQuery<bool>($"SELECT pg_advisory_unlock({EngineLockKey}) AS \"Value\"").SingleAsync();
        await Database.CloseConnectionAsync();
    }

    public void ResetTracking(params object[] keep)
    {
        ChangeTracker.Clear();
        foreach (var entity in keep) Update(entity);
    }

    public void ExpectVersion<T>(T entity, uint version) where T : Entity =>
        Entry(entity).Property(e => e.Version).OriginalValue = version;

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        StampEntities(now);
        AuditLogs.AddRange(BuildAuditEntries(now));
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException();
        }
    }

    private void StampEntities(DateTimeOffset now)
    {
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedBy = currentUser.UserId;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedBy = currentUser.UserId;
            }
        }
    }

    private static readonly HashSet<string> SkippedProperties =
        [nameof(Entity.Version), nameof(Entity.CreatedAt), nameof(Entity.CreatedBy), nameof(Entity.UpdatedAt), nameof(Entity.UpdatedBy),
            nameof(AiProviderCredential.EncryptedSecret)];

    private List<AuditLog> BuildAuditEntries(DateTimeOffset now)
    {
        var logs = new List<AuditLog>();
        foreach (var entry in ChangeTracker.Entries().Where(e => e.Entity is IAuditable))
        {
            var action = entry.State switch
            {
                EntityState.Added => AuditActions.Created,
                EntityState.Modified => AuditActions.Updated,
                EntityState.Deleted => AuditActions.Deleted,
                _ => null,
            };
            if (action is null) continue;

            var props = entry.Properties.Where(p => !SkippedProperties.Contains(p.Metadata.Name)).ToList();
            Dictionary<string, object?>? oldValues = null, newValues = null;
            if (entry.State != EntityState.Added)
                oldValues = props.Where(p => entry.State == EntityState.Deleted || p.IsModified)
                    .ToDictionary(p => p.Metadata.Name, p => p.OriginalValue);
            if (entry.State != EntityState.Deleted)
                newValues = props.Where(p => entry.State == EntityState.Added || p.IsModified)
                    .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);

            if (entry.State == EntityState.Modified && newValues!.Count == 0) continue;

            logs.Add(new AuditLog
            {
                OccurredAt = now,
                ActorUserId = currentUser.UserId,
                ActorName = currentUser.Name,
                Action = action,
                EntityType = entry.Metadata.ClrType.Name,
                EntityId = KeyOf(entry),
                OldValues = oldValues is null ? null : JsonSerializer.Serialize(oldValues),
                NewValues = newValues is null ? null : JsonSerializer.Serialize(newValues),
                CorrelationId = currentUser.CorrelationId,
            });
        }
        return logs;
    }

    private static string KeyOf(EntityEntry entry) =>
        string.Join("/", entry.Metadata.FindPrimaryKey()!.Properties.Select(p => entry.Property(p.Name).CurrentValue));
}
