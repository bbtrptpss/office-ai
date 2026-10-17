using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OfficeAI.Domain.Organization;
using OfficeAI.Domain.Work;
using OfficeAI.Domain.Workflows;

namespace OfficeAI.Infrastructure.Persistence;

/// <summary>Records one-off data seeds that have been applied, so they never run twice.</summary>
public class SeedMarker
{
    public required string Key { get; set; }
    public DateTimeOffset AppliedAt { get; set; }
}

internal class SeedMarkerConfig : IEntityTypeConfiguration<SeedMarker>
{
    public void Configure(EntityTypeBuilder<SeedMarker> b)
    {
        b.ToTable("SeedMarkers");
        b.HasKey(m => m.Key);
        b.Property(m => m.Key).HasMaxLength(100);
    }
}

internal class WorkflowDefinitionConfig : IEntityTypeConfiguration<WorkflowDefinition>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinition> b)
    {
        EntityConfig.Base(b);
        b.ToTable("WorkflowDefinitions");
        b.Property(d => d.Code).HasMaxLength(50);
        b.Property(d => d.Name).HasMaxLength(200);
        b.Property(d => d.DefaultBudgetUsd).HasPrecision(18, 4);
        b.HasIndex(d => d.Code).IsUnique();
    }
}

internal class WorkflowExecutionConfig : IEntityTypeConfiguration<WorkflowExecution>
{
    public void Configure(EntityTypeBuilder<WorkflowExecution> b)
    {
        EntityConfig.Base(b);
        b.ToTable("WorkflowExecutions");
        b.Ignore(w => w.IsOpen);
        b.Property(w => w.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(w => w.BudgetUsd).HasPrecision(18, 4);
        b.Property(w => w.PlanJson).HasColumnType("jsonb");
        b.HasIndex(w => new { w.Status, w.CreatedAt });
        b.HasIndex(w => w.ProjectId);
        b.HasOne<WorkflowDefinition>().WithMany().HasForeignKey(w => w.DefinitionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Project>().WithMany().HasForeignKey(w => w.ProjectId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<EmployeeProfile>().WithMany().HasForeignKey(w => w.PlannerEmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class WorkflowEventConfig : IEntityTypeConfiguration<WorkflowEvent>
{
    public void Configure(EntityTypeBuilder<WorkflowEvent> b)
    {
        b.ToTable("WorkflowEvents");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).UseIdentityAlwaysColumn();
        b.Property(e => e.Kind).HasMaxLength(50);
        b.HasIndex(e => new { e.WorkflowExecutionId, e.Id });
        b.HasOne<WorkflowExecution>().WithMany().HasForeignKey(e => e.WorkflowExecutionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class ApprovalRequestConfig : IEntityTypeConfiguration<ApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ApprovalRequest> b)
    {
        EntityConfig.Base(b);
        b.ToTable("ApprovalRequests");
        b.Property(a => a.Kind).HasConversion<string>().HasMaxLength(30);
        b.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(a => a.Title).HasMaxLength(300);
        b.Property(a => a.PayloadJson).HasColumnType("jsonb");
        b.Property(a => a.PayloadHash).HasMaxLength(64);
        b.HasIndex(a => new { a.Status, a.CreatedAt });
        b.HasIndex(a => a.TaskId);
        b.HasIndex(a => a.WorkflowExecutionId);
    }
}

internal class ReviewConfig : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> b)
    {
        EntityConfig.Base(b);
        b.ToTable("Reviews");
        b.Property(r => r.Outcome).HasConversion<string>().HasMaxLength(30);
        b.HasIndex(r => r.TaskId);
        b.HasOne<WorkTask>().WithMany().HasForeignKey(r => r.TaskId).OnDelete(DeleteBehavior.Cascade);
    }
}
