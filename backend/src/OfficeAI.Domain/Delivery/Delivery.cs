using OfficeAI.Domain.Common;

namespace OfficeAI.Domain.Delivery;

public enum RequirementKind
{
    Functional = 1,
    NonFunctional = 2,
    Constraint = 3,
    Assumption = 4,
    Risk = 5,
}

/// <summary>MoSCoW priority.</summary>
public enum RequirementPriority
{
    Must = 1,
    Should = 2,
    Could = 3,
    Wont = 4,
}

public enum ReviewState
{
    Draft = 1,
    Approved = 2,
    Rejected = 3,
}

public class ProjectRequirement : Entity, IAuditable
{
    public Guid ProjectId { get; set; }

    /// <summary>Human-friendly id, unique per project: REQ-001, REQ-002…</summary>
    public required string Code { get; set; }
    public RequirementKind Kind { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? AcceptanceCriteria { get; set; }
    public RequirementPriority Priority { get; set; } = RequirementPriority.Should;
    public ReviewState Status { get; set; } = ReviewState.Draft;
    public Guid? AuthorEmployeeId { get; set; }
    public Guid? RunId { get; set; }
}

public enum DecisionStatus
{
    Proposed = 1,
    Accepted = 2,
    Rejected = 3,
    Superseded = 4,
}

/// <summary>Architecture decision record.</summary>
public class ArchitectureDecision : Entity, IAuditable
{
    public Guid ProjectId { get; set; }
    public int Number { get; set; }
    public required string Title { get; set; }
    public required string Context { get; set; }
    public required string Decision { get; set; }
    public string? Consequences { get; set; }
    public DecisionStatus Status { get; set; } = DecisionStatus.Proposed;
    public Guid? AuthorEmployeeId { get; set; }
    public Guid? RunId { get; set; }
}

/// <summary>Internal cost of one hour of work for a position (THB).</summary>
public class RoleRate : Entity, IAuditable
{
    public Guid RoleDefinitionId { get; set; }
    public decimal HourlyRateThb { get; set; }
}

/// <summary>Company-wide commercial settings (single row).</summary>
public class CompanySettings : Entity, IAuditable
{
    /// <summary>Configured conversion for AI costs; not a live market rate.</summary>
    public decimal UsdToThb { get; set; } = 36m;
    public decimal DefaultContingencyPercent { get; set; } = 15m;
    public decimal TargetMarginPercent { get; set; } = 30m;
}

public enum EstimateStatus
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    Rejected = 4,
}

public class ProjectEstimate : Entity, IAuditable
{
    public Guid ProjectId { get; set; }
    public int Revision { get; set; }
    public EstimateStatus Status { get; set; } = EstimateStatus.Submitted;
    public string Currency { get; set; } = "THB";
    public string? Assumptions { get; set; }
    public decimal ContingencyPercent { get; set; }
    public decimal TargetMarginPercent { get; set; }
    public decimal AiCostUsd { get; set; }
    public decimal UsdToThb { get; set; }

    // Computed by EstimateCalculator; stored so the approved numbers never change afterwards.
    public decimal LaborCost { get; set; }
    public decimal AiCost { get; set; }
    public decimal Contingency { get; set; }
    public decimal TotalCost { get; set; }
    public decimal Price { get; set; }
    public decimal GrossMargin { get; set; }
    public decimal GrossMarginPercent { get; set; }
    public decimal TotalHours { get; set; }
    public List<string> Warnings { get; set; } = [];
    public Guid? AuthorEmployeeId { get; set; }
    public Guid? RunId { get; set; }
    public List<EstimateLineItem> Items { get; set; } = [];
}

public class EstimateLineItem
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EstimateId { get; set; }
    public required string Description { get; set; }
    public Guid RoleDefinitionId { get; set; }
    public required string RoleTitle { get; set; }
    public decimal Hours { get; set; }
    public decimal RateThb { get; set; }
    public decimal Amount { get; set; }

    /// <summary>What the hours are based on (similar past task, measured size…). Missing evidence is flagged.</summary>
    public string? Evidence { get; set; }
}

/// <summary>Deterministic estimate arithmetic. Prices are rounded up to the nearest 100 THB.</summary>
public static class EstimateCalculator
{
    public const decimal LargeItemHours = 80m;

