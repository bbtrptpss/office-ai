using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Application.Agents;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Knowledge;

namespace OfficeAI.Application.Knowledge;

internal static class K
{
    public static JsonElement Obj(object properties, string[] required) =>
        JsonSerializer.SerializeToElement(new { type = "object", properties, required, additionalProperties = false });
    public static string? Str(JsonElement o, string n) => o.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static string Snippet(string body, string query)
    {
        var i = body.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        var start = Math.Max(0, i < 0 ? 0 : i - 120);
        var len = Math.Min(body.Length - start, 900); // room for a whole short policy entry, so the agent can answer from it
        return (start > 0 ? "…" : "") + body.Substring(start, len) + (start + len < body.Length ? "…" : "");
    }
}

/// <summary>Search validated company and project knowledge. Proposed entries are never returned: they are not trusted yet.</summary>
public sealed class SearchKnowledgeTool : IAgentTool
{
    public const string ToolId = "knowledge.search";

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0",
        "Search the company's validated knowledge (policies, standards, decisions, lessons, templates) and this project's validated notes. " +
        "Each result has an id and a source you should cite.",
        K.Obj(new { query = new { type = "string", maxLength = 200 } }, ["query"]),
        TimeSpan.FromSeconds(10), 30_000, false, false, [RunPurpose.Work, RunPurpose.Review, RunPurpose.Plan]);

    public async Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct)
    {
        var q = (K.Str(input, "query") ?? "").Trim();
        if (q.Length < 2) return ToolResult.Fail("query must have at least 2 characters.");
        var hits = await KnowledgeService.SearchQuery(ctx.Db, q, ctx.ProjectId)
            .Where(k => k.Status == KnowledgeStatus.Validated).Take(10).ToListAsync(ct);
        return ToolResult.Ok(hits.Select(k => new
        {
            id = k.Id, kind = k.Kind.ToString(), k.Title, scope = k.ProjectId is null ? "company" : "project", k.Source, snippet = K.Snippet(k.Body, q),
        }));
    }
}

/// <summary>Suggest new knowledge. It stays Proposed until a person validates it.</summary>
public sealed class ProposeKnowledgeTool : IAgentTool
{
    public const string ToolId = "knowledge.propose";

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0",
        "Propose a lesson, standard, decision or template worth keeping. A person validates it before anyone relies on it. " +
        "scope 'project' keeps it inside this project; 'company' suggests it for everyone.",
        K.Obj(new
        {
            kind = new { type = "string", @enum = new[] { "Policy", "Standard", "Decision", "Lesson", "Template", "Reference" } },
            title = new { type = "string", maxLength = 200 },
            body = new { type = "string", maxLength = 20_000 },
            tags = new { type = "string", description = "Comma-separated", maxLength = 300 },
            scope = new { type = "string", @enum = new[] { "project", "company" } },
        }, ["kind", "title", "body", "scope"]),
        TimeSpan.FromSeconds(10), 1_000, false, true, [RunPurpose.Work, RunPurpose.Review]);

    public async Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct)
    {
        if (!Enum.TryParse<KnowledgeKind>(K.Str(input, "kind"), true, out var kind) || !Enum.IsDefined(kind)) return ToolResult.Fail("kind is invalid.");
        var title = K.Str(input, "title")!.Trim();
        var body = K.Str(input, "body")!;
        if (title.Length == 0 || string.IsNullOrWhiteSpace(body)) return ToolResult.Fail("title and body must not be empty.");
        var entry = new KnowledgeEntry
        {
            Kind = kind, Title = title, Body = body, ProjectId = K.Str(input, "scope") == "company" ? null : ctx.ProjectId,
            Tags = (K.Str(input, "tags") ?? "").Split(',').Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).Distinct().Take(10).ToList(),
            Source = $"task:{ctx.TaskId} run:{ctx.RunId}", AuthorEmployeeId = ctx.EmployeeId,
        };
        ctx.Db.KnowledgeEntries.Add(entry);
        await ctx.Db.SaveChangesAsync(ct);
        return ToolResult.Ok(new { id = entry.Id, status = "Proposed — a person will validate it" });
    }
}

/// <summary>Write a project document. Every call creates a new version of the document with that kind and title.</summary>
public sealed class WriteDocumentTool : IAgentTool
{
    public const string ToolId = "document.write";

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0",
        "Write a project document in Markdown (proposal, statement of work, specification, release notes, completion report, meeting minutes). " +
        "Writing the same kind and title again creates a new version. Documents are drafts until a person marks them final.",
        K.Obj(new
        {
            kind = new { type = "string", @enum = new[] { "Proposal", "StatementOfWork", "Specification", "ReleaseNotes", "CompletionReport", "MeetingMinutes", "Other" } },
            title = new { type = "string", maxLength = 200 },
            content = new { type = "string", maxLength = 200_000 },
        }, ["kind", "title", "content"]),
        TimeSpan.FromSeconds(15), 1_000, false, true);

    public async Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct)
    {
        if (!Enum.TryParse<DocumentKind>(K.Str(input, "kind"), true, out var kind) || !Enum.IsDefined(kind)) return ToolResult.Fail("kind is invalid.");
        var title = K.Str(input, "title")!.Trim();
        var content = K.Str(input, "content")!;
        if (title.Length == 0 || string.IsNullOrWhiteSpace(content)) return ToolResult.Fail("title and content must not be empty.");
        var doc = await KnowledgeService.AddVersionAsync(ctx.Db, ctx.ProjectId, kind, title, content, ct);
        doc.AuthorEmployeeId = ctx.EmployeeId;
        doc.TaskId = ctx.TaskId;
        doc.RunId = ctx.RunId;
        doc.IsSimulated = ctx.IsSimulated;
        await ctx.Db.SaveChangesAsync(ct);
        return ToolResult.Ok(new { id = doc.Id, version = doc.Revision, status = "Draft" });
    }
}
