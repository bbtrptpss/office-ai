using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Application.Communication;
using OfficeAI.Domain.Communication;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Api.Infrastructure;

/// <summary>In-process queue of AI chat replies. The ChatReplies table is the source of truth; the channel only wakes the worker.</summary>
public sealed class ChatReplyQueue : IChatReplyQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();
    public ChannelReader<Guid> Reader => _channel.Reader;
    public ValueTask EnqueueAsync(Guid replyId, CancellationToken ct = default) => _channel.Writer.WriteAsync(replyId, ct);
}

/// <summary>Answers @mentions in chat with bounded concurrency; on start-up re-queues waiting replies and closes interrupted ones.</summary>
public sealed class ChatReplyWorker(ChatReplyQueue queue, IServiceScopeFactory scopes, IConfiguration config, ILogger<ChatReplyWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        await RecoverAsync(stopping);
        using var slots = new SemaphoreSlim(Math.Max(1, config.GetValue("ChatReplies:MaxConcurrency", 2)));
        await foreach (var id in queue.Reader.ReadAllAsync(stopping))
        {
            await slots.WaitAsync(stopping);
            _ = Task.Run(async () =>
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
                    timeout.CancelAfter(TimeSpan.FromMinutes(2));
                    await scope.ServiceProvider.GetRequiredService<ChatReplyService>().ExecuteAsync(id, timeout.Token);
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Chat reply {ReplyId} crashed", id);
                    await MarkFailedAsync(id);
                }
                finally
                {
                    slots.Release();
                }
            }, CancellationToken.None);
        }
    }

    private async Task MarkFailedAsync(Guid id)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().ChatReplies
                .Where(r => r.Id == id && (r.Status == ChatReplyStatus.Queued || r.Status == ChatReplyStatus.Running))
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ChatReplyStatus.Failed).SetProperty(r => r.Error, "The reply stopped unexpectedly.")
                    .SetProperty(r => r.FinishedAt, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Could not mark chat reply {ReplyId} as failed", id);
        }
    }

    /// <summary>A reply interrupted mid-call is closed (asking again is cheap); replies still waiting are queued again.</summary>
    private async Task RecoverAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.ChatReplies.Where(r => r.Status == ChatReplyStatus.Running)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ChatReplyStatus.Failed).SetProperty(r => r.Error, "The server restarted while answering.")
                    .SetProperty(r => r.FinishedAt, DateTimeOffset.UtcNow), ct);
            foreach (var id in await db.ChatReplies.Where(r => r.Status == ChatReplyStatus.Queued).OrderBy(r => r.CreatedAt).Select(r => r.Id).ToListAsync(ct))
                await queue.EnqueueAsync(id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Chat reply recovery failed");
        }
    }
}
