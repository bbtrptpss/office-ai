using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Security;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Infrastructure.Security;

public record RetentionCounts(int RunTranscripts, int RunSteps, int ToolOutputs, int TestOutputs, int Notifications, int IntegrationDeliveries, int ChatMessages)
{
    public int Total => RunSteps + ToolOutputs + TestOutputs + Notifications + IntegrationDeliveries + ChatMessages + RunTranscripts;
}
public record RetentionDto(int? RunTranscriptDays, int? TestOutputDays, int? NotificationDays, int? IntegrationDeliveryDays, int? ChatMessageDays,
    DateTimeOffset? LastRunAt, RetentionCounts? LastResult, uint Version);
public record SaveRetentionRequest(int? RunTranscriptDays, int? TestOutputDays, int? NotificationDays, int? IntegrationDeliveryDays, int? ChatMessageDays, uint? Version = null);

/// <summary>Applies the retention policy. Removes only what the policy names; everything else is untouched.</summary>
public class RetentionService(AppDbContext db, SecurityAuditWriter audit, TimeProvider clock)
{
    public const string AuditAction = "RetentionApplied";

    public async Task<RetentionDto> GetAsync(CancellationToken ct)
    {
        var p = await db.RetentionPolicies.AsNoTracking().FirstOrDefaultAsync(ct) ?? new RetentionPolicy();
        return new RetentionDto(p.RunTranscriptDays, p.TestOutputDays, p.NotificationDays, p.IntegrationDeliveryDays, p.ChatMessageDays, p.LastRunAt,
            p.LastResultJson is null ? null : JsonSerializer.Deserialize<RetentionCounts>(p.LastResultJson), p.Version);
    }

    public async Task<RetentionDto> SaveAsync(SaveRetentionRequest req, CancellationToken ct)
    {
        foreach (var days in new[] { req.RunTranscriptDays, req.TestOutputDays, req.NotificationDays, req.IntegrationDeliveryDays, req.ChatMessageDays })
            if (days is { } d && d is < RetentionPolicy.MinDays or > RetentionPolicy.MaxDays)
                throw new DomainException("validation", $"Each period must be empty (keep forever) or {RetentionPolicy.MinDays}–{RetentionPolicy.MaxDays} days.");
        var p = await db.RetentionPolicies.FirstOrDefaultAsync(ct);
        if (p is null) db.RetentionPolicies.Add(p = new RetentionPolicy());
        else if (req.Version is { } v) db.ExpectVersion(p, v);
        p.RunTranscriptDays = req.RunTranscriptDays;
        p.TestOutputDays = req.TestOutputDays;
        p.NotificationDays = req.NotificationDays;
        p.IntegrationDeliveryDays = req.IntegrationDeliveryDays;
        p.ChatMessageDays = req.ChatMessageDays;
        await db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }

    private DateTimeOffset? Cutoff(int? days) => days is { } d ? clock.GetUtcNow().AddDays(-d) : null;

    // Each query selects exactly the rows the clean-up changes, so the preview and the run always agree.
    private IQueryable<Domain.Agents.AgentRun> Transcripts(DateTimeOffset c) =>
        db.AgentRuns.Where(r => r.FinishedAt != null && r.FinishedAt < c && r.ConversationJson != null);
    private IQueryable<Domain.Agents.AgentRunStep> Steps(DateTimeOffset c) =>
        db.AgentRunSteps.Where(s => db.AgentRuns.Any(r => r.Id == s.RunId && r.FinishedAt != null && r.FinishedAt < c));
    private IQueryable<Domain.Agents.ToolExecution> ToolOutputs(DateTimeOffset c) =>
        db.ToolExecutions.Where(t => t.StartedAt < c && t.Output != null && db.AgentRuns.Any(r => r.Id == t.RunId && r.FinishedAt != null));
    private IQueryable<Domain.Code.TestRun> TestOutputs(DateTimeOffset c) => db.TestRuns.Where(t => t.StartedAt < c && t.Output != null);
    private IQueryable<Domain.Communication.Notification> Notifications(DateTimeOffset c) => db.Notifications.Where(n => n.CreatedAt < c);
    private IQueryable<Domain.Integrations.IntegrationDelivery> Deliveries(DateTimeOffset c) => db.IntegrationDeliveries.Where(d => d.CreatedAt < c);
    private IQueryable<Domain.Communication.ChatMessage> Messages(DateTimeOffset c) => db.ChatMessages.Where(m => m.CreatedAt < c && m.Body != "");

