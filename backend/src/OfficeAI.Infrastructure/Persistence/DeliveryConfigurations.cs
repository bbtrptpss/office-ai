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
