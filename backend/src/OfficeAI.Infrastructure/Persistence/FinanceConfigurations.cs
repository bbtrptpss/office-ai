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
