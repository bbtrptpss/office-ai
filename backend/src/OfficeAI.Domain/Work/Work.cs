using OfficeAI.Domain.Common;

namespace OfficeAI.Domain.Work;

public enum ProjectStatus
{
    Draft = 1,
    Active = 2,
    OnHold = 3,
    Completed = 4,
    Cancelled = 5,
}

public class Project : Entity, IAuditable
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public ProjectStatus Status { get; set; } = ProjectStatus.Draft;
    public Guid? OwnerEmployeeId { get; set; }

    /// <summary>Planned budget (estimate). Actual costs are recorded separately in Phase 2+.</summary>
    public decimal? EstimatedBudget { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public List<Milestone> Milestones { get; set; } = [];
}

public class Milestone : Entity, IAuditable
{
    public Guid ProjectId { get; set; }
    public required string Name { get; set; }
    public DateOnly? DueDate { get; set; }
    public int SortOrder { get; set; }
}

public enum TaskPriority
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4,
}

public enum TaskComplexity
{
    Routine = 1,
    Standard = 2,
    Complex = 3,
}

/// <summary>A unit of work. Named WorkTask to avoid clashing with System.Threading.Tasks.Task.</summary>
public class WorkTask : Entity, IAuditable
{
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
    public Guid? MilestoneId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? AcceptanceCriteria { get; set; }
    public WorkTaskStatus Status { get; set; } = WorkTaskStatus.Todo;
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;

    /// <summary>Picks the model tier for AI runs: routine work goes to a cheaper model, complex work to a stronger one.</summary>
    public TaskComplexity Complexity { get; set; } = TaskComplexity.Standard;
    public DateOnly? DueDate { get; set; }
    public List<TaskAssignment> Assignments { get; set; } = [];

    // Orchestration (Phase 3)
    public Guid? WorkflowExecutionId { get; set; }

    /// <summary>Key of the plan item this task came from, unique within its workflow.</summary>
    public string? PlanKey { get; set; }
    public Guid? ParentTaskId { get; set; }
    public int DelegationDepth { get; set; }
    public List<string> RequiredSkills { get; set; } = [];
    public Guid? ReviewerEmployeeId { get; set; }
}

public class TaskDependency : IAuditable
{
    public Guid TaskId { get; set; }
    public Guid DependsOnTaskId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class TaskAssignment : Entity, IAuditable
{
    public Guid TaskId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
    public DateTimeOffset? UnassignedAt { get; set; }
    public bool IsActive => UnassignedAt is null;
}
