using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Application.Agents;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Work;
using OfficeAI.Domain.Workflows;

namespace OfficeAI.Application.Workflows;

public static class Approvals
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(7);

    public static ApprovalRequest Create(ApprovalKind kind, string title, string? details, object payload, DateTimeOffset now,
        Guid? projectId = null, Guid? taskId = null, Guid? workflowId = null, Guid? runId = null, Guid? requestedByEmployee = null, Guid? requestedByUser = null)
    {
        var json = JsonSerializer.Serialize(payload);
        return new ApprovalRequest
        {
            Kind = kind, Title = title.Length > 300 ? title[..300] : title, Details = details, PayloadJson = json, PayloadHash = ApprovalRequest.Hash(json),
            ProjectId = projectId, TaskId = taskId, WorkflowExecutionId = workflowId, RunId = runId,
            RequestedByEmployeeId = requestedByEmployee, RequestedByUserId = requestedByUser, ExpiresAt = now + DefaultLifetime,
        };
    }

    public static void Event(IAppDbContext db, Guid workflowId, Guid? taskId, string kind, string message, DateTimeOffset now) =>
        db.WorkflowEvents.Add(new WorkflowEvent { WorkflowExecutionId = workflowId, TaskId = taskId, Kind = kind, Message = message, CreatedAt = now });
}

/// <summary>The planner hands in its plan. The CEO must approve it before any task is created.</summary>
public sealed class PlanSubmitTool : IAgentTool
{
    public const string ToolId = "plan.submit";

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0",
        "Submit the plan for the objective: a list of tasks with a short key, title, description, acceptance criteria, required skills, " +
        "the keys of tasks each one depends on, and a priority (Low, Medium, High, Critical). Keep tasks concrete and independently checkable.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                summary = new { type = "string", maxLength = 4000 },
                tasks = new
                {
                    type = "array",
                    maxItems = PlanParser.MaxItems,
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            key = new { type = "string" },
                            title = new { type = "string" },
                            description = new { type = "string" },
                            acceptanceCriteria = new { type = "string" },
                            requiredSkills = new { type = "array", items = new { type = "string" } },
                            dependsOn = new { type = "array", items = new { type = "string" } },
                            priority = new { type = "string", @enum = new[] { "Low", "Medium", "High", "Critical" } },
                        },
                        required = new[] { "key", "title" },
                    },
                },
            },
            required = new[] { "summary", "tasks" },
            additionalProperties = false,
        }),
        TimeSpan.FromSeconds(15), 4_000, RequiresApproval: false, HasSideEffects: true, Purposes: [RunPurpose.Plan]);

    public async Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct)
    {
        var workflow = await ctx.Db.WorkflowExecutions.FirstOrDefaultAsync(w => w.PlanningTaskId == ctx.TaskId, ct);
        if (workflow is null) return ToolResult.Fail("This task is not a planning task.");
        if (workflow.Status != WorkflowStatus.Planning) return ToolResult.Fail($"The workflow is {workflow.Status}; a plan cannot be submitted now.");

        var (items, error) = PlanParser.Parse(input.GetProperty("tasks"));
        if (items is null) return ToolResult.Fail(error!);
        var summary = input.GetProperty("summary").GetString() ?? "";
        var now = ctx.Clock.GetUtcNow();

        workflow.PlanJson = PlanParser.Serialize(items);
        workflow.Status = WorkflowStatus.AwaitingPlanApproval;

        var markdown = new StringBuilder($"## Plan\n\n{summary}\n\n");
        foreach (var i in items)
            markdown.AppendLine($"- **{i.Key}** — {i.Title} ({i.Priority}){(i.DependsOn.Count > 0 ? $" ← {string.Join(", ", i.DependsOn)}" : "")}");
        ctx.Db.Deliverables.Add(new Deliverable
        {
            TaskId = ctx.TaskId, EmployeeId = ctx.EmployeeId, RunId = ctx.RunId, Title = "Plan", Content = markdown.ToString(), IsSimulated = ctx.IsSimulated,
        });

        var task = await ctx.Db.Tasks.FirstAsync(t => t.Id == ctx.TaskId, ct);
        if (TaskStateMachine.CanTransition(task.Status, WorkTaskStatus.InReview)) task.Status = WorkTaskStatus.InReview;

        ctx.Db.ApprovalRequests.Add(Approvals.Create(ApprovalKind.Plan, $"Approve plan: {workflow.Objective}", summary,
            new { workflowId = workflow.Id, summary, tasks = items }, now,
            projectId: workflow.ProjectId, taskId: ctx.TaskId, workflowId: workflow.Id, runId: ctx.RunId, requestedByEmployee: ctx.EmployeeId));
        Approvals.Event(ctx.Db, workflow.Id, ctx.TaskId, "plan_submitted", $"Plan with {items.Count} tasks submitted for approval.", now);
        await ctx.Db.SaveChangesAsync(ct);
        return ToolResult.Ok(new { status = "AwaitingApproval", tasks = items.Count });
    }
}

