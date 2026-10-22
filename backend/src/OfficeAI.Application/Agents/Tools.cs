using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Work;

namespace OfficeAI.Application.Agents;

/// <summary>Everything a tool may know about the call: who is running, on which task, in which run.</summary>
public sealed record ToolContext(Guid RunId, Guid TaskId, Guid ProjectId, Guid EmployeeId, bool IsSimulated, IAppDbContext Db, TimeProvider Clock, RunPurpose Purpose = RunPurpose.Work);

public sealed record ToolResult(bool Success, string Output)
{
    public static ToolResult Ok(object value) => new(true, JsonSerializer.Serialize(value, ToolJson.Options));
    public static ToolResult Fail(string message) => new(false, message);
}

/// <summary>Declared contract of a tool. Enforced by the runner, never by the model.</summary>
public sealed record ToolDescriptor(
    string Id,
    string Version,
    string Description,
    JsonElement InputSchema,
    TimeSpan Timeout,
    int MaxOutputChars,
    bool RequiresApproval,
    bool HasSideEffects,
    RunPurpose[]? Purposes = null)
{
    /// <summary>Kinds of run that may use this tool. Default: work runs only.</summary>
    public bool AllowedFor(RunPurpose purpose) => (Purposes ?? [RunPurpose.Work]).Contains(purpose);
}

public interface IAgentTool
{
    ToolDescriptor Descriptor { get; }
    Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct);
}

public sealed class ToolRegistry(IEnumerable<IAgentTool> tools)
{
    private readonly Dictionary<string, IAgentTool> _tools = tools.ToDictionary(t => t.Descriptor.Id);

    public IReadOnlyCollection<IAgentTool> All => _tools.Values;
    public IAgentTool? Find(string id) => _tools.GetValueOrDefault(id);

    /// <summary>Default grants for every new AI employee: read their own work context and hand in results.</summary>
    public static readonly string[] DefaultGrants =
        [TaskContextTool.ToolId, ProjectTasksTool.ToolId, FindEmployeesTool.ToolId, SubmitDeliverableTool.ToolId, RequestReviewTool.ToolId, ReportBlockedTool.ToolId, Workflows.ReviewSubmitTool.ToolId];
}

internal static class ToolJson
{
    // Tool output goes to the model and is shown as plain text, never as HTML: keep Thai and other scripts readable.
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static JsonElement Schema(object schema) => JsonSerializer.SerializeToElement(schema, Options);

    public static readonly JsonElement NoInput = Schema(new { type = "object", properties = new { }, additionalProperties = false });

    public static string? String(JsonElement input, string name) =>
        input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>Minimal JSON-schema check for tool input: object, required keys present, declared primitive types, no extra keys.</summary>
public static class ToolInputValidator
{
    public static string? Validate(JsonElement schema, JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object) return "Input must be a JSON object.";
        var props = schema.TryGetProperty("properties", out var p) ? p : default;

        if (schema.TryGetProperty("required", out var required))
            foreach (var r in required.EnumerateArray())
                if (!input.TryGetProperty(r.GetString()!, out _)) return $"Missing required field '{r.GetString()}'.";

        foreach (var field in input.EnumerateObject())
        {
            if (props.ValueKind != JsonValueKind.Object || !props.TryGetProperty(field.Name, out var def))
                return $"Unknown field '{field.Name}'.";
            if (!def.TryGetProperty("type", out var type)) continue;
            var ok = type.GetString() switch
            {
                "string" => field.Value.ValueKind == JsonValueKind.String,
                "integer" => field.Value.ValueKind == JsonValueKind.Number && field.Value.TryGetInt64(out _),
                "number" => field.Value.ValueKind == JsonValueKind.Number,
                "boolean" => field.Value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                _ => true,
            };
            if (!ok) return $"Field '{field.Name}' must be of type {type.GetString()}.";
            if (def.TryGetProperty("maxLength", out var max) && field.Value.ValueKind == JsonValueKind.String &&
                field.Value.GetString()!.Length > max.GetInt32())
                return $"Field '{field.Name}' is longer than {max.GetInt32()} characters.";
        }
        return null;
    }
}

// ---------------- Phase 2 tools: internal company data only ----------------