    /// <summary>What the clean-up would change right now. Changes nothing.</summary>
    public async Task<RetentionCounts> PreviewAsync(CancellationToken ct)
    {
        var p = await db.RetentionPolicies.AsNoTracking().FirstOrDefaultAsync(ct) ?? new RetentionPolicy();
        async Task<int> Count<T>(int? days, Func<DateTimeOffset, IQueryable<T>> q) => Cutoff(days) is { } c ? await q(c).CountAsync(ct) : 0;
        return new RetentionCounts(await Count(p.RunTranscriptDays, Transcripts), await Count(p.RunTranscriptDays, Steps), await Count(p.RunTranscriptDays, ToolOutputs),
            await Count(p.TestOutputDays, TestOutputs), await Count(p.NotificationDays, Notifications), await Count(p.IntegrationDeliveryDays, Deliveries),
            await Count(p.ChatMessageDays, Messages));
    }

    /// <summary>Removes data older than the policy allows and records the counts in the audit log. Returns null when the policy keeps everything.</summary>
    public async Task<RetentionCounts?> ApplyAsync(string trigger, CancellationToken ct)
    {
        var p = await db.RetentionPolicies.FirstOrDefaultAsync(ct);
        if (p is null || new[] { p.RunTranscriptDays, p.TestOutputDays, p.NotificationDays, p.IntegrationDeliveryDays, p.ChatMessageDays }.All(d => d is null))
            return null;

        var now = clock.GetUtcNow();
        int transcripts = 0, steps = 0, tools = 0, tests = 0, notifications = 0, deliveries = 0, messages = 0;
        if (Cutoff(p.RunTranscriptDays) is { } runs)
        {
            steps = await Steps(runs).ExecuteDeleteAsync(ct);
            transcripts = await Transcripts(runs).ExecuteUpdateAsync(s => s.SetProperty(r => r.ConversationJson, (string?)null), ct);
            tools = await ToolOutputs(runs).ExecuteUpdateAsync(s => s.SetProperty(t => t.Output, (string?)null).SetProperty(t => t.InputJson, "{}"), ct);
        }
        if (Cutoff(p.TestOutputDays) is { } t) tests = await TestOutputs(t).ExecuteUpdateAsync(s => s.SetProperty(x => x.Output, (string?)null), ct);
        if (Cutoff(p.NotificationDays) is { } n) notifications = await Notifications(n).ExecuteDeleteAsync(ct);
        if (Cutoff(p.IntegrationDeliveryDays) is { } d) deliveries = await Deliveries(d).ExecuteDeleteAsync(ct);
        if (Cutoff(p.ChatMessageDays) is { } m)
            messages = await Messages(m).ExecuteUpdateAsync(s => s.SetProperty(x => x.Body, "").SetProperty(x => x.MentionedEmployeeIds, new List<Guid>())
                .SetProperty(x => x.DeletedAt, x => x.DeletedAt ?? now), ct);

        var counts = new RetentionCounts(transcripts, steps, tools, tests, notifications, deliveries, messages);
        p.LastRunAt = now;
        p.LastResultJson = JsonSerializer.Serialize(counts);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditAction, nameof(RetentionPolicy), p.Id.ToString(), null, null, new { trigger, counts }, ct);
        return counts;
    }
}
