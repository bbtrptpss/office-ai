using System.Collections.Concurrent;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Communication;

namespace OfficeAI.Application.Communication;

/// <summary>The Claude Code CLI signed in on the host machine, reached through scripts/windows/claude-bridge.mjs.</summary>
public interface IClaudeBridge
{
    bool IsConfigured { get; }
    Task<ClaudeBridgeReply> AskAsync(string prompt, string system, string? sessionId, CancellationToken ct);
}

public record ClaudeBridgeReply(string Reply, string SessionId, decimal CostUsd);
public record ClaudeAskRequest(string Message, bool NewChat = false);
/// <param name="EquivalentCostUsd">What the call would cost on the API; the person's Claude plan actually pays for it.</param>
public record ClaudeReplyDto(string Reply, bool Continued, decimal EquivalentCostUsd);

/// <summary>
/// The dashboard's "Claude" assistant: the owner's own Claude Code session, talk only (the bridge disables every tool).
/// One conversation per person, continued until they start a new chat. Both sides are kept in the AI Assistant channel.
/// </summary>
public class ClaudeChatService(IAppDbContext db, ICurrentUser current, IClaudeBridge bridge, IRealtimeNotifier notifier)
{
    public const string ClaudeName = "Claude";
    public const int MaxMessage = 4000;

    /// <summary>Claude Code session per person; in memory, so a server restart starts fresh conversations.</summary>
    private static readonly ConcurrentDictionary<Guid, string> Sessions = new();

    public async Task<ClaudeReplyDto> AskAsync(ClaudeAskRequest req, CancellationToken ct)
    {
        var message = (req.Message ?? "").Trim();
        if (message.Length == 0) throw new DomainException("validation", "message is required.");
        if (message.Length > MaxMessage) throw new DomainException("validation", $"message must be at most {MaxMessage} characters.");
        if (!bridge.IsConfigured) throw new DomainException("claude.not_configured", "ยังไม่ได้ตั้งค่าตัวเชื่อม Claude (CLAUDE_BRIDGE_TOKEN ใน .env)");
        var userId = current.UserId ?? throw new ForbiddenException("Sign in first.");

        if (req.NewChat) Sessions.TryRemove(userId, out _);
        Sessions.TryGetValue(userId, out var session);
        var system = await SystemPromptAsync(ct);
        ClaudeBridgeReply reply;
        try
        {
            reply = await bridge.AskAsync(message, system, session, ct);
        }
        catch (Exception ex) when (session is not null && ex is not OperationCanceledException)
        {
            // The old conversation may be gone (e.g. cleaned up); start a new one rather than fail.
            session = null;
            reply = await AskFreshAsync(message, system, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unavailable(ex);
        }
        Sessions[userId] = reply.SessionId;

        await RecordAsync(message, reply.Reply, ct);
        return new ClaudeReplyDto(reply.Reply, session is not null, reply.CostUsd);
    }

    private async Task<ClaudeBridgeReply> AskFreshAsync(string message, string system, CancellationToken ct)
    {
        try
        {
            return await bridge.AskAsync(message, system, null, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unavailable(ex);
        }
    }

    /// <summary>Bridge not running vs. Claude itself refusing (e.g. the plan's usage limit): say which.</summary>
    private static DomainException Unavailable(Exception ex) =>
        ex.InnerException is System.Net.Sockets.SocketException || ex is TaskCanceledException
            ? new("claude.unavailable", "ตัวเชื่อม Claude ไม่ตอบ — เปิดระบบด้วยไอคอน Office AI หรือรัน node scripts/windows/claude-bridge.mjs")
            : new("claude.failed", $"Claude ตอบไม่ได้: {ex.Message}");

    private async Task<string> SystemPromptAsync(CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are Claude, chatting with the owner of \"AI Software House\" from the dashboard of their company system.");
        sb.AppendLine("Reply in the language they use (usually Thai), briefly and warmly, like a chat message.");
        sb.AppendLine("You have no tools here: you cannot run commands, read files or change anything. If they need something done, suggest giving the work to an AI employee from the 3D Office or with @name /run in a project chat.");
        sb.AppendLine("Live company snapshot (use it for questions about progress, work and priorities; say when it does not cover something):");
        sb.Append(await AssistantService.SnapshotAsync(db, ct));
        return sb.ToString();
    }

    /// <summary>Keeps the exchange in the AI Assistant channel so it can be read later in chat.</summary>
    private async Task RecordAsync(string question, string answer, CancellationToken ct)
    {
        var channel = await db.ChatChannels.FirstOrDefaultAsync(c => c.Kind == ChannelKind.Company && c.Name == ChatService.AssistantChannel, ct);
        if (channel is null) return;
        var me = await db.Employees.AsNoTracking().Where(e => e.UserId == current.UserId).Select(e => new { e.Id, e.DisplayName }).FirstOrDefaultAsync(ct);
        var asked = new ChatMessage
        {
            ChannelId = channel.Id, AuthorUserId = current.UserId, AuthorEmployeeId = me?.Id, AuthorName = me?.DisplayName ?? current.Name ?? "Unknown",
            Body = Clip($"@{ClaudeName} {question}"),
        };
        var answered = new ChatMessage { ChannelId = channel.Id, AuthorName = ClaudeName, IsAgent = true, Body = Clip(answer) };
        db.ChatMessages.Add(asked);
        await db.SaveChangesAsync(ct);
        db.ChatMessages.Add(answered); // saved after the question so it sorts after it
        await db.SaveChangesAsync(ct);
        await notifier.ChatMessageAsync(channel.Id, answered.Id, ct);
    }

    private static string Clip(string text) => text.Length > ChatService.MaxBody ? text[..(ChatService.MaxBody - 1)] + "…" : text;
}
