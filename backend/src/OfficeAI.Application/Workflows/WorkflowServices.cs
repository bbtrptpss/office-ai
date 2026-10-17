using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Application.Agents;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Organization;
using OfficeAI.Domain.Security;
using OfficeAI.Domain.Work;
using OfficeAI.Domain.Workflows;

namespace OfficeAI.Application.Workflows;

public record ApprovalDto(Guid Id, ApprovalKind Kind, ApprovalStatus Status, string Title, string? Details, string PayloadJson, string PayloadHash,
    Guid? ProjectId, Guid? TaskId, Guid? WorkflowExecutionId, Guid? RunId, Guid? RequestedByEmployeeId, string? RequestedByName,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, Guid? DecidedByUserId, DateTimeOffset? DecidedAt, string? DecisionNote,
    bool IsSimulated, string RequiredPermission);
public record DecideApprovalRequest(bool Approve, string PayloadHash, string? Note);

public record StartObjectiveRequest(string Objective, Guid? PlannerEmployeeId = null, decimal? BudgetUsd = null, int? MaxParallelRuns = null);
public record WorkflowDto(Guid Id, Guid ProjectId, string Objective, WorkflowStatus Status, Guid PlannerEmployeeId, string PlannerName,
    Guid? PlanningTaskId, decimal BudgetUsd, decimal SpentUsd, int MaxParallelRuns, int TaskCount, int DoneCount, DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt, string? Error);
public record WorkflowEventDto(long Id, Guid? TaskId, string Kind, string Message, DateTimeOffset CreatedAt);
public record ReviewDto(Guid Id, Guid TaskId, Guid? DeliverableId, string? ReviewerName, ReviewOutcome Outcome, string Comments, bool IsSimulated, DateTimeOffset CreatedAt);

/// <summary>The human approval inbox. Agents can never decide, and nobody approves their own request.</summary>
public class ApprovalService(IAppDbContext db, ICurrentUser current, TimeProvider clock, IRealtimeNotifier notifier, Code.ICodeRepositoryStore code)
{
    public static string PermissionFor(ApprovalKind kind) => kind switch
    {
        ApprovalKind.Plan => Permissions.ProjectManage,
        ApprovalKind.TaskCompletion => Permissions.TaskApprove,
        ApprovalKind.ToolAction => Permissions.AgentManage,
        ApprovalKind.Estimate => Permissions.ProjectManage,
        ApprovalKind.Merge => Permissions.ProjectManage,
        _ => Permissions.TaskManage,
    };

    public async Task<List<ApprovalDto>> ListAsync(ApprovalStatus? status, Guid? workflowId, Guid? taskId, CancellationToken ct)
    {
        var q = db.ApprovalRequests.AsNoTracking();
        if (status is { } s) q = q.Where(a => a.Status == s);
        if (workflowId is { } w) q = q.Where(a => a.WorkflowExecutionId == w);
        if (taskId is { } t) q = q.Where(a => a.TaskId == t);
        var rows = await q.OrderByDescending(a => a.CreatedAt).Take(200).ToListAsync(ct);
        return await ToDtosAsync(rows, ct);
    }

    public async Task<ApprovalDto> GetAsync(Guid id, CancellationToken ct)
    {
        var a = await db.ApprovalRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("ApprovalRequest", id);
        return (await ToDtosAsync([a], ct))[0];
    }

    private async Task<List<ApprovalDto>> ToDtosAsync(List<ApprovalRequest> rows, CancellationToken ct)
    {
        var employeeIds = rows.Select(r => r.RequestedByEmployeeId).OfType<Guid>().Distinct().ToList();
        var names = await db.Employees.AsNoTracking().Where(e => employeeIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.DisplayName, ct);
        var runIds = rows.Select(r => r.RunId).OfType<Guid>().Distinct().ToList();
        var simulated = await db.AgentRuns.AsNoTracking().Where(r => runIds.Contains(r.Id) && r.IsSimulated).Select(r => r.Id).ToListAsync(ct);
        return rows.Select(a => new ApprovalDto(a.Id, a.Kind, a.Status, a.Title, a.Details, a.PayloadJson, a.PayloadHash, a.ProjectId, a.TaskId,
            a.WorkflowExecutionId, a.RunId, a.RequestedByEmployeeId, a.RequestedByEmployeeId is { } e ? names.GetValueOrDefault(e) : null,
            a.CreatedAt, a.ExpiresAt, a.DecidedByUserId, a.DecidedAt, a.DecisionNote, a.RunId is { } r && simulated.Contains(r), PermissionFor(a.Kind))).ToList();
    }