/// <summary>A reviewer records its verdict on the latest deliverable of the task under review.</summary>
public sealed class ReviewSubmitTool : IAgentTool
{
    public const string ToolId = "review.submit";

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0",
        "Record your review of the task's latest deliverable. outcome is 'approve' when it meets every acceptance criterion, " +
        "otherwise 'changes_requested' with specific, actionable comments.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                outcome = new { type = "string", @enum = new[] { "approve", "changes_requested" } },
                comments = new { type = "string", maxLength = 8000 },
            },
            required = new[] { "outcome", "comments" },
            additionalProperties = false,
        }),
        TimeSpan.FromSeconds(15), 2_000, RequiresApproval: false, HasSideEffects: true, Purposes: [RunPurpose.Review]);

    public async Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct)
    {
        var outcomeText = input.GetProperty("outcome").GetString();
        var comments = input.GetProperty("comments").GetString() ?? "";
        if (outcomeText is not ("approve" or "changes_requested")) return ToolResult.Fail("outcome must be 'approve' or 'changes_requested'.");
        if (string.IsNullOrWhiteSpace(comments)) return ToolResult.Fail("comments must not be empty.");

        var task = await ctx.Db.Tasks.FirstAsync(t => t.Id == ctx.TaskId, ct);
        if (task.Status != WorkTaskStatus.InReview) return ToolResult.Fail($"The task is {task.Status}, not in review.");
        var deliverable = await ctx.Db.Deliverables.Where(d => d.TaskId == task.Id).OrderByDescending(d => d.CreatedAt).FirstOrDefaultAsync(ct);
        if (deliverable is null) return ToolResult.Fail("There is no deliverable to review.");

        var outcome = outcomeText == "approve" ? ReviewOutcome.Approved : ReviewOutcome.ChangesRequested;
        ctx.Db.Reviews.Add(new Review
        {
            TaskId = task.Id, DeliverableId = deliverable.Id, ReviewerEmployeeId = ctx.EmployeeId, RunId = ctx.RunId, Outcome = outcome,
            Comments = comments, IsSimulated = ctx.IsSimulated,
        });
        var now = ctx.Clock.GetUtcNow();
        if (outcome == ReviewOutcome.Approved)
        {
            deliverable.Status = DeliverableStatus.Accepted;
            task.Status = WorkTaskStatus.WaitingApproval;
            ctx.Db.ApprovalRequests.Add(Approvals.Create(ApprovalKind.TaskCompletion, $"Approve completion: {task.Title}", comments,
                new { taskId = task.Id, deliverableId = deliverable.Id, deliverable.Title, review = comments }, now,
                projectId: task.ProjectId, taskId: task.Id, workflowId: task.WorkflowExecutionId, runId: ctx.RunId, requestedByEmployee: ctx.EmployeeId));
        }
        else
        {
            deliverable.Status = DeliverableStatus.Rejected;
            task.Status = WorkTaskStatus.InProgress;
        }
        if (task.WorkflowExecutionId is { } wf)
            Approvals.Event(ctx.Db, wf, task.Id, "review", outcome == ReviewOutcome.Approved ? "Review approved; waiting for human approval." : "Reviewer requested changes.", now);
        await ctx.Db.SaveChangesAsync(ct);
        return ToolResult.Ok(new { outcome = outcome.ToString(), taskStatus = task.Status.ToString() });
    }
}

