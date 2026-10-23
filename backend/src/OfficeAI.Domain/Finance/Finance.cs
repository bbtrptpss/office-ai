using OfficeAI.Domain.Common;

namespace OfficeAI.Domain.Finance;

public enum BudgetScope
{
    Company = 1,
    Department = 2,
    Project = 3,
}

/// <summary>Monthly AI spending cap (USD) for the company, a department or a project. New runs stop once it is used up.</summary>
public class BudgetAllocation : Entity, IAuditable
{
    public BudgetScope Scope { get; set; }

    /// <summary>Department or project id; null for the company-wide budget.</summary>
    public Guid? ScopeId { get; set; }

    /// <summary>First day of the month the budget applies to.</summary>
    public DateOnly Month { get; set; }
    public decimal AmountUsd { get; set; }
}

public enum BudgetLevel
{
    Ok = 1,
    Warning = 2,
    Exceeded = 3,
}

public static class BudgetMath
{
    public const decimal WarningShare = 0.8m;

    public static BudgetLevel Level(decimal spent, decimal budget) =>
        spent >= budget ? BudgetLevel.Exceeded : spent >= budget * WarningShare ? BudgetLevel.Warning : BudgetLevel.Ok;

    public static DateOnly MonthOf(DateTimeOffset t) => new(t.Year, t.Month, 1);

    public static (DateTimeOffset From, DateTimeOffset To) Range(DateOnly month)
    {
        var from = new DateTimeOffset(month.Year, month.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return (from, from.AddMonths(1));
    }
}

public enum CostCurrency
{
    Thb = 1,
    Usd = 2,
}

public enum OperatingCostAllocation
{
    /// <summary>The whole amount belongs to one project.</summary>
    Project = 1,

    /// <summary>Split across projects by the AI tokens they used that month (optionally of one provider only, e.g. the GPU's electricity by Ollama use).</summary>
    AiUsage = 2,

    /// <summary>Company overhead: counted in company totals, never charged to a project.</summary>
    Overhead = 3,
}

/// <summary>
/// A recurring monthly cost that is not a per-token AI charge: electricity for the GPU, a flat-rate AI plan, hosting, software.
/// Kept apart from <c>CostRecords</c> so it never counts towards AI budgets (which stop runs). Entered by people.
/// </summary>
public class OperatingCost : Entity, IAuditable
{
    public required string Name { get; set; }
    public required string Category { get; set; }
    public decimal MonthlyAmount { get; set; }
    public CostCurrency Currency { get; set; } = CostCurrency.Thb;

    /// <summary>The amount is worked out rather than billed (e.g. electricity from watts and hours): shown as an estimate.</summary>
    public bool IsEstimate { get; set; }

    /// <summary>First month it applies to (day 1).</summary>
    public DateOnly StartMonth { get; set; }

    /// <summary>Last month it applies to (day 1); null while it continues.</summary>
    public DateOnly? EndMonth { get; set; }
    public OperatingCostAllocation Allocation { get; set; }

    /// <summary>For <see cref="OperatingCostAllocation.Project"/>.</summary>
    public Guid? ProjectId { get; set; }

    /// <summary>For <see cref="OperatingCostAllocation.AiUsage"/>: only this provider's tokens count; null = all providers.</summary>
    public string? Provider { get; set; }
    public string? Notes { get; set; }

    public bool AppliesTo(DateOnly month) => StartMonth <= month && (EndMonth is null || month <= EndMonth);
}
