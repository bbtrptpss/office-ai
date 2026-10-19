using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Application.Agents;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Communication;
using OfficeAI.Domain.Organization;
using OfficeAI.Domain.Work;

namespace OfficeAI.Application.Communication;

/// <summary>The Claude Code CLI signed in on the host machine, reached through scripts/windows/claude-bridge.mjs.</summary>
public interface IClaudeBridge
{
    bool IsConfigured { get; }
    Task<ClaudeBridgeReply> AskAsync(string prompt, string system, string? sessionId, CancellationToken ct);
    /// <summary>The dashboard terminal: one PowerShell command in the project folder on the host.</summary>
    Task<TerminalResultDto> RunAsync(string command, CancellationToken ct);
}

public record ClaudeBridgeReply(string Reply, string SessionId, decimal CostUsd);
public record TerminalRequest(string Command);
public record TerminalResultDto(string Output, int ExitCode, bool TimedOut, long Ms);
public record ClaudeAskRequest(string Message, bool NewChat = false);
/// <param name="EquivalentCostUsd">What the call would cost on the API; the person's Claude plan actually pays for it.</param>
/// <param name="TasksStarted">How many tasks Claude handed to AI employees in this reply.</param>
public record ClaudeReplyDto(string Reply, bool Continued, decimal EquivalentCostUsd, int TasksStarted = 0);

