using OfficeAI.Domain.Common;
using OfficeAI.Domain.Work;

namespace OfficeAI.Domain.Delivery;

public enum DefectSeverity
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4,
}

/// <summary>Open until a person verifies the fix or rejects the report. "Fixed, waiting for verification" is derived from the fix task.</summary>
public enum DefectStatus
{
    Open = 1,
    Verified = 2,
    Rejected = 3,
}

/// <summary>
/// A reproducible defect found during QA (Master Prompt §7 stage 6). Recording one creates a fix task for the developer who
/// worked on the affected task; the affected task cannot be marked Done while the defect is open.
/// </summary>
public class Defect : Entity, IAuditable
{
    public Guid ProjectId { get; set; }

    /// <summary>Human-friendly id, unique per project: DEF-001, DEF-002…</summary>
    public required string Code { get; set; }
    public required string Title { get; set; }
    public DefectSeverity Severity { get; set; } = DefectSeverity.Medium;
    public DefectStatus Status { get; set; } = DefectStatus.Open;

    /// <summary>Required: a defect nobody can reproduce is not recorded.</summary>
    public required string StepsToReproduce { get; set; }
    public string? ExpectedResult { get; set; }
    public string? ActualResult { get; set; }
    public string? Environment { get; set; }

    /// <summary>The task the defect was found in (optional).</summary>
    public Guid? TaskId { get; set; }

    /// <summary>The failing test run that shows it, if any.</summary>
    public Guid? TestRunId { get; set; }

    /// <summary>The task created to fix it; a new one is made each time the defect is reopened.</summary>
    public Guid? FixTaskId { get; set; }
    public int ReopenCount { get; set; }

    public Guid? ReportedByEmployeeId { get; set; }
    public Guid? ReportedByUserId { get; set; }
    public Guid? RunId { get; set; }
    public string? ResolutionNote { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    public static TaskPriority PriorityFor(DefectSeverity s) => s switch
    {
        DefectSeverity.Critical => TaskPriority.Critical,
        DefectSeverity.High => TaskPriority.High,
        DefectSeverity.Medium => TaskPriority.Medium,
        _ => TaskPriority.Low,
    };
}
