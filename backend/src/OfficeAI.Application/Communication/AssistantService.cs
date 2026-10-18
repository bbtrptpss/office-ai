using System.Text;
using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Communication;
using OfficeAI.Domain.Organization;
using OfficeAI.Domain.Work;
using OfficeAI.Domain.Workflows;

namespace OfficeAI.Application.Communication;

public record AssistantMemberDto(Guid EmployeeId, string DisplayName, string JobTitle, EmployeeStatus Status, int OpenTasks, string? CurrentTask);
public record AssistantDto(Guid ChannelId, List<AssistantMemberDto> Members);

/// <summary>
/// The dashboard's "ask the AI" box. Questions are ordinary chat messages in the company channel
/// <see cref="ChatService.AssistantChannel"/>, so answers use the chat reply rules: talk only, priced, budgeted, never AI-to-AI.
/// </summary>
public class AssistantService(IAppDbContext db)
{
    /// <summary>The AI employees offered as assistants, in this order when they exist.</summary>
    public static readonly string[] AssistantRoles = ["COO", "PM", "CTO"];

    public async Task<AssistantDto> GetAsync(CancellationToken ct)
    {
        var channel = await db.ChatChannels.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Kind == ChannelKind.Company && c.Name == ChatService.AssistantChannel, ct)
            ?? throw new Domain.Common.DomainException("assistant.no_channel", $"The '{ChatService.AssistantChannel}' channel is missing.");
        var closed = new[] { WorkTaskStatus.Done, WorkTaskStatus.Cancelled };
        var rows = await db.Employees.AsNoTracking()
            .Where(e => e.Kind == EmployeeKind.AI && e.Status != EmployeeStatus.Inactive && AssistantRoles.Contains(e.RoleDefinition!.Code))
            .Select(e => new
            {
                e.Id, e.DisplayName, e.RoleDefinition!.Code, JobTitle = e.RoleDefinition.Title, e.Status,
                Open = db.TaskAssignments.Count(a => a.EmployeeId == e.Id && a.UnassignedAt == null && db.Tasks.Any(t => t.Id == a.TaskId && !closed.Contains(t.Status))),
                Current = db.AgentRuns.Where(r => r.EmployeeId == e.Id && (r.Status == AgentRunStatus.Queued || r.Status == AgentRunStatus.Running))
                    .Join(db.Tasks, r => r.TaskId, t => t.Id, (r, t) => t.Title).FirstOrDefault(),
            })
            .ToListAsync(ct);
        var members = rows.OrderBy(r => Array.IndexOf(AssistantRoles, r.Code)).ThenBy(r => r.DisplayName, StringComparer.Ordinal)
            .Select(r => new AssistantMemberDto(r.Id, r.DisplayName, r.JobTitle, r.Status, r.Open, r.Current)).ToList();
        return new AssistantDto(channel.Id, members);
    }

    /// <summary>
    /// Live company facts for AI answers in chat (progress, open work, approvals, who is working), so "summarize today" uses real data.
    /// Read-only, compact, and never includes secrets or personal data beyond names and titles.
    /// </summary>
    public static async Task<string> SnapshotAsync(IAppDbContext db, CancellationToken ct)
    {
        var closed = new[] { WorkTaskStatus.Done, WorkTaskStatus.Cancelled };
        var projects = await db.Projects.AsNoTracking().Where(p => p.Status == ProjectStatus.Active).OrderBy(p => p.Code).Take(8)
            .Select(p => new
            {
                p.Code, p.Name,
                Total = db.Tasks.Count(t => t.ProjectId == p.Id && t.Status != WorkTaskStatus.Cancelled),
                Done = db.Tasks.Count(t => t.ProjectId == p.Id && t.Status == WorkTaskStatus.Done),
            }).ToListAsync(ct);
        var byStatus = await db.Tasks.AsNoTracking().Where(t => !closed.Contains(t.Status)).GroupBy(t => t.Status)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var open = await db.Tasks.AsNoTracking().Where(t => !closed.Contains(t.Status))
            .OrderByDescending(t => t.Priority).ThenBy(t => t.DueDate).Take(10)
            .Select(t => new { t.Title, t.Status, t.Priority, t.DueDate }).ToListAsync(ct);
        var approvals = await db.ApprovalRequests.CountAsync(a => a.Status == ApprovalStatus.Pending, ct);
        var working = await db.AgentRuns.AsNoTracking().Where(r => r.Status == AgentRunStatus.Queued || r.Status == AgentRunStatus.Running)
            .Join(db.Employees, r => r.EmployeeId, e => e.Id, (r, e) => new { r.TaskId, e.DisplayName })
            .Join(db.Tasks, x => x.TaskId, t => t.Id, (x, t) => new { x.DisplayName, t.Title }).Take(10).ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine($"Active projects ({projects.Count}): " + (projects.Count == 0 ? "none" : string.Join("; ", projects.Select(p => $"{p.Code} {p.Name} {p.Done}/{p.Total} tasks done"))));
        sb.AppendLine("Open tasks by status: " + (byStatus.Count == 0 ? "none" : string.Join(", ", byStatus.Select(s => $"{s.Key} {s.Count}"))));
        foreach (var t in open) sb.AppendLine($"- {t.Title} [{t.Status}, {t.Priority}{(t.DueDate is { } d ? $", due {d:yyyy-MM-dd}" : "")}]");
        sb.AppendLine($"Approvals waiting for a person: {approvals}");
        sb.AppendLine("AI working right now: " + (working.Count == 0 ? "nobody" : string.Join("; ", working.Select(w => $"{w.DisplayName} on \"{w.Title}\""))));
        return sb.ToString();
    }
}