    /// <param name="callerPermissions">Effective permissions of the caller, resolved server-side.</param>
    public async Task<ApprovalDto> DecideAsync(Guid id, DecideApprovalRequest req, IReadOnlySet<string> callerPermissions, CancellationToken ct)
    {
        if (current.IsAgent || current.UserId is null) throw new ForbiddenException("Only a person can decide an approval.");
        var approval = await db.ApprovalRequests.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("ApprovalRequest", id);
        if (!callerPermissions.Contains(PermissionFor(approval.Kind)))
            throw new ForbiddenException($"Deciding this approval needs the {PermissionFor(approval.Kind)} permission.");
        if (approval.RequestedByUserId == current.UserId) throw new ForbiddenException("You cannot approve your own request.");
        if (approval.Status != ApprovalStatus.Pending) throw new DomainException("approval.closed", $"This request is already {approval.Status}.");
        var now = clock.GetUtcNow();
        if (approval.ExpiresAt <= now) throw new DomainException("approval.expired", "This request has expired.");
        if (!string.Equals(req.PayloadHash, approval.PayloadHash, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("approval.changed", "The request changed since you opened it. Reload and review it again.");

        approval.Status = req.Approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        approval.DecidedByUserId = current.UserId;
        approval.DecidedAt = now;
        approval.DecisionNote = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();

        switch (approval.Kind)
        {
            case ApprovalKind.Plan:
                await ApplyPlanDecisionAsync(approval, req.Approve, now, ct);
                break;
            case ApprovalKind.TaskCompletion:
                await ApplyCompletionDecisionAsync(approval, req.Approve, now, ct);
                break;
            case ApprovalKind.Escalation:
                await ApplyEscalationDecisionAsync(approval, req.Approve, now, ct);
                break;
            case ApprovalKind.Estimate:
                await ApplyEstimateDecisionAsync(approval, req.Approve, ct);
                break;
            case ApprovalKind.Merge:
                await ApplyMergeDecisionAsync(approval, req.Approve, now, ct);
                break;
            case ApprovalKind.ToolAction:
                // Executed by the workflow engine (exactly once) on its next tick.
                break;
        }
        await db.SaveChangesAsync(ct);
        if (approval.ProjectId is { } p && approval.TaskId is { } t) await notifier.TaskChangedAsync(p, t, ct);
        await notifier.ApprovalsChangedAsync(ct);
        return await GetAsync(id, ct);
    }

    private async Task ApplyPlanDecisionAsync(ApprovalRequest approval, bool approve, DateTimeOffset now, CancellationToken ct)
    {
        var workflow = await db.WorkflowExecutions.FirstAsync(w => w.Id == approval.WorkflowExecutionId, ct);
        var planningTask = await db.Tasks.FirstAsync(t => t.Id == workflow.PlanningTaskId, ct);
        if (!approve)
        {
            // Back to the planner with the feedback; the engine starts a new planning run.
            workflow.Status = WorkflowStatus.Planning;
            workflow.PlanJson = null;
            planningTask.Status = WorkTaskStatus.InProgress;
            AddHumanReview(planningTask.Id, approval.DecisionNote ?? "Plan rejected.");
            Approvals.Event(db, workflow.Id, planningTask.Id, "plan_rejected", "The plan was rejected; the planner will revise it.", now);
            return;
        }

        var items = PlanParser.Deserialize(workflow.PlanJson ?? "[]");
        var byKey = new Dictionary<string, WorkTask>();
        foreach (var i in items)
        {
            var task = new WorkTask
            {
                ProjectId = workflow.ProjectId, WorkflowExecutionId = workflow.Id, PlanKey = i.Key, Title = i.Title, Description = i.Description,
                AcceptanceCriteria = i.AcceptanceCriteria, Priority = i.Priority, RequiredSkills = i.RequiredSkills,
            };
            db.Tasks.Add(task);
            byKey[i.Key] = task;
        }
        foreach (var i in items)
            foreach (var d in i.DependsOn)
                db.TaskDependencies.Add(new TaskDependency { TaskId = byKey[i.Key].Id, DependsOnTaskId = byKey[d].Id, CreatedAt = now });

        planningTask.Status = WorkTaskStatus.Done;
        workflow.Status = WorkflowStatus.Executing;
        workflow.ApproverUserId = current.UserId;
        Approvals.Event(db, workflow.Id, planningTask.Id, "plan_approved", $"Plan approved; {items.Count} tasks created.", now);
    }

    private async Task ApplyCompletionDecisionAsync(ApprovalRequest approval, bool approve, DateTimeOffset now, CancellationToken ct)
    {
        var task = await db.Tasks.FirstAsync(t => t.Id == approval.TaskId, ct);
        if (task.Status != WorkTaskStatus.WaitingApproval)
            throw new DomainException("approval.task_state", $"The task is {task.Status}, not waiting for approval.");
        if (approve)
        {
            task.Status = WorkTaskStatus.Done;
        }
        else
        {
            task.Status = WorkTaskStatus.InProgress;
            AddHumanReview(task.Id, approval.DecisionNote ?? "Changes requested by the approver.");
        }
        if (task.WorkflowExecutionId is { } wf)
            Approvals.Event(db, wf, task.Id, approve ? "task_done" : "task_rejected", approve ? $"'{task.Title}' approved as done." : $"'{task.Title}' sent back for changes.", now);
    }

    private async Task ApplyEscalationDecisionAsync(ApprovalRequest approval, bool approve, DateTimeOffset now, CancellationToken ct)
    {
        if (approval.TaskId is not { } taskId) return;
        var task = await db.Tasks.FirstAsync(t => t.Id == taskId, ct);
        if (approve)
        {
            // "Try again": unblock; the engine counts attempts from this decision onwards.
            if (task.Status == WorkTaskStatus.Blocked) task.Status = WorkTaskStatus.InProgress;
            if (approval.DecisionNote is { } note) AddHumanReview(task.Id, note);
        }
        else if (TaskStateMachine.CanTransition(task.Status, WorkTaskStatus.Cancelled))
        {
            task.Status = WorkTaskStatus.Cancelled;
        }
        if (task.WorkflowExecutionId is { } wf)
            Approvals.Event(db, wf, task.Id, approve ? "escalation_retry" : "escalation_cancel", approve ? $"'{task.Title}' will be retried." : $"'{task.Title}' was cancelled.", now);
    }

    private async Task ApplyEstimateDecisionAsync(ApprovalRequest approval, bool approve, CancellationToken ct)
    {
        var estimateId = System.Text.Json.JsonDocument.Parse(approval.PayloadJson).RootElement.GetProperty("estimateId").GetGuid();
        var estimate = await db.Estimates.FirstAsync(e => e.Id == estimateId, ct);
        estimate.Status = approve ? Domain.Delivery.EstimateStatus.Approved : Domain.Delivery.EstimateStatus.Rejected;
        if (approve)
        {
            var project = await db.Projects.FirstAsync(p => p.Id == estimate.ProjectId, ct);
            project.EstimatedBudget = estimate.TotalCost; // the planned cost; the quoted price stays on the estimate
        }
        else if (approval.TaskId is { } taskId && approval.DecisionNote is { } note)
        {
            AddHumanReview(taskId, note);
        }
    }

    /// <summary>Merges exactly the commit that was reviewed; if the branch moved since, the request is refused.</summary>
    private async Task ApplyMergeDecisionAsync(ApprovalRequest approval, bool approve, DateTimeOffset now, CancellationToken ct)
    {
        var payload = System.Text.Json.JsonDocument.Parse(approval.PayloadJson).RootElement;
        var mr = await db.MergeRequests.FirstAsync(m => m.Id == payload.GetProperty("mergeRequestId").GetGuid(), ct);
        if (mr.Status != Domain.Code.MergeRequestStatus.Open) throw new DomainException("approval.closed", $"The merge request is {mr.Status}.");
        if (!approve)
        {
            mr.Status = Domain.Code.MergeRequestStatus.Closed;
            if (mr.TaskId is { } t) AddHumanReview(t, approval.DecisionNote ?? $"Merge request !{mr.Number} was not approved.");
            return;
        }
        var repo = await db.CodeRepositories.AsNoTracking().FirstAsync(r => r.Id == mr.RepositoryId, ct);
        var heads = await code.BranchesAsync(repo, ct);
        if (!heads.Contains(mr.SourceBranch)) throw new DomainException("approval.changed", "The source branch no longer exists.");
        var layout = (Code.IRepositoryLayout)code;
        var tip = (await code.GitAsync(System.IO.Path.GetFullPath("."), ["--git-dir", layout.OriginPath(repo), "rev-parse", mr.SourceBranch], ct)).Output.Trim();
        if (tip != mr.HeadSha)
            throw new DomainException("approval.changed", "New commits were pushed after this merge request was opened. Ask for a fresh merge request.");

        var outcome = await code.MergeAsync(repo, mr.SourceBranch, mr.TargetBranch, $"Merge !{mr.Number}: {mr.Title}", (current.Name ?? "CEO", "ceo@officeai.local"), ct);
        if (outcome.Merged)
        {
            mr.Status = Domain.Code.MergeRequestStatus.Merged;
            mr.MergedSha = outcome.Sha;
            mr.MergedAt = now;
        }
        else
        {
            mr.Status = Domain.Code.MergeRequestStatus.Conflict;
            approval.DecisionNote = (approval.DecisionNote is null ? "" : approval.DecisionNote + "\n") + "Merge failed: " + outcome.Error;
            if (mr.TaskId is { } t) AddHumanReview(t, $"Merge request !{mr.Number} conflicts with {mr.TargetBranch}. Update the branch and open a new merge request.");
        }
        if (approval.WorkflowExecutionId is { } wf)
            Approvals.Event(db, wf, mr.TaskId, mr.Status == Domain.Code.MergeRequestStatus.Merged ? "merged" : "merge_conflict",
                mr.Status == Domain.Code.MergeRequestStatus.Merged ? $"!{mr.Number} merged into {mr.TargetBranch}." : $"!{mr.Number} could not be merged.", now);
    }

    private void AddHumanReview(Guid taskId, string comments) =>
        db.Reviews.Add(new Review { TaskId = taskId, ReviewerUserId = current.UserId, Outcome = ReviewOutcome.ChangesRequested, Comments = comments });

    public async Task<List<ReviewDto>> ListReviewsAsync(Guid taskId, CancellationToken ct) =>
        await db.Reviews.AsNoTracking().Where(r => r.TaskId == taskId).OrderByDescending(r => r.CreatedAt)
            .Select(r => new ReviewDto(r.Id, r.TaskId, r.DeliverableId,
                r.ReviewerEmployeeId != null ? db.Employees.Where(e => e.Id == r.ReviewerEmployeeId).Select(e => e.DisplayName).FirstOrDefault() : "CEO / Manager",
                r.Outcome, r.Comments, r.IsSimulated, r.CreatedAt))
            .ToListAsync(ct);
}

/// <summary>CEO-facing entry point: start, inspect and cancel objectives.</summary>
public class ObjectiveService(IAppDbContext db, AgentRunService runs, ICurrentUser current, TimeProvider clock, IRealtimeNotifier notifier)
{
    public const string DefaultDefinition = "objective-delivery";

    public async Task<WorkflowDto> StartAsync(Guid projectId, StartObjectiveRequest req, CancellationToken ct)
    {
        Validate.Required(req.Objective, "objective");
        if (req.Objective.Length > 4000) throw new DomainException("validation", "objective is longer than 4000 characters.");
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct) ?? throw new NotFoundException("Project", projectId);
        if (project.Status is ProjectStatus.Completed or ProjectStatus.Cancelled)
            throw new DomainException("workflow.project_closed", $"The project is {project.Status}.");
        var definition = await db.WorkflowDefinitions.AsNoTracking().FirstOrDefaultAsync(d => d.Code == DefaultDefinition && d.IsActive, ct)
            ?? throw new DomainException("workflow.no_definition", "No active workflow definition.");

        var plannerId = req.PlannerEmployeeId
            ?? await db.Employees.Where(e => e.Kind == EmployeeKind.AI && e.RoleDefinition!.Code == "COO").Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct)
            ?? throw new DomainException("workflow.no_planner", "Choose an AI employee to plan this objective.");
        var planner = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == plannerId, ct) ?? throw new NotFoundException("Employee", plannerId);
        if (planner.Kind != EmployeeKind.AI) throw new DomainException("validation", "The planner must be an AI employee.");
        if (req.BudgetUsd is < 0 or > 100_000) throw new DomainException("validation", "budgetUsd must be between 0 and 100000.");
        if (req.MaxParallelRuns is < 1 or > 20) throw new DomainException("validation", "maxParallelRuns must be between 1 and 20.");

