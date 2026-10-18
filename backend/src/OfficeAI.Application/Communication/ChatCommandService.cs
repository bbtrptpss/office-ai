using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Application.Agents;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Communication;

namespace OfficeAI.Application.Communication;

/// <summary>
/// <c>@AI /run …</c> in a project channel: the instruction becomes a task in that project, assigned to the AI employee, and an ordinary
/// work run starts on it — same tools, limits, budgets and human approvals as a run started from the task page. The AI reports the
/// start and the outcome in the channel. Only people who may create and assign tasks and run AI can do this.
/// </summary>
public partial class ChatCommandService(IAppDbContext db, WorkService work, AgentRunService runs, IRealtimeNotifier notifier, TimeProvider clock)
{
    public const int MaxTitle = 200;

    [GeneratedRegex(@"(?:^|\s)/(?:run|ทำ)(?=\s|$)", RegexOptions.IgnoreCase)]
    private static partial Regex CommandPattern();

    /// <summary>The instruction after <c>/run</c> (or <c>/ทำ</c>) with the @mentions removed, or null when the message is not a command.</summary>
    public static string? Parse(string body, IEnumerable<string> mentionedNames)
    {
        var match = CommandPattern().Match(body);
        if (!match.Success) return null;
        var text = body.Remove(match.Index, match.Length);
        foreach (var name in mentionedNames.OrderByDescending(n => n.Length)) text = text.Replace($"@{name}", " ");
        return Regex.Replace(text, @"[ \t]+", " ").Trim();
    }

    /// <summary>Creates the task and starts the run, or explains in chat (as the AI employee) why it cannot.</summary>
    public async Task StartAsync(ChatChannel channel, ChatMessage trigger, Guid employeeId, string instruction, bool allowed, string? problem, CancellationToken ct)
    {
        var employee = await db.Employees.AsNoTracking().FirstAsync(e => e.Id == employeeId, ct);
        var command = db.ChatCommands.Add(new ChatCommand
        {
            ChannelId = channel.Id, TriggerMessageId = trigger.Id, EmployeeId = employeeId, RequestedByUserId = trigger.AuthorUserId, CreatedAt = clock.GetUtcNow(),
        }).Entity;

        problem ??= !allowed ? "คุณยังไม่มีสิทธิ์สั่งงาน AI (ต้องมีสิทธิ์สร้างงาน มอบหมายงาน และสั่ง AI ทำงาน)"
            : channel.ProjectId is null ? "สั่งงานได้เฉพาะในห้องแชทของโปรเจกต์ เพราะงานต้องอยู่ในโปรเจกต์ใดโปรเจกต์หนึ่ง"
            : instruction.Length == 0 ? $"บอกด้วยว่าต้องการให้ทำอะไร เช่น @{employee.DisplayName} /run เพิ่มหน้าเข้าสู่ระบบ"
            : null;
        if (problem is not null)
        {
            await RejectAsync(command, employee, channel, problem, problem);
            return;
        }

        TaskDto? task = null;
        try
        {
            task = await work.CreateTaskAsync(channel.ProjectId!.Value, new SaveTaskRequest(Title(instruction), Description(instruction, trigger, channel)), ct);
            command.TaskId = task.Id;
            await work.AssignAsync(task.Id, new AssignRequest(employeeId), ct);
            var run = await runs.CreateRunAsync(task.Id, employeeId, RunPurpose.Work, ct);
            command.RunId = run.Id;
            await PostAsync(employee, channel, run.IsSimulated,
                $"รับงานแล้วครับ กำลังเริ่มทำ: “{task.Title}”\nสร้างเป็นงานในโปรเจกต์นี้แล้ว ผลลัพธ์จะแจ้งในห้องนี้เมื่อเสร็จ");
        }
        catch (Exception ex) when (ex is DomainException or NotFoundException or ConcurrencyException)
        {
            var where = task is null ? "" : $" (สร้างงาน “{task.Title}” ไว้แล้ว เริ่มใหม่ได้ที่หน้าโปรเจกต์)";
            await RejectAsync(command, employee, channel, $"{ex.Message}{where}", ex.Message);
        }
    }

