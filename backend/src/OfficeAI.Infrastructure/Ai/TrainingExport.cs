using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Agents;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Work;
using OfficeAI.Domain.Workflows;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Infrastructure.Ai;

public record TrainingExportSummaryDto(int WorkRuns, int PlanRuns, DateTimeOffset? Oldest, DateTimeOffset? Newest);

/// <summary>
/// Real work people approved, as fine-tuning data in the format D:\FreeAi\train uses: one JSON object per line with
/// <c>tag</c>, <c>tools</c> (OpenAI function definitions) and <c>messages</c> (system, user, assistant tool calls, tool results).
/// A work run counts when it succeeded, is not simulated, its task is Done (a person approved it) and its deliverable was not
/// rejected; a planning run counts when a person approved its plan. Simulated runs and runs whose transcript the retention
/// policy removed are never included.
/// </summary>
public sealed class TrainingExportService(AppDbContext db, ToolRegistry tools)
{
    private IQueryable<AgentRun> Eligible(DateTimeOffset? since) =>
        db.AgentRuns.AsNoTracking()
            .Where(r => r.Status == AgentRunStatus.Succeeded && !r.IsSimulated && r.ConversationJson != null && (since == null || r.CreatedAt >= since))
            .Where(r => (r.Purpose == RunPurpose.Work
                         && db.Tasks.Any(t => t.Id == r.TaskId && t.Status == WorkTaskStatus.Done)
                         && db.Deliverables.Any(d => d.RunId == r.Id && d.Status != DeliverableStatus.Rejected))
                     || (r.Purpose == RunPurpose.Plan
                         && db.ApprovalRequests.Any(a => a.RunId == r.Id && a.Kind == ApprovalKind.Plan && a.Status == ApprovalStatus.Approved)));

    public async Task<TrainingExportSummaryDto> SummaryAsync(DateTimeOffset? since, CancellationToken ct)
    {
        var rows = await Eligible(since).Select(r => new { r.Purpose, r.CreatedAt }).ToListAsync(ct);
        return new TrainingExportSummaryDto(rows.Count(r => r.Purpose == RunPurpose.Work), rows.Count(r => r.Purpose == RunPurpose.Plan),
            rows.Count == 0 ? null : rows.Min(r => r.CreatedAt), rows.Count == 0 ? null : rows.Max(r => r.CreatedAt));
    }

    private static readonly JsonSerializerOptions Line = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>JSONL, oldest run first. The tool list is rebuilt from the employee's current grants, as the run was offered them.</summary>
    public async Task<(string Jsonl, int Count)> ExportAsync(DateTimeOffset? since, CancellationToken ct)
    {
        var runs = await Eligible(since).OrderBy(r => r.CreatedAt).ToListAsync(ct);
        var sb = new StringBuilder();
        var count = 0;
        foreach (var run in runs)
        {
            var system = await db.AgentRunSteps.AsNoTracking().Where(s => s.RunId == run.Id && s.Kind == RunStepKind.Instructions)
                .OrderBy(s => s.Sequence).Select(s => s.Content).FirstOrDefaultAsync(ct);
            if (system is null) continue;
            var role = await db.Employees.AsNoTracking().Where(e => e.Id == run.EmployeeId).Select(e => e.RoleDefinition!.Code).FirstOrDefaultAsync(ct) ?? "unknown";
            var granted = await db.AgentToolPermissions.AsNoTracking().Where(p => p.EmployeeId == run.EmployeeId).Select(p => p.ToolId).ToListAsync(ct);
            var defs = granted.Select(tools.Find).OfType<IAgentTool>().Where(t => t.Descriptor.AllowedFor(run.Purpose)).OrderBy(t => t.Descriptor.Id)
                .Select(t => new AiToolDefinition(AgentExecutor.WireName(t.Descriptor.Id), t.Descriptor.Description, t.Descriptor.InputSchema)).ToList();

            var request = OpenAiProvider.BuildRequest(new AiRequest(run.ModelId, system, ConversationCodec.Decode(run.ConversationJson!), defs, 0, Effort.Medium));
            var messages = request["messages"]!.AsArray();
            // The target is the model's last turn: drop tool results left after the run handed the task on.
            while (messages.Count > 0 && messages[^1]?["role"]?.GetValue<string>() != "assistant") messages.RemoveAt(messages.Count - 1);
            if (messages.Count < 3) continue;
            var sample = new JsonObject
            {
                ["tag"] = $"real-{run.Purpose.ToString().ToLowerInvariant()}-{role.ToLowerInvariant()}",
                ["tools"] = request["tools"]?.DeepClone() ?? new JsonArray(),
                ["messages"] = messages.DeepClone(),
            };
            sb.Append(sample.ToJsonString(Line)).Append('\n');
            count++;
        }
        return (sb.ToString(), count);
    }
}
