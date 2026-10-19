using OfficeAI.Domain.Common;

namespace OfficeAI.Domain.Delivery;

/// <summary>New → Clarifying (questions sent to the customer) → Qualified (nothing essential missing) → Converted into a project; or Lost.</summary>
public enum LeadStatus
{
    New = 1,
    Clarifying = 2,
    Qualified = 3,
    Converted = 4,
    Lost = 5,
}

/// <summary>
/// A customer request before it becomes a project (Master Prompt §7 stage 1). The system lists what is still missing and the
/// clarification questions asked; a lead is converted into a project only when the essentials are known.
/// </summary>
public class Lead : Entity, IAuditable
{
    /// <summary>Company-wide: LEAD-001, LEAD-002…</summary>
    public required string Code { get; set; }
    public required string Title { get; set; }
    public required string CustomerName { get; set; }
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }

    /// <summary>Where the request came from: referral, website, email…</summary>
    public string? Source { get; set; }

    /// <summary>The customer's request in their own words.</summary>
    public required string Need { get; set; }
    public string? BusinessGoal { get; set; }
    public string? TargetUsers { get; set; }

    /// <summary>Web, mobile, desktop, integrations…</summary>
    public string? Platforms { get; set; }
    public string? Constraints { get; set; }
    public decimal? Budget { get; set; }
    public DateOnly? DesiredDueDate { get; set; }

    public LeadStatus Status { get; set; } = LeadStatus.New;
    public Guid? ProjectId { get; set; }
    public DateTimeOffset? ConvertedAt { get; set; }
    public string? LostReason { get; set; }
    public List<LeadQuestion> Questions { get; set; } = [];
}

/// <summary>A clarification question for the customer and, once relayed back, their answer.</summary>
public class LeadQuestion : Entity, IAuditable
{
    public Guid LeadId { get; set; }

    /// <summary>1, 2, 3… in the order asked; questions added together share a timestamp.</summary>
    public int Sequence { get; set; }

    /// <summary>The missing-information check it addresses ("budget", "targetUsers"…) or "other".</summary>
    public required string Topic { get; set; }
    public required string Question { get; set; }
    public string? Answer { get; set; }
    public DateTimeOffset? AnsweredAt { get; set; }
}