/// <summary>
/// The dashboard's "Claude" assistant: the owner's own Claude Code session (the bridge disables every Claude Code tool).
/// Claude acts only through <c>&lt;action&gt;</c> blocks in its reply, which this service carries out as the person asking, with their
/// permissions: create a task in a project, assign it to an AI employee and start the work run — the same path as <c>@name /run</c>.
/// One conversation per person, continued until they start a new chat. Both sides are kept in the AI Assistant channel,
/// where the AI employees also report when the work ends.
/// </summary>
public partial class ClaudeChatService(
    IAppDbContext db, ICurrentUser current, IClaudeBridge bridge, IRealtimeNotifier notifier, WorkService work, AgentRunService runs, TimeProvider clock)
{
    public const string ClaudeName = "Claude";
    public const int MaxMessage = 4000;
    public const int MaxActions = 3;

    /// <summary>Claude Code session per person; in memory, so a server restart starts fresh conversations.</summary>
    private static readonly ConcurrentDictionary<Guid, string> Sessions = new();

    [GeneratedRegex(@"<action>(.*?)</action>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ActionPattern();

    /// <param name="canCommandAi">The person may create and assign tasks and run AI (task.manage, task.assign, agent.run).</param>
    public async Task<ClaudeReplyDto> AskAsync(ClaudeAskRequest req, bool canCommandAi, CancellationToken ct)
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

        var (channel, asked) = await RecordQuestionAsync(message, ct);
        var (answer, started) = await CarryOutAsync(reply.Reply, canCommandAi, channel, asked, ct);
        await RecordAnswerAsync(channel, answer, ct);
        return new ClaudeReplyDto(answer, session is not null, reply.CostUsd, started);
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
        sb.AppendLine("You cannot run commands or read files yourself. You get work done by giving it to the company's AI employees:");
        sb.AppendLine("when the owner asks for something to be done (build, write, fix, research, plan...), hand it over yourself by ending your reply with one block per task:");
        sb.AppendLine("<action>{\"project\":\"<project code>\",\"employee\":\"<AI employee name>\",\"title\":\"<short task title>\",\"details\":\"<what to do and what done looks like>\"}</action>");
        sb.AppendLine("The system then creates the task in that project, assigns it to that AI employee, starts the work right away and shows the owner the result under your reply.");
        sb.AppendLine($"Rules: at most {MaxActions} blocks per reply; use only the project codes and AI employee names listed below, picking the employee whose role fits the work;");
        sb.AppendLine("if the project or the work is unclear, ask first instead of guessing; no block for questions, summaries or small talk; never tell them to go and command the AI themselves.");
        sb.AppendLine("Projects (code: name): " + await ProjectListAsync(ct));
        sb.AppendLine("AI employees (name: title): " + await AiEmployeeListAsync(ct));
        sb.AppendLine("Live company snapshot (use it for questions about progress, work and priorities; say when it does not cover something):");
        sb.Append(await AssistantService.SnapshotAsync(db, ct));
        return sb.ToString();
    }

    private async Task<string> ProjectListAsync(CancellationToken ct)
    {
        var rows = await db.Projects.AsNoTracking().Where(p => p.Status == ProjectStatus.Active).OrderBy(p => p.Code).Take(40)
            .Select(p => p.Code + ": " + p.Name).ToListAsync(ct);
        return rows.Count == 0 ? "none (the owner has to create a project first)" : string.Join("; ", rows);
    }

    private async Task<string> AiEmployeeListAsync(CancellationToken ct)
    {
        var rows = await db.Employees.AsNoTracking().Where(e => e.Kind == EmployeeKind.AI && e.Status != EmployeeStatus.Inactive)
            .OrderBy(e => e.DisplayName).Take(60).Select(e => e.DisplayName + ": " + e.RoleDefinition!.Title).ToListAsync(ct);
        return rows.Count == 0 ? "none" : string.Join("; ", rows);
    }

    /// <summary>Removes the action blocks from Claude's reply, carries them out and appends what happened to each.</summary>
    private async Task<(string Answer, int Started)> CarryOutAsync(string reply, bool canCommandAi, ChatChannel? channel, ChatMessage? asked, CancellationToken ct)
    {
        var blocks = ActionPattern().Matches(reply).Select(m => m.Groups[1].Value).ToList();
        if (blocks.Count == 0) return (reply, 0);
        var text = ActionPattern().Replace(reply, "").Trim();
        var results = new List<string>();
        var started = 0;
        if (!canCommandAi) results.Add("❌ สั่งงานไม่ได้: คุณยังไม่มีสิทธิ์สั่งงาน AI (ต้องมีสิทธิ์สร้างงาน มอบหมายงาน และสั่ง AI ทำงาน)");
        else
        {
            foreach (var block in blocks.Take(MaxActions))
            {
                var (ok, line) = await StartWorkAsync(block, channel, asked, ct);
                if (ok) started++;
                results.Add(line);
            }
            if (blocks.Count > MaxActions) results.Add($"⚠️ สั่งได้ครั้งละ {MaxActions} งาน อีก {blocks.Count - MaxActions} งานยังไม่ได้สั่ง");
        }
        return ((text.Length > 0 ? text + "\n\n" : "") + string.Join("\n", results), started);
    }

    private record WorkAction(string? Project, string? Employee, string? Title, string? Details);

    /// <summary>One action block: create the task, assign it and start the work run, as the person asking.</summary>
    private async Task<(bool Ok, string Line)> StartWorkAsync(string block, ChatChannel? channel, ChatMessage? asked, CancellationToken ct)
    {
        WorkAction? a;
        try { a = JsonSerializer.Deserialize<WorkAction>(block, new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
        catch (JsonException) { a = null; }
        var title = a?.Title?.Trim() ?? "";
        if (a is null || title.Length == 0) return (false, "❌ สั่งงานไม่ได้: Claude ส่งคำสั่งที่อ่านไม่ออก ลองพิมพ์ใหม่อีกครั้ง");
        if (title.Length > ChatCommandService.MaxTitle) title = title[..(ChatCommandService.MaxTitle - 1)] + "…";

        var code = (a.Project ?? "").Trim().ToLower();
        var project = await db.Projects.AsNoTracking().Where(p => p.Status == ProjectStatus.Active)
            .FirstOrDefaultAsync(p => p.Code.ToLower() == code || p.Name.ToLower() == code, ct);
        if (project is null) return (false, $"❌ “{title}”: ไม่พบโปรเจกต์ “{a.Project}”");
        var name = (a.Employee ?? "").Trim().ToLower();
        var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Kind == EmployeeKind.AI && e.DisplayName.ToLower() == name, ct);
        if (employee is null) return (false, $"❌ “{title}”: ไม่พบพนักงาน AI ชื่อ “{a.Employee}”");

        // Recorded like an @name /run command, so the AI employee reports the outcome in the AI Assistant channel.
        ChatCommand? command = null;
        if (channel is not null && asked is not null)
            command = db.ChatCommands.Add(new ChatCommand
            {
                ChannelId = channel.Id, TriggerMessageId = asked.Id, EmployeeId = employee.Id, RequestedByUserId = current.UserId, CreatedAt = clock.GetUtcNow(),
            }).Entity;
        TaskDto? task = null;
        try
        {
            var by = asked?.AuthorName ?? current.Name ?? "Unknown";
            task = await work.CreateTaskAsync(project.Id, new SaveTaskRequest(title, $"{a.Details?.Trim()}\n\n— สั่งงานผ่าน Claude โดย {by}".Trim()), ct);
            if (command is not null) command.TaskId = task.Id;
            await work.AssignAsync(task.Id, new AssignRequest(employee.Id), ct);
            var run = await runs.CreateRunAsync(task.Id, employee.Id, RunPurpose.Work, ct);
            if (command is not null) command.RunId = run.Id;
            await db.SaveChangesAsync(ct);
            return (true, $"✅ มอบงาน “{task.Title}” ให้ {employee.DisplayName} ({project.Code}) แล้ว กำลังเริ่มทำ{(run.IsSimulated ? " (โหมดจำลอง)" : "")} — ผลจะแจ้งในห้อง AI Assistant");
        }
        catch (Exception ex) when (ex is DomainException or NotFoundException or ConcurrencyException or ForbiddenException)
        {
            if (command is not null)
            {
                command.Status = ChatCommandStatus.Rejected;
                command.Error = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                command.FinishedAt = clock.GetUtcNow();
                await db.SaveChangesAsync(CancellationToken.None);
            }
            var where = task is null ? "" : " (สร้างงานไว้แล้ว เริ่มใหม่ได้ที่หน้าโปรเจกต์)";
            return (false, $"❌ “{title}” ให้ {employee.DisplayName}: {ex.Message}{where}");
        }
    }

    /// <summary>Keeps the exchange in the AI Assistant channel so it can be read later in chat; the question first, so it sorts first.</summary>
    private async Task<(ChatChannel?, ChatMessage?)> RecordQuestionAsync(string question, CancellationToken ct)
    {
        var channel = await db.ChatChannels.FirstOrDefaultAsync(c => c.Kind == ChannelKind.Company && c.Name == ChatService.AssistantChannel, ct);
        if (channel is null) return (null, null);
        var me = await db.Employees.AsNoTracking().Where(e => e.UserId == current.UserId).Select(e => new { e.Id, e.DisplayName }).FirstOrDefaultAsync(ct);
        var asked = new ChatMessage
        {
            ChannelId = channel.Id, AuthorUserId = current.UserId, AuthorEmployeeId = me?.Id, AuthorName = me?.DisplayName ?? current.Name ?? "Unknown",
            Body = Clip($"@{ClaudeName} {question}"),
        };
        db.ChatMessages.Add(asked);
        await db.SaveChangesAsync(ct);
        return (channel, asked);
    }

    private async Task RecordAnswerAsync(ChatChannel? channel, string answer, CancellationToken ct)
    {
        if (channel is null) return;
        var answered = new ChatMessage { ChannelId = channel.Id, AuthorName = ClaudeName, IsAgent = true, Body = Clip(answer) };
        db.ChatMessages.Add(answered); // saved after the question so it sorts after it
        await db.SaveChangesAsync(ct);
        await notifier.ChatMessageAsync(channel.Id, answered.Id, ct);
    }

    private static string Clip(string text) => text.Length > ChatService.MaxBody ? text[..(ChatService.MaxBody - 1)] + "…" : text;
}