    /// <summary>When a run started from chat ends, its AI employee reports the outcome in that channel. Called by the run executor.</summary>
    public static async Task ReportRunFinishedAsync(IAppDbContext db, IRealtimeNotifier notifier, TimeProvider clock, AgentRun run)
    {
        var command = await db.ChatCommands.FirstOrDefaultAsync(c => c.RunId == run.Id && c.Status == ChatCommandStatus.Running);
        if (command is null) return;
        var employee = await db.Employees.AsNoTracking().FirstAsync(e => e.Id == run.EmployeeId);
        var channel = await db.ChatChannels.AsNoTracking().FirstAsync(c => c.Id == command.ChannelId);
        var task = await db.Tasks.AsNoTracking().FirstAsync(t => t.Id == run.TaskId);
        var deliverables = await db.Deliverables.AsNoTracking().Where(d => d.RunId == run.Id).Select(d => d.Title).ToListAsync();
        var last = await db.AgentRunSteps.AsNoTracking().Where(s => s.RunId == run.Id && s.Kind == RunStepKind.AssistantText)
            .OrderByDescending(s => s.Sequence).Select(s => s.Content).FirstOrDefaultAsync();

        var sb = new StringBuilder(run.Status switch
        {
            AgentRunStatus.Succeeded => $"✅ ทำงาน “{task.Title}” เสร็จแล้วครับ",
            AgentRunStatus.Cancelled => $"⏹ งาน “{task.Title}” ถูกยกเลิก",
            AgentRunStatus.LimitReached => $"⚠️ งาน “{task.Title}” หยุดเพราะ{LimitText(run.StopReason)}",
            _ => $"❌ ทำงาน “{task.Title}” ไม่สำเร็จ",
        });
        if (run.Error is { Length: > 0 } error) sb.Append("\nสาเหตุ: ").Append(error.Length > 300 ? error[..300] + "…" : error);
        if (last is { Length: > 0 }) sb.Append("\n\n").Append(last.Length > 1500 ? last[..1500] + "…" : last);
        if (deliverables.Count > 0) sb.Append("\n\nผลงาน: ").Append(string.Join(", ", deliverables.Select(d => $"“{d}”")));
        sb.Append($"\n\nค่าใช้จ่าย {run.CostUsd:0.####} USD · ดูรายละเอียด ตรวจ และอนุมัติได้ที่หน้าโปรเจกต์ (สถานะงาน: {StatusText(task.Status)})");

        command.Status = ChatCommandStatus.Finished;
        command.Error = run.Error is { Length: > 1000 } e ? e[..1000] : run.Error;
        command.FinishedAt = clock.GetUtcNow();
        var message = NewMessage(employee, channel, run.IsSimulated, sb.ToString());
        db.ChatMessages.Add(message);
        await db.SaveChangesAsync(CancellationToken.None);
        await notifier.ChatMessageAsync(channel.Id, message.Id, CancellationToken.None);
    }

    /// <summary>Task status in the words the task board uses.</summary>
    public static string StatusText(Domain.Work.WorkTaskStatus status) => status switch
    {
        Domain.Work.WorkTaskStatus.Todo => "รอเริ่ม",
        Domain.Work.WorkTaskStatus.Assigned => "มอบหมายแล้ว",
        Domain.Work.WorkTaskStatus.InProgress => "กำลังทำ",
        Domain.Work.WorkTaskStatus.Blocked => "ติดขัด",
        Domain.Work.WorkTaskStatus.InReview => "รอตรวจ",
        Domain.Work.WorkTaskStatus.WaitingApproval => "รออนุมัติ",
        Domain.Work.WorkTaskStatus.Done => "เสร็จ",
        Domain.Work.WorkTaskStatus.Failed => "ล้มเหลว",
        Domain.Work.WorkTaskStatus.Cancelled => "ยกเลิก",
        _ => status.ToString(),
    };

    private static string LimitText(string? stopReason) => stopReason switch
    {
        "timeout" => "ใช้เวลานานเกินกำหนด",
        "turns" => "คุยกับ AI ครบจำนวนรอบที่กำหนด",
        "tool_calls" => "ใช้เครื่องมือครบจำนวนครั้งที่กำหนด",
        "tokens" => "ใช้ token ครบตามที่กำหนด",
        "max_tokens" => "คำตอบของ AI ยาวเกินที่กำหนด",
        "budget" => "งบ AI ไม่พอ",
        _ => $"ถึงขีดจำกัด ({stopReason})",
    };

    private static string Title(string instruction)
    {
        var line = instruction.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? instruction;
        return line.Length > MaxTitle ? line[..(MaxTitle - 1)] + "…" : line;
    }

    private static string Description(string instruction, ChatMessage trigger, ChatChannel channel) =>
        $"{instruction}\n\n— สั่งงานจากแชท #{channel.Name} โดย {trigger.AuthorName}";

    private async Task RejectAsync(ChatCommand command, Domain.Organization.EmployeeProfile employee, ChatChannel channel, string text, string error)
    {
        command.Status = ChatCommandStatus.Rejected;
        command.Error = error.Length > 1000 ? error[..1000] : error;
        command.FinishedAt = clock.GetUtcNow();
        await PostAsync(employee, channel, false, $"ขอโทษครับ เริ่มงานไม่ได้: {text}");
    }

    private async Task PostAsync(Domain.Organization.EmployeeProfile employee, ChatChannel channel, bool simulated, string text)
    {
        var message = NewMessage(employee, channel, simulated, text);
        db.ChatMessages.Add(message);
        await db.SaveChangesAsync(CancellationToken.None);
        await notifier.ChatMessageAsync(channel.Id, message.Id, CancellationToken.None);
    }

    private static ChatMessage NewMessage(Domain.Organization.EmployeeProfile employee, ChatChannel channel, bool simulated, string text) => new()
    {
        ChannelId = channel.Id, AuthorEmployeeId = employee.Id, AuthorUserId = employee.UserId, AuthorName = employee.DisplayName,
        IsAgent = true, IsSimulated = simulated, Body = text.Length > ChatService.MaxBody ? text[..(ChatService.MaxBody - 1)] + "…" : text,
    };
}
