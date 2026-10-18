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

public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser, TimeProvider clock)
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
    public DbSet<RoleRate> RoleRates => Set<RoleRate>();
    public DbSet<CompanySettings> CompanySettings => Set<CompanySettings>();
    public DbSet<ProjectEstimate> Estimates => Set<ProjectEstimate>();
    public DbSet<CodeRepository> CodeRepositories => Set<CodeRepository>();
    public DbSet<TestRun> TestRuns => Set<TestRun>();
    public DbSet<MergeRequest> MergeRequests => Set<MergeRequest>();
    public DbSet<BudgetAllocation> BudgetAllocations => Set<BudgetAllocation>();
    public DbSet<KnowledgeEntry> KnowledgeEntries => Set<KnowledgeEntry>();
    public DbSet<ProjectDocument> Documents => Set<ProjectDocument>();
    public DbSet<IntegrationConfiguration> Integrations => Set<IntegrationConfiguration>();
    public DbSet<IntegrationDelivery> IntegrationDeliveries => Set<IntegrationDelivery>();
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
    public DbSet<SeedMarker> SeedMarkers => Set<SeedMarker>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
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
        [nameof(Entity.Version), nameof(Entity.CreatedAt), nameof(Entity.CreatedBy), nameof(Entity.UpdatedAt), nameof(Entity.UpdatedBy)];

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
