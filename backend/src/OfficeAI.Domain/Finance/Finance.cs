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
