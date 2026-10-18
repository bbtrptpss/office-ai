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
                    await MarkCrashedAsync(runId);
                }
                finally
                {
                    queue.Untrack(runId);
                    slots.Release();
                }
            }, CancellationToken.None);
        }
    }

    /// <summary>Safety net: a run must never stay Queued/Running after its executor died.</summary>
    private async Task MarkCrashedAsync(Guid runId)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.AgentRuns.Where(r => r.Id == runId && (r.Status == AgentRunStatus.Queued || r.Status == AgentRunStatus.Running))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, AgentRunStatus.Failed)
                    .SetProperty(r => r.StopReason, "crashed")
                    .SetProperty(r => r.Error, "The run stopped unexpectedly.")
                    .SetProperty(r => r.FinishedAt, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Could not mark run {RunId} as failed", runId);
        }
    }

    public const int MaxResumes = 2;

    /// <summary>
    /// A run that was Running when the process stopped continues from its last checkpoint (at most <see cref="MaxResumes"/> times;
    /// only the model call in flight is repeated). Runs without a checkpoint are marked failed. Queued runs are re-queued.
    /// </summary>
    public async Task RecoverAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using var acting = ActingAs.Use(new ActingIdentity(null, "System", IsAgent: false, "recovery"));

            var interrupted = await db.AgentRuns.Where(r => r.Status == AgentRunStatus.Running).ToListAsync(ct);
            var resumed = 0;
            foreach (var run in interrupted)
            {
                var step = (await db.AgentRunSteps.Where(s => s.RunId == run.Id).MaxAsync(s => (int?)s.Sequence, ct) ?? 0) + 1;
                if (run.ConversationJson is not null && run.ResumeCount < MaxResumes && !run.CancelRequested)
                {
                    run.Status = AgentRunStatus.Queued;
                    run.ResumeCount++;
                    db.AgentRunSteps.Add(new AgentRunStep
                    {
                        RunId = run.Id, Sequence = step, Kind = RunStepKind.Notice, CreatedAt = DateTimeOffset.UtcNow,
                        Content = $"The server restarted during this run; it continues from the last saved step (resume {run.ResumeCount} of {MaxResumes}).",
                    });
                    resumed++;
                    continue;
                }
                run.Status = AgentRunStatus.Failed;
                run.StopReason = "interrupted";
                run.Error = run.ConversationJson is null
                    ? "The server restarted while this run was in progress. Start a new run."
                    : $"The server restarted during this run more than {MaxResumes} times. Start a new run.";
                run.FinishedAt = DateTimeOffset.UtcNow;
                var employee = await db.Employees.FirstAsync(e => e.Id == run.EmployeeId, ct);
                if (employee.Status == EmployeeStatus.Working) employee.Status = EmployeeStatus.Available;
            }
            await db.SaveChangesAsync(ct);

            foreach (var id in await db.AgentRuns.Where(r => r.Status == AgentRunStatus.Queued).OrderBy(r => r.CreatedAt).Select(r => r.Id).ToListAsync(ct))
                await queue.EnqueueAsync(id, ct);
            if (interrupted.Count > 0) log.LogWarning("Interrupted agent runs: {Resumed} resumed, {Failed} marked failed", resumed, interrupted.Count - resumed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Agent run recovery failed");
        }
    }
}