public sealed class TaskContextTool : IAgentTool
{
    public const string ToolId = "task.read_context";

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0",
        "Read the task you are assigned: title, description, acceptance criteria, priority, due date, dependencies and earlier deliverables.",
        ToolJson.NoInput, TimeSpan.FromSeconds(10), 40_000, RequiresApproval: false, HasSideEffects: false, Purposes: [RunPurpose.Work, RunPurpose.Review, RunPurpose.Plan]);

    public async Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct)
    {
        var task = await ctx.Db.Tasks.AsNoTracking().FirstAsync(t => t.Id == ctx.TaskId, ct);
        var project = await ctx.Db.Projects.AsNoTracking().Where(p => p.Id == task.ProjectId).Select(p => new { p.Code, p.Name, p.Description }).FirstAsync(ct);
        var deps = await ctx.Db.TaskDependencies.AsNoTracking().Where(d => d.TaskId == task.Id)
            .Join(ctx.Db.Tasks, d => d.DependsOnTaskId, t => t.Id, (d, t) => new { t.Title, Status = t.Status.ToString() }).ToListAsync(ct);
        var deliverables = await ctx.Db.Deliverables.AsNoTracking().Where(d => d.TaskId == task.Id)
            .OrderBy(d => d.CreatedAt).Select(d => new { d.Title, Status = d.Status.ToString(), d.Content }).ToListAsync(ct);
        var reviews = await ctx.Db.Reviews.AsNoTracking().Where(r => r.TaskId == task.Id)
            .OrderBy(r => r.CreatedAt).Select(r => new { Outcome = r.Outcome.ToString(), r.Comments }).ToListAsync(ct);
        // Work handed in by the tasks this one waits for (e.g. delegated sub-tasks).
        var upstream = await ctx.Db.TaskDependencies.AsNoTracking().Where(d => d.TaskId == task.Id)
            .Join(ctx.Db.Deliverables, d => d.DependsOnTaskId, x => x.TaskId, (d, x) => new { x.TaskId, x.Title, x.Content, x.CreatedAt })
            .OrderBy(x => x.CreatedAt).Select(x => new { x.Title, x.Content }).ToListAsync(ct);
        return ToolResult.Ok(new
        {
            project,
            task = new { task.Title, task.Description, task.AcceptanceCriteria, Priority = task.Priority.ToString(), task.DueDate, Status = task.Status.ToString(), task.RequiredSkills },
            dependsOn = deps,
            previousDeliverables = deliverables,
            reviewFeedback = reviews,
            upstreamDeliverables = upstream,
        });
    }
}

public sealed class ProjectTasksTool : IAgentTool
{
    public const string ToolId = "project.list_tasks";

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0",
        "List the other tasks in your project with their status and assignee, to understand the surrounding work.",
        ToolJson.NoInput, TimeSpan.FromSeconds(10), 20_000, RequiresApproval: false, HasSideEffects: false, Purposes: [RunPurpose.Work, RunPurpose.Review, RunPurpose.Plan]);

    public async Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct)
    {
        var tasks = await ctx.Db.Tasks.AsNoTracking()
            .Where(t => t.ProjectId == ctx.ProjectId && t.Status != WorkTaskStatus.Cancelled)
            .OrderBy(t => t.CreatedAt)
            .Select(t => new
            {
                t.Title,
                Status = t.Status.ToString(),
                Assignee = ctx.Db.TaskAssignments.Where(a => a.TaskId == t.Id && a.UnassignedAt == null)
                    .Join(ctx.Db.Employees, a => a.EmployeeId, e => e.Id, (a, e) => e.DisplayName).FirstOrDefault(),
            })
            .Take(200).ToListAsync(ct);
        return ToolResult.Ok(tasks);
    }
}