/// <summary>Split off a sub-task for a colleague. The current task waits for it; depth is limited.</summary>
public sealed class DelegateTaskTool : IAgentTool
{
    public const string ToolId = "task.delegate";
    public const int MaxChildrenPerTask = 5;

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0",
        "Create a sub-task for a colleague with different skills. Your task waits until the sub-task is done; then you continue with its deliverable. " +
        "Use only for genuinely separate work.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                title = new { type = "string", maxLength = 200 },
                description = new { type = "string", maxLength = 4000 },
                acceptanceCriteria = new { type = "string", maxLength = 4000 },
                requiredSkills = new { type = "string", description = "Comma-separated skills", maxLength = 300 },
            },
            required = new[] { "title", "description" },
            additionalProperties = false,
        }),
        TimeSpan.FromSeconds(15), 2_000, RequiresApproval: false, HasSideEffects: true);

    public async Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct)
    {
        var parent = await ctx.Db.Tasks.AsNoTracking().FirstAsync(t => t.Id == ctx.TaskId, ct);
        if (parent.WorkflowExecutionId is not { } workflowId) return ToolResult.Fail("Only tasks that belong to a workflow can be delegated.");
        var workflow = await ctx.Db.WorkflowExecutions.AsNoTracking().FirstAsync(w => w.Id == workflowId, ct);
        var definition = await ctx.Db.WorkflowDefinitions.AsNoTracking().FirstAsync(d => d.Id == workflow.DefinitionId, ct);
        if (parent.DelegationDepth + 1 > definition.MaxDelegationDepth)
            return ToolResult.Fail($"Delegation depth limit ({definition.MaxDelegationDepth}) reached. Do this part yourself or report blocked.");
        if (await ctx.Db.Tasks.CountAsync(t => t.ParentTaskId == parent.Id, ct) >= MaxChildrenPerTask)
            return ToolResult.Fail($"A task can have at most {MaxChildrenPerTask} sub-tasks.");

        var skills = (input.TryGetProperty("requiredSkills", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString()! : "")
            .Split(',').Select(x => x.Trim().ToLowerInvariant()).Where(x => x.Length > 0).Distinct().Take(10).ToList();
        var child = new WorkTask
        {
            ProjectId = parent.ProjectId, WorkflowExecutionId = workflowId, ParentTaskId = parent.Id, DelegationDepth = parent.DelegationDepth + 1,
            Title = input.GetProperty("title").GetString()!.Trim(), Description = input.GetProperty("description").GetString(),
            AcceptanceCriteria = input.TryGetProperty("acceptanceCriteria", out var a) ? a.GetString() : null,
            Priority = parent.Priority, RequiredSkills = skills,
        };
        ctx.Db.Tasks.Add(child);
        ctx.Db.TaskDependencies.Add(new TaskDependency { TaskId = parent.Id, DependsOnTaskId = child.Id, CreatedAt = ctx.Clock.GetUtcNow() });
        Approvals.Event(ctx.Db, workflowId, child.Id, "delegated", $"'{parent.Title}' delegated '{child.Title}'.", ctx.Clock.GetUtcNow());
        await ctx.Db.SaveChangesAsync(ct);
        return ToolResult.Ok(new { subTaskId = child.Id, note = "Sub-task created. End your turn; you will be resumed when it is done." });
    }
}
