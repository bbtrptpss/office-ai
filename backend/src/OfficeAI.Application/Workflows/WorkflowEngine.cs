using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OfficeAI.Application.Abstractions;
using OfficeAI.Application.Agents;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Organization;
using OfficeAI.Domain.Work;
using OfficeAI.Domain.Workflows;

namespace OfficeAI.Application.Workflows;

/// <summary>
/// Deterministic orchestrator. Each tick re-reads all state from the database, so it can stop and resume at any time.
/// It never decides for a human: plans, completions, escalations and approval-gated tool actions go to the inbox.
/// </summary>
public sealed class WorkflowEngine(
    IAppDbContext db,
    AgentRunService runs,
    AgentSelector selector,
    WorkService work,
    ToolRegistry tools,
    IRealtimeNotifier notifier,
    TimeProvider clock,
    ILogger<WorkflowEngine> log)
{
    /// <summary>Stop reasons that a retry will not fix.</summary>
    private static readonly HashSet<string> NonRetryable = ["budget", "refusal", "precondition", "cancelled"];

    public static async Task<decimal> SpentAsync(IAppDbContext db, Guid workflowId, CancellationToken ct) =>
        await db.CostRecords.Where(c => c.Kind == CostKind.Actual &&
                db.AgentRuns.Any(r => r.Id == c.RunId && db.Tasks.Any(t => t.Id == r.TaskId && t.WorkflowExecutionId == workflowId)))
            .SumAsync(c => (decimal?)c.AmountUsd, ct) ?? 0m;

    /// <summary>One pass over every open workflow. Returns false if another engine instance holds the lock.</summary>
    public async Task<bool> TickAsync(CancellationToken ct)
    {
        if (!await db.TryAcquireEngineLockAsync(ct))
        {
            Telemetry.EngineTicks.Add(1, new KeyValuePair<string, object?>("outcome", "locked"));
            return false;
        }
        using var activity = Telemetry.Source.StartActivity("workflow.tick");
        Telemetry.EngineTicks.Add(1, new KeyValuePair<string, object?>("outcome", "ran"));
        try
        {
            using var acting = ActingAs.Use(new ActingIdentity(null, "Workflow engine", IsAgent: false, "engine"));
            await ExpireApprovalsAsync(ct);
            await ExecuteApprovedToolActionsAsync(ct);
            var open = await db.WorkflowExecutions.AsNoTracking()
                .Where(w => w.Status == WorkflowStatus.Planning || w.Status == WorkflowStatus.Executing)
                .OrderBy(w => w.CreatedAt).Select(w => w.Id).ToListAsync(ct);
            foreach (var id in open)
            {
                try
                {
                    db.ResetTracking();
                    await AdvanceAsync(id, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogError(ex, "Workflow {WorkflowId} tick failed", id);
                }
            }
            return true;
        }
        finally
        {
            await db.ReleaseEngineLockAsync();
        }
    }

    private async Task AdvanceAsync(Guid workflowId, CancellationToken ct)
    {
        var w = await db.WorkflowExecutions.FirstAsync(x => x.Id == workflowId, ct);
        var definition = await db.WorkflowDefinitions.AsNoTracking().FirstAsync(d => d.Id == w.DefinitionId, ct);
        var now = clock.GetUtcNow();

        if (w.Status == WorkflowStatus.Planning)
        {
            await AdvanceTaskAsync(w, definition, w.PlanningTaskId!.Value, RunPurpose.Plan, ct);
            return;
        }

        var spent = await SpentAsync(db, w.Id, ct);
        var overBudget = spent >= w.BudgetUsd;
        if (overBudget && !await db.ApprovalRequests.AnyAsync(a => a.WorkflowExecutionId == w.Id && a.Kind == ApprovalKind.Escalation && a.TaskId == null && a.Status == ApprovalStatus.Pending, ct)
            && !await db.WorkflowEvents.AnyAsync(e => e.WorkflowExecutionId == w.Id && e.Kind == "budget_reached", ct))
        {
            db.ApprovalRequests.Add(Approvals.Create(ApprovalKind.Escalation, $"Budget reached: {w.Objective}",
                $"Spent {spent} of {w.BudgetUsd} USD. Raise the workflow budget to continue.", new { workflowId = w.Id, spent, budget = w.BudgetUsd }, now,
                projectId: w.ProjectId, workflowId: w.Id));
            Approvals.Event(db, w.Id, null, "budget_reached", $"Budget of {w.BudgetUsd} USD reached; no new runs start.", now);
            await db.SaveChangesAsync(ct);
            await notifier.ApprovalsChangedAsync(ct);
        }

        var tasks = await db.Tasks.AsNoTracking().Where(t => t.WorkflowExecutionId == w.Id && t.Id != w.PlanningTaskId)
            .OrderByDescending(t => t.Priority).ThenBy(t => t.CreatedAt).ToListAsync(ct);
        if (tasks.Count > 0 && tasks.All(t => t.Status is WorkTaskStatus.Done or WorkTaskStatus.Cancelled))
        {
            w.Status = tasks.Any(t => t.Status == WorkTaskStatus.Done) ? WorkflowStatus.Completed : WorkflowStatus.Failed;
            w.CompletedAt = now;
            if (w.Status == WorkflowStatus.Failed) w.Error = "Every task was cancelled.";
            Approvals.Event(db, w.Id, null, w.Status == WorkflowStatus.Completed ? "completed" : "failed",
                w.Status == WorkflowStatus.Completed ? "All tasks are done." : "Every task was cancelled.", now);
            await db.SaveChangesAsync(ct);
            return;
        }
        if (overBudget) return;

        foreach (var task in tasks)
        {
            if (task.Status is WorkTaskStatus.Done or WorkTaskStatus.Cancelled) continue;
            await AdvanceTaskAsync(w, definition, task.Id, task.Status == WorkTaskStatus.InReview ? RunPurpose.Review : RunPurpose.Work, ct);
        }
    }

    private async Task<int> ActiveRunsAsync(Guid workflowId, CancellationToken ct) =>
        await db.AgentRuns.CountAsync(r => (r.Status == AgentRunStatus.Queued || r.Status == AgentRunStatus.Running) &&
            db.Tasks.Any(t => t.Id == r.TaskId && t.WorkflowExecutionId == workflowId), ct);

    private async Task AdvanceTaskAsync(WorkflowExecution w, WorkflowDefinition def, Guid taskId, RunPurpose purpose, CancellationToken ct)
    {
        var task = await db.Tasks.AsNoTracking().FirstAsync(t => t.Id == taskId, ct);
        var now = clock.GetUtcNow();
        var assignees = await db.TaskAssignments.AsNoTracking().Where(a => a.TaskId == task.Id && a.UnassignedAt == null).Select(a => a.EmployeeId).ToListAsync(ct);

        switch (task.Status)
        {
            case WorkTaskStatus.Blocked:
                await EscalateOnceAsync(w, task, "The task is blocked.", "Blocked", ct);
                return;
            case WorkTaskStatus.WaitingApproval:
                if (!await db.ApprovalRequests.AnyAsync(a => a.TaskId == task.Id && a.Kind == ApprovalKind.TaskCompletion && a.Status == ApprovalStatus.Pending, ct))
                    await RequestCompletionApprovalAsync(w, task, "Waiting for final approval.", ct);
                return;
            case WorkTaskStatus.Failed:
                await EscalateOnceAsync(w, task, "The task failed.", "Failed", ct);
                return;
        }

        // Dependencies first.
        var openDeps = await db.TaskDependencies.AsNoTracking().Where(d => d.TaskId == task.Id)
            .Join(db.Tasks, d => d.DependsOnTaskId, t => t.Id, (d, t) => t.Status).CountAsync(s => s != WorkTaskStatus.Done, ct);
        if (openDeps > 0 && purpose != RunPurpose.Review) return;

        if (task.Status == WorkTaskStatus.Todo)
        {
            var choice = await selector.ChooseWorkerAsync(task, ct);
            if (choice is null)
            {
                await EscalateOnceAsync(w, task, "No AI employee can take this task (skills, budget or provider).", "NoAgent", ct);
                return;
            }
            await work.AssignAsync(task.Id, new AssignRequest(choice.EmployeeId), ct);
            Approvals.Event(db, w.Id, task.Id, "assigned", $"'{task.Title}' assigned to {choice.DisplayName} ({choice.Reason}).", now);
            await db.SaveChangesAsync(ct);
            task = await db.Tasks.AsNoTracking().FirstAsync(t => t.Id == taskId, ct);
            assignees = [choice.EmployeeId];
        }

        Guid employeeId;
        if (purpose == RunPurpose.Review)
        {
            if (task.ReviewerEmployeeId is null)
            {
                var reviewer = def.RequireAiReview ? await selector.ChooseReviewerAsync(task, assignees, ct) : null;
                if (reviewer is null)
                {
                    // No AI reviewer: a human reviews and approves directly.
                    await MoveToWaitingApprovalAsync(w, task, "No AI reviewer available; sent straight to human approval.", ct);
                    return;
                }
                var tracked = await db.Tasks.FirstAsync(t => t.Id == task.Id, ct);
                tracked.ReviewerEmployeeId = reviewer.EmployeeId;
                Approvals.Event(db, w.Id, task.Id, "reviewer", $"{reviewer.DisplayName} will review '{task.Title}' ({reviewer.Reason}).", now);
                await db.SaveChangesAsync(ct);
                task = await db.Tasks.AsNoTracking().FirstAsync(t => t.Id == taskId, ct);
            }
            employeeId = task.ReviewerEmployeeId!.Value;
        }
        else
        {
            var ai = await db.Employees.AsNoTracking().Where(e => assignees.Contains(e.Id) && e.Kind == EmployeeKind.AI).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
            if (ai is null) return; // a person is doing this one
            employeeId = ai.Value;
        }

        // At most one active run per task; respect the workflow's parallel limit.
        var history = await db.AgentRuns.AsNoTracking().Where(r => r.TaskId == task.Id && r.Purpose == purpose)
            .OrderByDescending(r => r.CreatedAt).Select(r => new { r.Status, r.StopReason, r.CreatedAt }).ToListAsync(ct);
        if (await db.AgentRuns.AnyAsync(r => r.TaskId == task.Id && (r.Status == AgentRunStatus.Queued || r.Status == AgentRunStatus.Running), ct)) return;

        // Count only runs since the last human "try again".
        var since = await db.ApprovalRequests.AsNoTracking()
            .Where(a => a.TaskId == task.Id && a.Kind == ApprovalKind.Escalation && a.Status == ApprovalStatus.Approved)
            .MaxAsync(a => (DateTimeOffset?)a.DecidedAt, ct) ?? DateTimeOffset.MinValue;
        var recent = history.Where(h => h.CreatedAt > since).ToList();
        var last = recent.FirstOrDefault();

        if (last is not null && last.Status == AgentRunStatus.Cancelled) return; // a person stopped it; leave it to them
        if (purpose == RunPurpose.Review && last is { Status: AgentRunStatus.Succeeded })
        {
            // The reviewer finished without a verdict: hand over to a human rather than looping.
            var tracked = await db.Tasks.AsNoTracking().FirstAsync(t => t.Id == task.Id, ct);
            if (tracked.Status == WorkTaskStatus.InReview && !await LatestDeliverableIsNewerThanAsync(task.Id, last.CreatedAt, ct))
            {
                await MoveToWaitingApprovalAsync(w, task, "The reviewer gave no verdict; sent to human approval.", ct);
                return;
            }
        }
        if (last is { Status: AgentRunStatus.Failed or AgentRunStatus.LimitReached })
        {
            var failures = recent.TakeWhile(h => h.Status is AgentRunStatus.Failed or AgentRunStatus.LimitReached).Count();
            if (NonRetryable.Contains(last.StopReason ?? "") || failures > def.MaxRetriesPerTask)
            {
                if (purpose == RunPurpose.Review)
                {
                    await MoveToWaitingApprovalAsync(w, task, "AI review failed; sent to human approval.", ct);
                    return;
                }
                await EscalateOnceAsync(w, task, $"Runs keep failing ({last.StopReason}). Retry or cancel?", "RunsFailed", ct);
                return;
            }
        }
        if (recent.Count >= def.MaxRunsPerTask)
        {
            await EscalateOnceAsync(w, task, $"{recent.Count} runs without finishing. Retry or cancel?", "TooManyRuns", ct);
            return;
        }
        if (await ActiveRunsAsync(w.Id, ct) >= w.MaxParallelRuns) return;

        try
        {
            var run = await runs.CreateRunAsync(task.Id, employeeId, purpose, ct);
            Approvals.Event(db, w.Id, task.Id, "run_started", $"{purpose} run #{run.Attempt} started for '{task.Title}'.", now);
            await db.SaveChangesAsync(ct);
        }
        catch (DomainException ex)
        {
            await EscalateOnceAsync(w, task, ex.Message, ex.Code, ct);
        }
    }

    private async Task<bool> LatestDeliverableIsNewerThanAsync(Guid taskId, DateTimeOffset when, CancellationToken ct) =>
        await db.Deliverables.AnyAsync(d => d.TaskId == taskId && d.CreatedAt > when, ct);

    private async Task MoveToWaitingApprovalAsync(WorkflowExecution w, WorkTask task, string reason, CancellationToken ct)
    {
        var tracked = await db.Tasks.FirstAsync(t => t.Id == task.Id, ct);
        if (!TaskStateMachine.CanTransition(tracked.Status, WorkTaskStatus.WaitingApproval)) return;
        tracked.Status = WorkTaskStatus.WaitingApproval;
        await RequestCompletionApprovalAsync(w, tracked, reason, ct);
    }

    private async Task RequestCompletionApprovalAsync(WorkflowExecution w, WorkTask task, string reason, CancellationToken ct)
    {
        var deliverable = await db.Deliverables.AsNoTracking().Where(d => d.TaskId == task.Id).OrderByDescending(d => d.CreatedAt)
            .Select(d => new { d.Id, d.Title }).FirstOrDefaultAsync(ct);
        db.ApprovalRequests.Add(Approvals.Create(ApprovalKind.TaskCompletion, $"Approve completion: {task.Title}", reason,
            new { taskId = task.Id, deliverableId = deliverable?.Id, deliverable?.Title, review = reason }, clock.GetUtcNow(),
            projectId: task.ProjectId, taskId: task.Id, workflowId: w.Id));
        Approvals.Event(db, w.Id, task.Id, "approval_requested", reason, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        await notifier.TaskChangedAsync(task.ProjectId, task.Id, ct);
        await notifier.ApprovalsChangedAsync(ct);
    }

    /// <summary>Asks a human once per situation; a later "try again" decision re-arms it.</summary>
    private async Task EscalateOnceAsync(WorkflowExecution w, WorkTask task, string reason, string code, CancellationToken ct)
    {
        var pendingOrRecent = await db.ApprovalRequests.AsNoTracking()
            .Where(a => a.TaskId == task.Id && a.Kind == ApprovalKind.Escalation)
            .OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync(ct);
        if (pendingOrRecent is { Status: ApprovalStatus.Pending }) return;
        if (pendingOrRecent is not null && pendingOrRecent.Details == reason && pendingOrRecent.Status != ApprovalStatus.Approved) return;

        var tracked = await db.Tasks.FirstAsync(t => t.Id == task.Id, ct);
        if (TaskStateMachine.CanTransition(tracked.Status, WorkTaskStatus.Blocked)) tracked.Status = WorkTaskStatus.Blocked;
        db.ApprovalRequests.Add(Approvals.Create(ApprovalKind.Escalation, $"Needs a decision: {task.Title}", reason,
            new { taskId = task.Id, code, reason }, clock.GetUtcNow(), projectId: task.ProjectId, taskId: task.Id, workflowId: w.Id));
        Approvals.Event(db, w.Id, task.Id, "escalated", $"'{task.Title}': {reason}", clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        await notifier.TaskChangedAsync(task.ProjectId, task.Id, ct);
        await notifier.ApprovalsChangedAsync(ct);
    }

    private async Task ExpireApprovalsAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var expired = await db.ApprovalRequests.Where(a => a.Status == ApprovalStatus.Pending && a.ExpiresAt <= now).ToListAsync(ct);
        if (expired.Count == 0) return;
        foreach (var a in expired)
        {
            a.Status = ApprovalStatus.Expired;
            if (a.WorkflowExecutionId is { } wf) Approvals.Event(db, wf, a.TaskId, "approval_expired", $"Approval expired: {a.Title}", now);
            // Re-ask for decisions the work cannot continue without; expired tool actions simply never run.
            if (a.Kind is ApprovalKind.Plan or ApprovalKind.TaskCompletion or ApprovalKind.Escalation)
                db.ApprovalRequests.Add(Approvals.Create(a.Kind, a.Title, a.Details, JsonSerializer.Deserialize<JsonElement>(a.PayloadJson), now,
                    a.ProjectId, a.TaskId, a.WorkflowExecutionId, a.RunId, a.RequestedByEmployeeId, a.RequestedByUserId));
        }
        await db.SaveChangesAsync(ct);
        await notifier.ApprovalsChangedAsync(ct);
    }

    /// <summary>Runs approved tool actions exactly once, attributed to the agent that asked for them.</summary>
    private async Task ExecuteApprovedToolActionsAsync(CancellationToken ct)
    {
        var approved = await db.ApprovalRequests.Where(a => a.Kind == ApprovalKind.ToolAction && a.Status == ApprovalStatus.Approved && a.ExecutedAt == null)
            .ToListAsync(ct);
        foreach (var a in approved)
        {
            a.ExecutedAt = clock.GetUtcNow(); // claim first: never run twice, even if execution fails
            await db.SaveChangesAsync(ct);

            var payload = JsonDocument.Parse(a.PayloadJson).RootElement;
            var toolId = payload.GetProperty("toolId").GetString()!;
            var input = payload.GetProperty("input");
            var tool = tools.Find(toolId);
            var run = a.RunId is { } runId ? await db.AgentRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct) : null;
            var exec = new ToolExecution
            {
                RunId = a.RunId ?? Guid.Empty, ToolId = toolId, ToolVersion = tool?.Descriptor.Version ?? "-", ToolCallId = $"approval:{a.Id:N}",
                IdempotencyKey = $"approval:{a.Id:N}", InputJson = input.GetRawText(), StartedAt = clock.GetUtcNow(),
            };
            if (tool is null || run is null)
            {
                (exec.Status, exec.Error) = (ToolExecutionStatus.Failed, "Tool or originating run no longer exists.");
            }
            else
            {
                var employee = await db.Employees.AsNoTracking().FirstAsync(e => e.Id == run.EmployeeId, ct);
                using var acting = ActingAs.Use(new ActingIdentity(employee.UserId, employee.DisplayName, IsAgent: true, $"approval:{a.Id:N}"));
                var sw = Stopwatch.StartNew();
                try
                {
                    var result = await tool.ExecuteAsync(new ToolContext(run.Id, run.TaskId, run.ProjectId, run.EmployeeId, run.IsSimulated, db, clock, run.Purpose), input, ct);
                    exec.Status = result.Success ? ToolExecutionStatus.Succeeded : ToolExecutionStatus.Failed;
                    if (result.Success) exec.Output = result.Output; else exec.Error = result.Output;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogWarning(ex, "Approved tool action {ApprovalId} failed", a.Id);
                    (exec.Status, exec.Error) = (ToolExecutionStatus.Failed, "The tool failed with an internal error.");
                }
                exec.DurationMs = (int)sw.ElapsedMilliseconds;
            }
            db.ResetTracking();
            if (exec.RunId != Guid.Empty) db.ToolExecutions.Add(exec);
            await db.SaveChangesAsync(ct);
        }
    }
}