        var now = clock.GetUtcNow();
        var workflow = new WorkflowExecution
        {
            DefinitionId = definition.Id, ProjectId = projectId, Objective = req.Objective.Trim(), PlannerEmployeeId = plannerId,
            BudgetUsd = req.BudgetUsd ?? definition.DefaultBudgetUsd, MaxParallelRuns = req.MaxParallelRuns ?? definition.MaxParallelRuns,
        };
        var title = workflow.Objective.Length > 120 ? workflow.Objective[..120] + "…" : workflow.Objective;
        var planningTask = new WorkTask
        {
            ProjectId = projectId, WorkflowExecutionId = workflow.Id, Title = $"Plan: {title}", Description = workflow.Objective,
            AcceptanceCriteria = "A plan of concrete, checkable tasks with skills and dependencies, approved by the CEO.", Priority = TaskPriority.High,
            Status = WorkTaskStatus.Assigned, PlanKey = "plan",
        };
        workflow.PlanningTaskId = planningTask.Id;
        db.WorkflowExecutions.Add(workflow);
        db.Tasks.Add(planningTask);
        db.TaskAssignments.Add(new TaskAssignment { TaskId = planningTask.Id, EmployeeId = plannerId, AssignedAt = now });
        Approvals.Event(db, workflow.Id, planningTask.Id, "started", $"Objective started; {planner.DisplayName} will plan it.", now);
        await db.SaveChangesAsync(ct);
        await notifier.TaskChangedAsync(projectId, planningTask.Id, ct);
        return await GetAsync(workflow.Id, ct);
    }

    public async Task<List<WorkflowDto>> ListAsync(Guid? projectId, CancellationToken ct)
    {
        var q = db.WorkflowExecutions.AsNoTracking();
        if (projectId is { } p) q = q.Where(w => w.ProjectId == p);
        var ids = await q.OrderByDescending(w => w.CreatedAt).Select(w => w.Id).Take(100).ToListAsync(ct);
        var list = new List<WorkflowDto>();
        foreach (var id in ids) list.Add(await GetAsync(id, ct));
        return list;
    }

    public async Task<WorkflowDto> GetAsync(Guid id, CancellationToken ct)
    {
        var w = await db.WorkflowExecutions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Workflow", id);
        var planner = await db.Employees.AsNoTracking().Where(e => e.Id == w.PlannerEmployeeId).Select(e => e.DisplayName).FirstAsync(ct);
        var tasks = await db.Tasks.AsNoTracking().Where(t => t.WorkflowExecutionId == id && t.Id != w.PlanningTaskId).Select(t => t.Status).ToListAsync(ct);
        var spent = await WorkflowEngine.SpentAsync(db, id, ct);
        return new WorkflowDto(w.Id, w.ProjectId, w.Objective, w.Status, w.PlannerEmployeeId, planner, w.PlanningTaskId, w.BudgetUsd, spent, w.MaxParallelRuns,
            tasks.Count(s => s != WorkTaskStatus.Cancelled), tasks.Count(s => s == WorkTaskStatus.Done), w.CreatedAt, w.CompletedAt, w.Error);
    }

    public async Task<List<WorkflowEventDto>> ListEventsAsync(Guid id, CancellationToken ct) =>
        await db.WorkflowEvents.AsNoTracking().Where(e => e.WorkflowExecutionId == id).OrderByDescending(e => e.Id).Take(300)
            .Select(e => new WorkflowEventDto(e.Id, e.TaskId, e.Kind, e.Message, e.CreatedAt)).ToListAsync(ct);

    /// <summary>Stops everything: active runs are cancelled, open tasks and pending approvals are closed.</summary>
    public async Task<WorkflowDto> CancelAsync(Guid id, CancellationToken ct)
    {
        var w = await db.WorkflowExecutions.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Workflow", id);
        if (!w.IsOpen) return await GetAsync(id, ct);
        var now = clock.GetUtcNow();
        w.Status = WorkflowStatus.Cancelled;
        w.CompletedAt = now;

        var taskIds = await db.Tasks.Where(t => t.WorkflowExecutionId == id).Select(t => t.Id).ToListAsync(ct);
        foreach (var task in await db.Tasks.Where(t => t.WorkflowExecutionId == id && t.Status != WorkTaskStatus.Done && t.Status != WorkTaskStatus.Cancelled).ToListAsync(ct))
            task.Status = WorkTaskStatus.Cancelled;
        foreach (var a in await db.ApprovalRequests.Where(a => a.WorkflowExecutionId == id && a.Status == ApprovalStatus.Pending).ToListAsync(ct))
            a.Status = ApprovalStatus.Cancelled;
        Approvals.Event(db, id, null, "cancelled", $"Cancelled by {current.Name ?? "a user"}.", now);
        await db.SaveChangesAsync(ct);

        foreach (var runId in await db.AgentRuns.Where(r => taskIds.Contains(r.TaskId) && (r.Status == AgentRunStatus.Queued || r.Status == AgentRunStatus.Running)).Select(r => r.Id).ToListAsync(ct))
            await runs.CancelAsync(runId, ct);
        await notifier.ApprovalsChangedAsync(ct);
        return await GetAsync(id, ct);
    }
}
