using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OfficeAI.Application.Abstractions;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Integrations;
using OfficeAI.Domain.Workflows;

namespace OfficeAI.Application.Integrations;

public record IntegrationDto(Guid Id, string Name, IntegrationKind Kind, string SecretEnvVar, bool SecretPresent, List<string> Events, bool IsEnabled,
    DateTimeOffset Cursor, uint Version);
public record SaveIntegrationRequest(string Name, string SecretEnvVar, List<string> Events, bool IsEnabled = true, uint? Version = null);
public record DeliveryDto(long Id, string Event, string Title, DeliveryStatus Status, int? StatusCode, string? Error, DateTimeOffset CreatedAt);
public record IntegrationMessage(string Event, string Title, string Text, string? Link, DateTimeOffset OccurredAt);

public class IntegrationSettings
{
    /// <summary>Public base URL of the web app, used for links in notifications.</summary>
    public string? AppBaseUrl { get; set; }

    /// <summary>Public base URL of the API, used for calendar subscription links. Defaults to the address of the request.</summary>
    public string? ApiBaseUrl { get; set; }

    /// <summary>Plain http targets are refused unless allowed (development and tests only).</summary>
    public bool AllowHttp { get; set; }
}

public partial class IntegrationService(IAppDbContext db, TimeProvider clock)
{
    [GeneratedRegex("^INTEGRATION_[A-Z0-9_]{2,60}$")]
    private static partial Regex EnvVarName();

    public static string? ReadSecret(string envVar) =>
        EnvVarName().IsMatch(envVar) ? Environment.GetEnvironmentVariable(envVar) : null;

    public async Task<List<IntegrationDto>> ListAsync(CancellationToken ct) =>
        (await db.Integrations.AsNoTracking().OrderBy(i => i.Name).ToListAsync(ct))
            .Select(i => new IntegrationDto(i.Id, i.Name, i.Kind, i.SecretEnvVar, !string.IsNullOrEmpty(ReadSecret(i.SecretEnvVar)), i.Events, i.IsEnabled, i.Cursor, i.Version))
            .ToList();

    private static void Validate(SaveIntegrationRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length > 100) throw new DomainException("validation", "name is required (max 100).");
        if (!EnvVarName().IsMatch(req.SecretEnvVar ?? ""))
            throw new DomainException("validation", $"secretEnvVar must be an environment variable name starting with {IntegrationEvents.EnvPrefix} (upper case).");
        var unknown = (req.Events ?? []).Except(IntegrationEvents.All).ToList();
        if (unknown.Count > 0 || req.Events is null || req.Events.Count == 0)
            throw new DomainException("validation", $"events must be one or more of: {string.Join(", ", IntegrationEvents.All)}.");
    }

    public async Task<IntegrationDto> CreateAsync(SaveIntegrationRequest req, CancellationToken ct)
    {
        Validate(req);
        var i = new IntegrationConfiguration { Name = req.Name.Trim(), SecretEnvVar = req.SecretEnvVar, Events = req.Events.Distinct().ToList(), IsEnabled = req.IsEnabled, Cursor = clock.GetUtcNow() };
        db.Integrations.Add(i);
        await db.SaveChangesAsync(ct);
        return (await ListAsync(ct)).First(x => x.Id == i.Id);
    }

    public async Task<IntegrationDto> UpdateAsync(Guid id, SaveIntegrationRequest req, CancellationToken ct)
    {
        Validate(req);
        var i = await db.Integrations.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Integration", id);
        if (req.Version is { } v) db.ExpectVersion(i, v);
        if (req.IsEnabled && !i.IsEnabled) i.Cursor = clock.GetUtcNow(); // don't replay what happened while it was off
        i.Name = req.Name.Trim();
        i.SecretEnvVar = req.SecretEnvVar;
        i.Events = req.Events.Distinct().ToList();
        i.IsEnabled = req.IsEnabled;
        await db.SaveChangesAsync(ct);
        return (await ListAsync(ct)).First(x => x.Id == id);
    }

    public async Task<List<DeliveryDto>> DeliveriesAsync(Guid id, CancellationToken ct) =>
        await db.IntegrationDeliveries.AsNoTracking().Where(d => d.IntegrationId == id).OrderByDescending(d => d.Id).Take(100)
            .Select(d => new DeliveryDto(d.Id, d.Event, d.Title, d.Status, d.StatusCode, d.Error, d.CreatedAt)).ToListAsync(ct);
}

