using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OfficeAI.Domain.Delivery;
using OfficeAI.Domain.Organization;
using OfficeAI.Domain.Work;

namespace OfficeAI.Infrastructure.Persistence;

internal class RequirementConfig : IEntityTypeConfiguration<ProjectRequirement>
{
    public void Configure(EntityTypeBuilder<ProjectRequirement> b)
    {
        EntityConfig.Base(b);
        b.ToTable("ProjectRequirements");
        b.Property(r => r.Code).HasMaxLength(20);
        b.Property(r => r.Title).HasMaxLength(200);
        b.Property(r => r.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(r => r.Priority).HasConversion<string>().HasMaxLength(10);
        b.Property(r => r.Status).HasConversion<string>().HasMaxLength(10);
        b.HasIndex(r => new { r.ProjectId, r.Code }).IsUnique();
        b.HasOne<Project>().WithMany().HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class DecisionConfig : IEntityTypeConfiguration<ArchitectureDecision>
{
    public void Configure(EntityTypeBuilder<ArchitectureDecision> b)
    {
        EntityConfig.Base(b);
        b.ToTable("ArchitectureDecisions");
        b.Property(a => a.Title).HasMaxLength(200);
        b.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(a => new { a.ProjectId, a.Number }).IsUnique();
        b.HasOne<Project>().WithMany().HasForeignKey(a => a.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class RoleRateConfig : IEntityTypeConfiguration<RoleRate>
{
    public void Configure(EntityTypeBuilder<RoleRate> b)
    {
        EntityConfig.Base(b);
        b.ToTable("RoleRates");
        b.Property(r => r.HourlyRateThb).HasPrecision(18, 2);
        b.HasIndex(r => r.RoleDefinitionId).IsUnique();
        b.HasOne<EmployeeRoleDefinition>().WithMany().HasForeignKey(r => r.RoleDefinitionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class CompanySettingsConfig : IEntityTypeConfiguration<CompanySettings>
{
    public void Configure(EntityTypeBuilder<CompanySettings> b)
    {
        EntityConfig.Base(b);
        b.ToTable("CompanySettings");
        b.Property(s => s.UsdToThb).HasPrecision(18, 4);
        b.Property(s => s.DefaultContingencyPercent).HasPrecision(6, 2);
        b.Property(s => s.TargetMarginPercent).HasPrecision(6, 2);
    }
}

internal class EstimateConfig : IEntityTypeConfiguration<ProjectEstimate>
{
    public void Configure(EntityTypeBuilder<ProjectEstimate> b)
    {
        EntityConfig.Base(b);
        b.ToTable("ProjectEstimates");
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Currency).HasMaxLength(3);
        foreach (var p in new[] { nameof(ProjectEstimate.LaborCost), nameof(ProjectEstimate.AiCost), nameof(ProjectEstimate.Contingency),
                     nameof(ProjectEstimate.TotalCost), nameof(ProjectEstimate.Price), nameof(ProjectEstimate.GrossMargin), nameof(ProjectEstimate.TotalHours) })
            b.Property<decimal>(p).HasPrecision(18, 2);
        foreach (var p in new[] { nameof(ProjectEstimate.ContingencyPercent), nameof(ProjectEstimate.TargetMarginPercent), nameof(ProjectEstimate.GrossMarginPercent) })
            b.Property<decimal>(p).HasPrecision(6, 2);
        b.Property(e => e.AiCostUsd).HasPrecision(18, 4);
        b.Property(e => e.UsdToThb).HasPrecision(18, 4);
        b.HasIndex(e => new { e.ProjectId, e.Revision }).IsUnique();
        b.HasOne<Project>().WithMany().HasForeignKey(e => e.ProjectId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(e => e.Items).WithOne().HasForeignKey(i => i.EstimateId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class EstimateLineItemConfig : IEntityTypeConfiguration<EstimateLineItem>
{
    public void Configure(EntityTypeBuilder<EstimateLineItem> b)
    {
        b.ToTable("EstimateLineItems");
        b.HasKey(i => i.Id);
        b.Property(i => i.Description).HasMaxLength(500);
        b.Property(i => i.RoleTitle).HasMaxLength(200);
        b.Property(i => i.Hours).HasPrecision(10, 2);
        b.Property(i => i.RateThb).HasPrecision(18, 2);
        b.Property(i => i.Amount).HasPrecision(18, 2);
    }
}

internal class DefectConfig : IEntityTypeConfiguration<Defect>
{
    public void Configure(EntityTypeBuilder<Defect> b)
    {
        EntityConfig.Base(b);
        b.ToTable("Defects");
        b.Property(d => d.Code).HasMaxLength(20);
        b.Property(d => d.Title).HasMaxLength(200);
        b.Property(d => d.Environment).HasMaxLength(500);
        b.Property(d => d.Severity).HasConversion<string>().HasMaxLength(10);
        b.Property(d => d.Status).HasConversion<string>().HasMaxLength(10);
        b.HasIndex(d => new { d.ProjectId, d.Code }).IsUnique();
        b.HasIndex(d => new { d.TaskId, d.Status });
        b.HasIndex(d => d.FixTaskId);
        b.HasOne<Project>().WithMany().HasForeignKey(d => d.ProjectId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<WorkTask>().WithMany().HasForeignKey(d => d.TaskId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<WorkTask>().WithMany().HasForeignKey(d => d.FixTaskId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal class ReleaseConfig : IEntityTypeConfiguration<Release>
{
    public void Configure(EntityTypeBuilder<Release> b)
    {
        EntityConfig.Base(b);
        b.ToTable("Releases");
        b.Property(r => r.Tag).HasMaxLength(40);
        b.Property(r => r.Title).HasMaxLength(200);
        b.Property(r => r.CommitSha).HasMaxLength(64);
        b.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(r => new { r.ProjectId, r.Tag }).IsUnique();
        b.HasOne<Project>().WithMany().HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class LeadConfig : IEntityTypeConfiguration<Lead>
{
    public void Configure(EntityTypeBuilder<Lead> b)
    {
        EntityConfig.Base(b);
        b.ToTable("Leads");
        b.Property(l => l.Code).HasMaxLength(20);
        b.Property(l => l.Title).HasMaxLength(200);
        b.Property(l => l.CustomerName).HasMaxLength(200);
        b.Property(l => l.ContactName).HasMaxLength(200);
        b.Property(l => l.ContactEmail).HasMaxLength(200);
        b.Property(l => l.ContactPhone).HasMaxLength(50);
        b.Property(l => l.Source).HasMaxLength(100);
        b.Property(l => l.Budget).HasPrecision(18, 2);
        b.Property(l => l.Status).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(l => l.Code).IsUnique();
        b.HasIndex(l => l.ProjectId);
        b.HasOne<Project>().WithMany().HasForeignKey(l => l.ProjectId).OnDelete(DeleteBehavior.SetNull);
        b.HasMany(l => l.Questions).WithOne().HasForeignKey(q => q.LeadId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class LeadQuestionConfig : IEntityTypeConfiguration<LeadQuestion>
{
    public void Configure(EntityTypeBuilder<LeadQuestion> b)
    {
        EntityConfig.Base(b);
        b.ToTable("LeadQuestions");
        b.Property(q => q.Topic).HasMaxLength(40);
        b.Property(q => q.Question).HasMaxLength(2000);
    }
}
