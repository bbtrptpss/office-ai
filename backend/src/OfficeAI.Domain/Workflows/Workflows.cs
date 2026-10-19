using System.Security.Cryptography;
using System.Text;
using OfficeAI.Domain.Common;

namespace OfficeAI.Domain.Workflows;

/// <summary>A reusable orchestration template and its limits.</summary>
public class WorkflowDefinition : Entity, IAuditable
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public int MaxParallelRuns { get; set; } = 3;
    public int MaxRetriesPerTask { get; set; } = 2;
    public int MaxRunsPerTask { get; set; } = 5;
    public int MaxDelegationDepth { get; set; } = 2;
    public decimal DefaultBudgetUsd { get; set; } = 20m;
    public bool RequireAiReview { get; set; } = true;
    public bool IsActive { get; set; } = true;
}

public enum WorkflowStatus
{
    Planning = 1,
    AwaitingPlanApproval = 2,
    Executing = 3,
    Completed = 4,
    Failed = 5,
    Cancelled = 6,
}

/// <summary>One objective being carried out. All orchestration state is here and on its tasks.</summary>
public class WorkflowExecution : Entity, IAuditable
{
    public Guid DefinitionId { get; set; }
    public Guid ProjectId { get; set; }
    public required string Objective { get; set; }
    public WorkflowStatus Status { get; set; } = WorkflowStatus.Planning;
    public Guid PlannerEmployeeId { get; set; }

    /// <summary>When set, every task of this objective (and every review) goes only to AI employees of this department.</summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>The task the planner works on; Done once the plan is approved.</summary>
    public Guid? PlanningTaskId { get; set; }

    /// <summary>Validated plan proposed by the planner (JSON), shown in the approval request.</summary>
    public string? PlanJson { get; set; }
    public decimal BudgetUsd { get; set; }
    public int MaxParallelRuns { get; set; }
    public Guid? ApproverUserId { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Error { get; set; }

    public bool IsOpen => Status is WorkflowStatus.Planning or WorkflowStatus.AwaitingPlanApproval or WorkflowStatus.Executing;
}

/// <summary>Timeline of orchestration decisions, for people and for observability.</summary>
public class WorkflowEvent
{
    public long Id { get; set; }
    public Guid WorkflowExecutionId { get; set; }
    public Guid? TaskId { get; set; }
    public required string Kind { get; set; }
    public required string Message { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public enum ApprovalKind
{
    Plan = 1,
    TaskCompletion = 2,
    ToolAction = 3,
    Escalation = 4,
    Estimate = 5,
    Merge = 6,
    ProductionDeployment = 7,
}

public enum ApprovalStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Expired = 4,
    Cancelled = 5,
}

/// <summary>A decision only a human may take. Bound to a hash of what was shown, so it cannot approve something else.</summary>
public class ApprovalRequest : Entity, IAuditable
{
    public ApprovalKind Kind { get; set; }
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public required string Title { get; set; }
    public string? Details { get; set; }
    public required string PayloadJson { get; set; }
    public required string PayloadHash { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? WorkflowExecutionId { get; set; }
    public Guid? RunId { get; set; }
    public Guid? RequestedByEmployeeId { get; set; }
    public Guid? RequestedByUserId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }

    /// <summary>For ToolAction: set once the approved action has been executed, so it can never run twice.</summary>
    public DateTimeOffset? ExecutedAt { get; set; }

    public static string Hash(string payload) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
}

public enum ReviewOutcome
{
    Approved = 1,
    ChangesRequested = 2,
}

public class Review : Entity, IAuditable
{
    public Guid TaskId { get; set; }
    public Guid? DeliverableId { get; set; }
    public Guid? ReviewerEmployeeId { get; set; }
    public Guid? ReviewerUserId { get; set; }
    public Guid? RunId { get; set; }
    public ReviewOutcome Outcome { get; set; }
    public required string Comments { get; set; }
    public bool IsSimulated { get; set; }
}
