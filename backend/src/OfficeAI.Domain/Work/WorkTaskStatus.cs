namespace OfficeAI.Domain.Work;

public enum WorkTaskStatus
{
    Todo = 1,
    Assigned = 2,
    InProgress = 3,
    Blocked = 4,
    InReview = 5,
    WaitingApproval = 6,
    Done = 7,
    Failed = 8,
    Cancelled = 9,
}

/// <summary>The only legal task status transitions. Anything else is rejected.</summary>
public static class TaskStateMachine
{
    private static readonly Dictionary<WorkTaskStatus, WorkTaskStatus[]> Allowed = new()
    {
        [WorkTaskStatus.Todo] = [WorkTaskStatus.Assigned, WorkTaskStatus.Cancelled],
        [WorkTaskStatus.Assigned] = [WorkTaskStatus.InProgress, WorkTaskStatus.Todo, WorkTaskStatus.Cancelled],
        [WorkTaskStatus.InProgress] = [WorkTaskStatus.InReview, WorkTaskStatus.Blocked, WorkTaskStatus.Failed, WorkTaskStatus.Cancelled],
        [WorkTaskStatus.Blocked] = [WorkTaskStatus.InProgress, WorkTaskStatus.Cancelled],
        [WorkTaskStatus.InReview] = [WorkTaskStatus.Done, WorkTaskStatus.InProgress, WorkTaskStatus.WaitingApproval, WorkTaskStatus.Cancelled],
        [WorkTaskStatus.WaitingApproval] = [WorkTaskStatus.Done, WorkTaskStatus.InProgress, WorkTaskStatus.Cancelled],
        [WorkTaskStatus.Failed] = [WorkTaskStatus.Todo, WorkTaskStatus.Cancelled],
        [WorkTaskStatus.Done] = [],
        [WorkTaskStatus.Cancelled] = [],
    };

    public static bool CanTransition(WorkTaskStatus from, WorkTaskStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    public static IReadOnlyList<WorkTaskStatus> NextStates(WorkTaskStatus from) => Allowed[from];

    /// <summary>Moving to Done from review or approval needs the approver permission, not just task update rights.</summary>
    public static bool RequiresApprover(WorkTaskStatus from, WorkTaskStatus to) =>
        to == WorkTaskStatus.Done;
}
