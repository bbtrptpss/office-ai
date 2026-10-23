using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OfficeAI.Domain.Finance;

namespace OfficeAI.Infrastructure.Persistence;

internal class BudgetAllocationConfig : IEntityTypeConfiguration<BudgetAllocation>
{
    public void Configure(EntityTypeBuilder<BudgetAllocation> b)
    {
        EntityConfig.Base(b);
        b.ToTable("BudgetAllocations", t => t.HasCheckConstraint("CK_BudgetAllocations_Amount", "\"AmountUsd\" >= 0"));
        b.Property(x => x.Scope).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.AmountUsd).HasPrecision(18, 4);
        b.HasIndex(x => new { x.Scope, x.ScopeId, x.Month }).IsUnique().AreNullsDistinct(false);
    }
}

internal class OperatingCostConfig : IEntityTypeConfiguration<OperatingCost>
{
    public void Configure(EntityTypeBuilder<OperatingCost> b)
    {
        EntityConfig.Base(b);
        b.ToTable("OperatingCosts", t => t.HasCheckConstraint("CK_OperatingCosts_Amount", "\"MonthlyAmount\" >= 0"));
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Category).HasMaxLength(40);
        b.Property(x => x.MonthlyAmount).HasPrecision(18, 2);
        b.Property(x => x.Currency).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Allocation).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Provider).HasMaxLength(50);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasOne<OfficeAI.Domain.Work.Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
    }
}