public sealed class FindEmployeesTool : IAgentTool
{
    public const string ToolId = "org.find_employees";

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0",
        "Find colleagues by skill or job title, e.g. to name who should review your work.",
        ToolJson.Schema(new
        {
            type = "object",
            properties = new { query = new { type = "string", description = "Skill or title to search for", maxLength = 100 } },
            required = new[] { "query" },
            additionalProperties = false,
        }),
        TimeSpan.FromSeconds(10), 10_000, RequiresApproval: false, HasSideEffects: false, Purposes: [RunPurpose.Work, RunPurpose.Plan]);

    public async Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct)
    {
        var q = (ToolJson.String(input, "query") ?? "").Trim().ToLowerInvariant();
        var rows = await ctx.Db.Employees.AsNoTracking()
            .Select(e => new { e.DisplayName, Title = e.RoleDefinition!.Title, Department = e.Department!.Name, e.RoleDefinition.Skills, Kind = e.Kind.ToString() })
            .ToListAsync(ct);
        var hits = rows.Where(r => r.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Skills.Any(s => s.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .Take(20).ToList();
        return ToolResult.Ok(hits);
    }
}

public sealed class SubmitDeliverableTool : IAgentTool
{
    public const string ToolId = "deliverable.submit";

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0",
        "Hand in your work product for this task as Markdown. This is the only way work counts as delivered.",
        ToolJson.Schema(new
        {
            type = "object",
            properties = new
            {
                title = new { type = "string", maxLength = 200 },
                content = new { type = "string", description = "Markdown", maxLength = 100_000 },
            },
            required = new[] { "title", "content" },
            additionalProperties = false,
        }),
        TimeSpan.FromSeconds(15), 2_000, RequiresApproval: false, HasSideEffects: true);

    public async Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct)
    {
        var title = ToolJson.String(input, "title")!.Trim();
        var content = ToolJson.String(input, "content")!;
        if (title.Length == 0 || string.IsNullOrWhiteSpace(content)) return ToolResult.Fail("title and content must not be empty.");

        var deliverable = new Deliverable
        {
            TaskId = ctx.TaskId, EmployeeId = ctx.EmployeeId, RunId = ctx.RunId, Title = title, Content = content, IsSimulated = ctx.IsSimulated,
        };
        ctx.Db.Deliverables.Add(deliverable);
        await ctx.Db.SaveChangesAsync(ct);
        return ToolResult.Ok(new { deliverableId = deliverable.Id, status = "Submitted" });
    }
}

public sealed class RequestReviewTool : IAgentTool
{
    public const string ToolId = "task.request_review";

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0",
        "Move your task to review after you have submitted at least one deliverable. You cannot mark tasks as done yourself.",
        ToolJson.Schema(new
        {
            type = "object",
            properties = new { note = new { type = "string", description = "Short note for the reviewer", maxLength = 2000 } },
            additionalProperties = false,
        }),
        TimeSpan.FromSeconds(10), 2_000, RequiresApproval: false, HasSideEffects: true);

    public async Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct)
    {
        // A task that delegated work is only finished once that work is done; it is resumed then.
        var waitingFor = await ctx.Db.TaskDependencies.Where(d => d.TaskId == ctx.TaskId)
            .Join(ctx.Db.Tasks, d => d.DependsOnTaskId, t => t.Id, (_, t) => t)
            .Where(t => t.Status != WorkTaskStatus.Done && t.Status != WorkTaskStatus.Cancelled).Select(t => t.Title).ToListAsync(ct);
        if (waitingFor.Count > 0)
            return ToolResult.Fail($"Waiting for: {string.Join("; ", waitingFor)}. End your turn now; you will be resumed when they are done.");
        if (!await ctx.Db.Deliverables.AnyAsync(d => d.TaskId == ctx.TaskId && d.RunId == ctx.RunId, ct))
            return ToolResult.Fail("Submit a deliverable with deliverable.submit before requesting review.");
        var task = await ctx.Db.Tasks.FirstAsync(t => t.Id == ctx.TaskId, ct);
        if (task.Status == WorkTaskStatus.InReview) return ToolResult.Ok(new { status = "InReview" });
        if (!TaskStateMachine.CanTransition(task.Status, WorkTaskStatus.InReview))
            return ToolResult.Fail($"The task is {task.Status} and cannot move to review.");
        task.Status = WorkTaskStatus.InReview;
        await ctx.Db.SaveChangesAsync(ct);
        return ToolResult.Ok(new { status = "InReview" });
    }
}

public sealed class ReportBlockedTool : IAgentTool
{
    public const string ToolId = "task.report_blocked";

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0",
        "Report that you cannot continue (missing information, unclear requirement). The task moves to Blocked for a human to resolve.",
        ToolJson.Schema(new
        {
            type = "object",
            properties = new { reason = new { type = "string", maxLength = 2000 } },
            required = new[] { "reason" },
            additionalProperties = false,
        }),
        TimeSpan.FromSeconds(10), 2_000, RequiresApproval: false, HasSideEffects: true);

    public async Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct)
    {
        var task = await ctx.Db.Tasks.FirstAsync(t => t.Id == ctx.TaskId, ct);
        if (!TaskStateMachine.CanTransition(task.Status, WorkTaskStatus.Blocked))
            return ToolResult.Fail($"The task is {task.Status} and cannot be marked blocked.");
        task.Status = WorkTaskStatus.Blocked;
        await ctx.Db.SaveChangesAsync(ct);
        return ToolResult.Ok(new { status = "Blocked", reason = ToolJson.String(input, "reason") });
    }
}