    public static void Compute(ProjectEstimate e)
    {
        var warnings = new List<string>();
        foreach (var i in e.Items)
        {
            i.Amount = Math.Round(i.Hours * i.RateThb, 2, MidpointRounding.AwayFromZero);
            if (string.IsNullOrWhiteSpace(i.Evidence)) warnings.Add($"No evidence for '{i.Description}'.");
            if (i.Hours > LargeItemHours) warnings.Add($"'{i.Description}' is {i.Hours} hours; split it to estimate it reliably.");
        }
        if (e.Items.Count == 0) warnings.Add("The estimate has no line items.");
        if (string.IsNullOrWhiteSpace(e.Assumptions)) warnings.Add("No assumptions recorded.");

        e.TotalHours = e.Items.Sum(i => i.Hours);
        e.LaborCost = e.Items.Sum(i => i.Amount);
        e.AiCost = Math.Round(e.AiCostUsd * e.UsdToThb, 2, MidpointRounding.AwayFromZero);
        var subtotal = e.LaborCost + e.AiCost;
        e.Contingency = Math.Round(subtotal * e.ContingencyPercent / 100m, 2, MidpointRounding.AwayFromZero);
        e.TotalCost = subtotal + e.Contingency;
        var margin = Math.Clamp(e.TargetMarginPercent, 0m, 90m) / 100m;
        e.Price = Math.Ceiling(e.TotalCost / (1 - margin) / 100m) * 100m;
        e.GrossMargin = e.Price - e.TotalCost;
        e.GrossMarginPercent = e.Price == 0 ? 0 : Math.Round(e.GrossMargin / e.Price * 100m, 2, MidpointRounding.AwayFromZero);
        e.Warnings = warnings;
    }
}

/// <summary>What-if changes applied to an approved estimate. Null keeps the estimate's own value.</summary>
public record ScenarioInput(decimal HoursChangePercent = 0m, decimal RateChangePercent = 0m, decimal? AiCostUsd = null, decimal? UsdToThb = null,
    decimal? ContingencyPercent = null, decimal? PriceThb = null, bool RepriceAtTargetMargin = false);

public record ScenarioResult(decimal TotalHours, decimal LaborCost, decimal AiCost, decimal Contingency, decimal TotalCost, decimal Price,
    decimal GrossMargin, decimal GrossMarginPercent);

/// <summary>
/// Deterministic scenario planning on top of an approved estimate. Nothing is saved: the agreed price stays
/// unless the scenario sets a new one or asks to re-price at the estimate's target margin.
/// </summary>
public static class ScenarioCalculator
{
    public const decimal MaxChangePercent = 500m;

    public static ScenarioResult Baseline(ProjectEstimate e) =>
        new(e.TotalHours, e.LaborCost, e.AiCost, e.Contingency, e.TotalCost, e.Price, e.GrossMargin, e.GrossMarginPercent);

    public static ScenarioResult Run(ProjectEstimate baseline, ScenarioInput s)
    {
        if (s.HoursChangePercent is < -100m or > MaxChangePercent || s.RateChangePercent is < -100m or > MaxChangePercent)
            throw new DomainException("scenario.invalid", $"Changes must be between -100% and {MaxChangePercent}%.");
        if (s.AiCostUsd < 0 || s.UsdToThb <= 0 || s.ContingencyPercent is < 0m or > 100m || s.PriceThb < 0)
            throw new DomainException("scenario.invalid", "AI cost, price and contingency must be zero or more; the exchange rate must be above zero.");

        var est = new ProjectEstimate
        {
            ContingencyPercent = s.ContingencyPercent ?? baseline.ContingencyPercent,
            TargetMarginPercent = baseline.TargetMarginPercent,
            AiCostUsd = s.AiCostUsd ?? baseline.AiCostUsd,
            UsdToThb = s.UsdToThb ?? baseline.UsdToThb,
            Assumptions = baseline.Assumptions,
            Items = baseline.Items.Select(i => new EstimateLineItem
            {
                Description = i.Description, RoleTitle = i.RoleTitle, RoleDefinitionId = i.RoleDefinitionId, Evidence = i.Evidence,
                Hours = Math.Round(i.Hours * (1 + s.HoursChangePercent / 100m), 2, MidpointRounding.AwayFromZero),
                RateThb = Math.Round(i.RateThb * (1 + s.RateChangePercent / 100m), 2, MidpointRounding.AwayFromZero),
            }).ToList(),
        };
        EstimateCalculator.Compute(est);
        var price = s.RepriceAtTargetMargin ? est.Price : s.PriceThb ?? baseline.Price;
        var margin = price - est.TotalCost;
        var pct = price == 0 ? 0 : Math.Round(margin / price * 100m, 2, MidpointRounding.AwayFromZero);
        return new ScenarioResult(est.TotalHours, est.LaborCost, est.AiCost, est.Contingency, est.TotalCost, price, margin, pct);
    }
}