/// <summary>Turns recent company events into notifications and posts them to the enabled webhooks.</summary>
public sealed class IntegrationDispatcher(IAppDbContext db, IHttpClientFactory http, Microsoft.Extensions.Options.IOptions<IntegrationSettings> settings,
    TimeProvider clock, ILogger<IntegrationDispatcher> log)
{
    public async Task<int> TickAsync(CancellationToken ct)
    {
        var sent = 0;
        foreach (var integration in await db.Integrations.Where(i => i.IsEnabled).ToListAsync(ct))
        {
            var until = clock.GetUtcNow();
            var messages = await CollectAsync(integration, integration.Cursor, until, ct);
            var url = IntegrationService.ReadSecret(integration.SecretEnvVar);
            foreach (var m in messages)
            {
                var delivery = new IntegrationDelivery { IntegrationId = integration.Id, Event = m.Event, Title = m.Title, CreatedAt = clock.GetUtcNow() };
                if (string.IsNullOrEmpty(url))
                    (delivery.Status, delivery.Error) = (DeliveryStatus.Failed, $"{integration.SecretEnvVar} is not set on the server.");
                else if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && !(settings.Value.AllowHttp && uri.Scheme == Uri.UriSchemeHttp)))
                    (delivery.Status, delivery.Error) = (DeliveryStatus.Failed, "The target must be an https URL.");
                else
                    await SendAsync(uri, m, delivery, ct);
                if (delivery.Status == DeliveryStatus.Sent) sent++;
                db.IntegrationDeliveries.Add(delivery);
            }
            integration.Cursor = until;
            await db.SaveChangesAsync(ct);
        }
        return sent;
    }

    private async Task SendAsync(Uri uri, IntegrationMessage m, IntegrationDelivery delivery, CancellationToken ct)
    {
        var client = http.CreateClient("integrations");
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var res = await client.PostAsJsonAsync(uri, new { @event = m.Event, title = m.Title, text = $"{m.Title}\n{m.Text}", link = m.Link, occurredAt = m.OccurredAt }, ct);
                delivery.StatusCode = (int)res.StatusCode;
                if (res.IsSuccessStatusCode)
                {
                    delivery.Status = DeliveryStatus.Sent;
                    delivery.Error = null;
                    return;
                }
                (delivery.Status, delivery.Error) = (DeliveryStatus.Failed, $"HTTP {(int)res.StatusCode}");
                if ((int)res.StatusCode < 500) return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                log.LogWarning(ex, "Webhook delivery failed");
                (delivery.Status, delivery.Error) = (DeliveryStatus.Failed, ex.Message);
            }
        }
    }

    private async Task<List<IntegrationMessage>> CollectAsync(IntegrationConfiguration i, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var link = settings.Value.AppBaseUrl?.TrimEnd('/');
        var list = new List<IntegrationMessage>();
        if (i.Events.Contains(IntegrationEvents.ApprovalRequested))
            list.AddRange((await db.ApprovalRequests.AsNoTracking().Where(a => a.CreatedAt > from && a.CreatedAt <= to).OrderBy(a => a.CreatedAt).Take(100).ToListAsync(ct))
                .Select(a => new IntegrationMessage(IntegrationEvents.ApprovalRequested, a.Title, a.Details ?? "", link is null ? null : $"{link}/approvals", a.CreatedAt)));
        var wfKinds = new List<string>();
        if (i.Events.Contains(IntegrationEvents.WorkflowCompleted)) wfKinds.Add("completed");
        if (i.Events.Contains(IntegrationEvents.WorkflowEscalated)) wfKinds.Add("escalated");
        if (i.Events.Contains(IntegrationEvents.BudgetReached)) wfKinds.Add("budget_reached");
        if (wfKinds.Count > 0)
            list.AddRange((await db.WorkflowEvents.AsNoTracking().Where(e => e.CreatedAt > from && e.CreatedAt <= to && wfKinds.Contains(e.Kind)).OrderBy(e => e.Id).Take(100).ToListAsync(ct))
                .Select(e => new IntegrationMessage(e.Kind switch { "completed" => IntegrationEvents.WorkflowCompleted, "escalated" => IntegrationEvents.WorkflowEscalated, _ => IntegrationEvents.BudgetReached },
                    e.Message, "", null, e.CreatedAt)));
        if (i.Events.Contains(IntegrationEvents.RunFailed))
            list.AddRange((await db.AgentRuns.AsNoTracking().Where(r => r.FinishedAt > from && r.FinishedAt <= to && r.Status == AgentRunStatus.Failed).Take(100)
                    .Join(db.Employees, r => r.EmployeeId, e => e.Id, (r, e) => new { r.Error, r.FinishedAt, e.DisplayName }).ToListAsync(ct))
                .Select(r => new IntegrationMessage(IntegrationEvents.RunFailed, $"{r.DisplayName}: run failed", r.Error ?? "", link is null ? null : $"{link}/agents", r.FinishedAt!.Value)));
        return list.OrderBy(m => m.OccurredAt).ToList();
    }
}
