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
        [TaskContextTool.ToolId, ReadDeliverableTool.ToolId, ProjectTasksTool.ToolId, FindEmployeesTool.ToolId, SubmitDeliverableTool.ToolId, RequestReviewTool.ToolId, ReportBlockedTool.ToolId, Workflows.ReviewSubmitTool.ToolId];
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
        "Read the task you are assigned: title, description, acceptance criteria, priority, due date, dependencies, review feedback, " +
        "the latest deliverable of this task and the latest deliverable of each task it waits for. Long texts are cut; read the rest with task_read_deliverable.",
        ToolJson.NoInput, TimeSpan.FromSeconds(10), 60_000, RequiresApproval: false, HasSideEffects: false, Purposes: [RunPurpose.Work, RunPurpose.Review, RunPurpose.Plan]);

    /// <summary>
    /// Everything used to be sent oldest first and cut at the output limit, so on a task with long drafts the newest one, the one that mattered,
    /// was the part cut off. Now the newest comes first and in full up to a size, older versions are only listed, and the rest is one call away.
    /// </summary>
    private const int LatestChars = 30_000;
    private const int UpstreamChars = 6_000;

    public async Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct)
    {
        var task = await ctx.Db.Tasks.AsNoTracking().FirstAsync(t => t.Id == ctx.TaskId, ct);
        var project = await ctx.Db.Projects.AsNoTracking().Where(p => p.Id == task.ProjectId).Select(p => new { p.Code, p.Name, p.Description }).FirstAsync(ct);
        var deps = await ctx.Db.TaskDependencies.AsNoTracking().Where(d => d.TaskId == task.Id)
            .Join(ctx.Db.Tasks, d => d.DependsOnTaskId, t => t.Id, (d, t) => new { t.Title, Status = t.Status.ToString() }).ToListAsync(ct);
        var own = await ctx.Db.Deliverables.AsNoTracking().Where(d => d.TaskId == task.Id)
            .OrderByDescending(d => d.CreatedAt).Select(d => new { d.Id, d.Title, Status = d.Status.ToString(), d.CreatedAt, d.Content }).ToListAsync(ct);
        var reviews = await ctx.Db.Reviews.AsNoTracking().Where(r => r.TaskId == task.Id)
            .OrderByDescending(r => r.CreatedAt).Select(r => new { Outcome = r.Outcome.ToString(), r.Comments, r.CreatedAt }).ToListAsync(ct);
        // Work handed in by the tasks this one waits for (e.g. delegated sub-tasks): the newest from each.
        var upstreamAll = await ctx.Db.TaskDependencies.AsNoTracking().Where(d => d.TaskId == task.Id)
            .Join(ctx.Db.Deliverables, d => d.DependsOnTaskId, x => x.TaskId, (d, x) => new { x.Id, x.TaskId, x.Title, x.Content, x.CreatedAt })
            .ToListAsync(ct);
        var upstream = upstreamAll.GroupBy(x => x.TaskId).Select(g => g.OrderByDescending(x => x.CreatedAt).First()).OrderBy(x => x.CreatedAt)
            .Select(x => new { x.Id, x.Title, Content = Clip(x.Content, UpstreamChars, x.Id) }).ToList();
        var latest = own.FirstOrDefault();
        return ToolResult.Ok(new
        {
            project,
            task = new { task.Title, task.Description, task.AcceptanceCriteria, Priority = task.Priority.ToString(), task.DueDate, Status = task.Status.ToString(), task.RequiredSkills },
            dependsOn = deps,
            reviewFeedback = reviews.Select(r => new { r.Outcome, r.Comments }),
            latestDeliverable = latest is null ? null : new { latest.Id, latest.Title, latest.Status, Content = Clip(latest.Content, LatestChars, latest.Id) },
            earlierDeliverables = own.Skip(1).Select(d => new { d.Id, d.Title, d.Status, d.CreatedAt }),
            upstreamDeliverables = upstream,
        });
    }

    private static string Clip(string content, int max, Guid id) => content.Length <= max ? content
        : content[..max] + $"\n…[{content.Length - max} more characters: task_read_deliverable with id {id} and offset {max}]";
}

/// <summary>Reads a deliverable of this task, or of a task it waits for, in pieces.</summary>
public sealed class ReadDeliverableTool : IAgentTool
{
    public const string ToolId = "task.read_deliverable";
    private const int Piece = 30_000;

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0",
        "Read a deliverable of your task, or of a task it waits for, by its id (from task_read_context). Returns up to 30,000 characters from offset; " +
        "call again with the next offset to read on.",
        ToolJson.Schema(new { type = "object", properties = new { id = new { type = "string" }, offset = new { type = "integer", minimum = 0 } }, required = new[] { "id" }, additionalProperties = false }),
        TimeSpan.FromSeconds(10), 32_000, RequiresApproval: false, HasSideEffects: false, Purposes: [RunPurpose.Work, RunPurpose.Review, RunPurpose.Plan]);

    public async Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct)
    {
        if (!input.TryGetProperty("id", out var idEl) || !Guid.TryParse(idEl.GetString(), out var id)) return ToolResult.Fail("id must be a deliverable id.");
        var offset = input.TryGetProperty("offset", out var o) && o.TryGetInt32(out var n) ? Math.Max(0, n) : 0;
        var allowed = await ctx.Db.TaskDependencies.AsNoTracking().Where(d => d.TaskId == ctx.TaskId).Select(d => d.DependsOnTaskId).ToListAsync(ct);
        allowed.Add(ctx.TaskId);
        var d = await ctx.Db.Deliverables.AsNoTracking().Where(x => x.Id == id && allowed.Contains(x.TaskId)).Select(x => new { x.Title, x.Content }).FirstOrDefaultAsync(ct);
        if (d is null) return ToolResult.Fail("No such deliverable on this task or on a task it waits for.");
        if (offset >= d.Content.Length) return ToolResult.Fail($"offset is past the end ({d.Content.Length} characters).");
        var end = Math.Min(d.Content.Length, offset + Piece);
        var more = end < d.Content.Length ? $"\n…[{d.Content.Length - end} more characters: call again with offset {end}]" : "\n[end]";
        return new ToolResult(true, $"{d.Title} (characters {offset}-{end} of {d.Content.Length})\n{d.Content[offset..end]}{more}");
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
