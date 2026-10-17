using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Application.Agents;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Organization;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Api.Infrastructure;

/// <summary>In-process queue of runs to execute. The database row is the source of truth; the channel only wakes the worker.</summary>
public sealed class AgentRunQueue : IAgentRunQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();

    public ChannelReader<Guid> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(Guid runId, CancellationToken ct = default) => _channel.Writer.WriteAsync(runId, ct);

    public void Cancel(Guid runId)
    {
        if (_running.TryGetValue(runId, out var cts)) cts.Cancel();
    }

    internal CancellationTokenSource Track(Guid runId, CancellationToken stopping)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        _running[runId] = cts;
        return cts;
    }

    internal void Untrack(Guid runId)
    {
        if (_running.TryRemove(runId, out var cts)) cts.Dispose();
    }
}

/// <summary>Executes queued runs with bounded concurrency and recovers runs interrupted by a restart.</summary>
public sealed class AgentRunWorker(AgentRunQueue queue, IServiceScopeFactory scopes, IConfiguration config, ILogger<AgentRunWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        await RecoverAsync(stopping);
        using var slots = new SemaphoreSlim(Math.Max(1, config.GetValue("AgentRuns:MaxConcurrency", 4)));

        await foreach (var runId in queue.Reader.ReadAllAsync(stopping))
        {
            await slots.WaitAsync(stopping);
            _ = Task.Run(async () =>
            {
                var cts = queue.Track(runId, stopping);
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<AgentExecutor>().ExecuteAsync(runId, cts.Token);
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Run {RunId} crashed", runId);
                }
                finally
                {
                    queue.Untrack(runId);
                    slots.Release();
                }
            }, CancellationToken.None);
        }
    }

    /// <summary>A run that was Running when the process stopped cannot be resumed mid-call: mark it failed. Queued runs are re-queued.</summary>
    private async Task RecoverAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using var acting = ActingAs.Use(new ActingIdentity(null, "System", IsAgent: false, "recovery"));

            var interrupted = await db.AgentRuns.Where(r => r.Status == AgentRunStatus.Running).ToListAsync(ct);
            foreach (var run in interrupted)
            {
                run.Status = AgentRunStatus.Failed;
                run.StopReason = "interrupted";
                run.Error = "The server restarted while this run was in progress. Start a new run.";
                run.FinishedAt = DateTimeOffset.UtcNow;
                var employee = await db.Employees.FirstAsync(e => e.Id == run.EmployeeId, ct);
                if (employee.Status == EmployeeStatus.Working) employee.Status = EmployeeStatus.Available;
            }
            await db.SaveChangesAsync(ct);

            foreach (var id in await db.AgentRuns.Where(r => r.Status == AgentRunStatus.Queued).OrderBy(r => r.CreatedAt).Select(r => r.Id).ToListAsync(ct))
                await queue.EnqueueAsync(id, ct);
            if (interrupted.Count > 0) log.LogWarning("Marked {Count} interrupted agent runs as failed", interrupted.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Agent run recovery failed");
        }
    }
}
