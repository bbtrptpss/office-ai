using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Organization;
using OfficeAI.Domain.Work;

namespace OfficeAI.Infrastructure.Persistence;

internal class AiModelConfig : IEntityTypeConfiguration<AiModel>
{
    public void Configure(EntityTypeBuilder<AiModel> b)
    {
        EntityConfig.Base(b);
        b.ToTable("AiModels");
        b.Property(m => m.Provider).HasMaxLength(50);
        b.Property(m => m.ModelId).HasMaxLength(100);
        b.Property(m => m.DisplayName).HasMaxLength(200);
        b.Property(m => m.Tier).HasConversion<string>().HasMaxLength(20);
        foreach (var p in new[] { nameof(AiModel.InputUsdPerMTok), nameof(AiModel.OutputUsdPerMTok), nameof(AiModel.CacheReadUsdPerMTok), nameof(AiModel.CacheWriteUsdPerMTok) })
            b.Property<decimal>(p).HasPrecision(18, 6);
        b.HasIndex(m => new { m.Provider, m.ModelId }).IsUnique();
    }
}

internal class AgentConfigurationConfig : IEntityTypeConfiguration<AgentConfiguration>
{
    public void Configure(EntityTypeBuilder<AgentConfiguration> b)
    {
        EntityConfig.Base(b);
        b.ToTable("AgentConfigurations", t => t.HasCheckConstraint("CK_AgentConfigurations_Budget", "\"MonthlyBudgetUsd\" >= 0"));
        b.Property(c => c.Provider).HasMaxLength(50);
        b.Property(c => c.ModelId).HasMaxLength(100);
        b.Property(c => c.Effort).HasConversion<string>().HasMaxLength(10);
        b.Property(c => c.MonthlyBudgetUsd).HasPrecision(18, 4);
        b.HasIndex(c => c.EmployeeId).IsUnique();
        b.HasOne<EmployeeProfile>().WithOne().HasForeignKey<AgentConfiguration>(c => c.EmployeeId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class AgentToolPermissionConfig : IEntityTypeConfiguration<AgentToolPermission>
{
    public void Configure(EntityTypeBuilder<AgentToolPermission> b)
    {
        b.ToTable("AgentToolPermissions");
        b.HasKey(p => new { p.EmployeeId, p.ToolId });
        b.Property(p => p.ToolId).HasMaxLength(100);
        b.HasOne<EmployeeProfile>().WithMany().HasForeignKey(p => p.EmployeeId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class AgentRunConfig : IEntityTypeConfiguration<AgentRun>
{
    public void Configure(EntityTypeBuilder<AgentRun> b)
    {
        EntityConfig.Base(b);
        b.ToTable("AgentRuns");
        b.Ignore(r => r.IsActive);
        // The executor is the only writer of a run's progress; a cancel flag set meanwhile must not make its saves fail.
        b.Ignore(r => r.Version);
        b.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(r => r.Provider).HasMaxLength(50);
        b.Property(r => r.ModelId).HasMaxLength(100);
        b.Property(r => r.StopReason).HasMaxLength(50);
        b.Property(r => r.CorrelationId).HasMaxLength(100);
        b.Property(r => r.CostUsd).HasPrecision(18, 6);
        b.HasIndex(r => new { r.EmployeeId, r.CreatedAt });
        b.HasIndex(r => new { r.ProjectId, r.CreatedAt });
        // At most one active run per task.
        b.HasIndex(r => r.TaskId).IsUnique().HasFilter("\"Status\" IN ('Queued', 'Running')").HasDatabaseName("IX_AgentRuns_TaskId_Active");
        b.HasIndex(r => r.TaskId);
        b.HasOne<WorkTask>().WithMany().HasForeignKey(r => r.TaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<EmployeeProfile>().WithMany().HasForeignKey(r => r.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class AgentRunStepConfig : IEntityTypeConfiguration<AgentRunStep>
{
    public void Configure(EntityTypeBuilder<AgentRunStep> b)
    {
        b.ToTable("AgentRunSteps");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).UseIdentityAlwaysColumn();
        b.Property(s => s.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(s => s.ToolCallId).HasMaxLength(100);
        b.HasIndex(s => new { s.RunId, s.Sequence }).IsUnique();
        b.HasOne<AgentRun>().WithMany().HasForeignKey(s => s.RunId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class ToolExecutionConfig : IEntityTypeConfiguration<ToolExecution>
{
    public void Configure(EntityTypeBuilder<ToolExecution> b)
    {
        b.ToTable("ToolExecutions");
        b.HasKey(x => x.Id);
        b.Property(x => x.ToolId).HasMaxLength(100);
        b.Property(x => x.ToolVersion).HasMaxLength(20);
        b.Property(x => x.ToolCallId).HasMaxLength(100);
        b.Property(x => x.IdempotencyKey).HasMaxLength(200);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.InputJson).HasColumnType("jsonb");
        b.HasIndex(x => x.IdempotencyKey).IsUnique();
        b.HasIndex(x => x.RunId);
        b.HasOne<AgentRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class UsageRecordConfig : IEntityTypeConfiguration<UsageRecord>
{
    public void Configure(EntityTypeBuilder<UsageRecord> b)
    {
        b.ToTable("UsageRecords");
        b.HasKey(u => u.Id);
        b.Property(u => u.Provider).HasMaxLength(50);
        b.Property(u => u.ModelId).HasMaxLength(100);
        b.HasIndex(u => new { u.EmployeeId, u.OccurredAt });
        b.HasIndex(u => new { u.ProjectId, u.OccurredAt });
        b.HasIndex(u => u.RunId);
        b.HasOne<AgentRun>().WithMany().HasForeignKey(u => u.RunId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class CostRecordConfig : IEntityTypeConfiguration<CostRecord>
{
    public void Configure(EntityTypeBuilder<CostRecord> b)
    {
        b.ToTable("CostRecords");
        b.HasKey(c => c.Id);
        b.Property(c => c.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(c => c.Category).HasMaxLength(50);
        b.Property(c => c.AmountUsd).HasPrecision(18, 6);
        b.Property(c => c.Description).HasMaxLength(500);
        b.HasIndex(c => new { c.EmployeeId, c.Kind, c.OccurredAt });
        b.HasIndex(c => new { c.ProjectId, c.Kind, c.OccurredAt });
        b.HasIndex(c => c.RunId);
    }
}

internal class DeliverableConfig : IEntityTypeConfiguration<Deliverable>
{
    public void Configure(EntityTypeBuilder<Deliverable> b)
    {
        EntityConfig.Base(b);
        b.ToTable("Deliverables");
        b.Property(d => d.Title).HasMaxLength(200);
        b.Property(d => d.Format).HasMaxLength(20);
        b.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(d => d.TaskId);
        b.HasOne<WorkTask>().WithMany().HasForeignKey(d => d.TaskId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<EmployeeProfile>().WithMany().HasForeignKey(d => d.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}
