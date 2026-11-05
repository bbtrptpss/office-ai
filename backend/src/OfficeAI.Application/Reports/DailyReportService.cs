using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OfficeAI.Application.Abstractions;
using OfficeAI.Application.Communication;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Communication;
using OfficeAI.Domain.Organization;
using OfficeAI.Domain.Work;
using OfficeAI.Domain.Workflows;

namespace OfficeAI.Application.Reports;

public class DailyReportOptions
{
    /// <summary>Post the report automatically after midnight (turned off in tests).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The day starts and ends at midnight in this time zone.</summary>
    public string TimeZone { get; set; } = "Asia/Bangkok";
}

public record DailyReportDto(DateOnly Day, string Text, int TasksDone, int Deliverables, int Runs, int FailedRuns, int PendingApprovals, decimal? CostUsd);

/// <summary>
/// What the AI employees did in one day, posted after midnight to the company channel <see cref="Channel"/> with a notification to the people.
/// Built from the records only (no model call), so the numbers are exact and it costs nothing.
/// </summary>
public class DailyReportService(IAppDbContext db, IRealtimeNotifier notifier, TimeProvider clock, IOptions<DailyReportOptions> options)
{
    public const string Channel = "รายงานประจำวัน";
    public const string AuthorName = "Office AI";
    private const int ListLimit = 12;

    public TimeZoneInfo Zone
    {
        get
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return TimeZoneInfo.CreateCustomTimeZone("UTC+07", TimeSpan.FromHours(7), "UTC+07", "UTC+07"); // Thailand, no daylight saving
            }
        }
    }

    /// <summary>The last full day in the report's time zone.</summary>
    public DateOnly Yesterday => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Zone).DateTime).AddDays(-1);

    private (DateTimeOffset From, DateTimeOffset To) Window(DateOnly day)
    {
        var start = day.ToDateTime(TimeOnly.MinValue);
        var offset = Zone.GetUtcOffset(start);
        var from = new DateTimeOffset(start, offset).ToUniversalTime();
        return (from, from.AddDays(1));
    }

    private static string Header(DateOnly day) => $"# รายงานประจำวัน {day:yyyy-MM-dd}";

    public async Task<DailyReportDto> BuildAsync(DateOnly day, CancellationToken ct, bool includeCost = true)
    {
        var (from, to) = Window(day);
        var names = await db.Employees.AsNoTracking().ToDictionaryAsync(e => e.Id, e => e.DisplayName, ct);
        var projects = await db.Projects.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Code, ct);

        var done = await db.Tasks.AsNoTracking()
            .Where(t => t.Status == WorkTaskStatus.Done && (t.UpdatedAt ?? t.CreatedAt) >= from && (t.UpdatedAt ?? t.CreatedAt) < to)
            .OrderBy(t => t.UpdatedAt).Select(t => new { t.Title, t.ProjectId }).ToListAsync(ct);
        var deliverables = await db.Deliverables.AsNoTracking().Where(d => d.CreatedAt >= from && d.CreatedAt < to && !d.IsSimulated)
            .OrderBy(d => d.CreatedAt).Select(d => new { d.EmployeeId, d.Title }).ToListAsync(ct);
        var runs = await db.AgentRuns.AsNoTracking().Where(r => r.FinishedAt >= from && r.FinishedAt < to)
            .Select(r => new { r.EmployeeId, r.Status, r.Error, r.TaskId }).ToListAsync(ct);
        var failedRuns = runs.Where(r => r.Status is AgentRunStatus.Failed or AgentRunStatus.LimitReached).ToList();
        var failedTitles = await db.Tasks.AsNoTracking().Where(t => failedRuns.Select(r => r.TaskId).Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Title, ct);
        var started = await db.WorkflowExecutions.AsNoTracking().Where(w => w.CreatedAt >= from && w.CreatedAt < to).Select(w => w.Objective).ToListAsync(ct);
        var finished = await db.WorkflowExecutions.AsNoTracking().Where(w => w.CompletedAt >= from && w.CompletedAt < to)
            .Select(w => new { w.Objective, w.Status }).ToListAsync(ct);
        var pending = await db.ApprovalRequests.AsNoTracking().Where(a => a.Status == ApprovalStatus.Pending)
            .OrderBy(a => a.CreatedAt).Select(a => a.Title).ToListAsync(ct);
        var blocked = await db.Tasks.AsNoTracking().Where(t => t.Status == WorkTaskStatus.Blocked).Select(t => new { t.Title, t.ProjectId }).ToListAsync(ct);
        var open = await db.Tasks.AsNoTracking().Where(t => t.Status != WorkTaskStatus.Done && t.Status != WorkTaskStatus.Cancelled)
            .GroupBy(t => t.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var cost = await db.CostRecords.AsNoTracking().Where(c => c.Kind == CostKind.Actual && c.OccurredAt >= from && c.OccurredAt < to).SumAsync(c => (decimal?)c.AmountUsd, ct) ?? 0m;

        string Project(Guid id) => projects.GetValueOrDefault(id, "?");
        string Who(Guid id) => names.GetValueOrDefault(id, "?");
        static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
        static void List(StringBuilder sb, IReadOnlyCollection<string> items)
        {
            foreach (var i in items.Take(ListLimit)) sb.AppendLine($"- {i}");
            if (items.Count > ListLimit) sb.AppendLine($"- …และอีก {items.Count - ListLimit} รายการ");
        }

        var sb = new StringBuilder();
        sb.AppendLine($"{Header(day)} (00:00–24:00 น.)");
        sb.AppendLine();
        sb.AppendLine($"**สรุป:** งานเสร็จ {done.Count} · ส่งมอบ {deliverables.Count} ชิ้น · AI ทำงาน {runs.Count} รอบ (ไม่สำเร็จ {failedRuns.Count}) · รอ CEO อนุมัติ {pending.Count}{(includeCost ? $" · ค่าใช้จ่าย AI ${cost:0.##}" : "")}");

        if (done.Count + deliverables.Count + runs.Count + started.Count + finished.Count == 0)
            sb.AppendLine().AppendLine("วันนี้ไม่มีงานเกิดขึ้น");

        if (started.Count + finished.Count > 0)
        {
            sb.AppendLine().AppendLine("## 🎯 เป้าหมายที่สั่งงาน");
            List(sb, started.Select(o => $"เริ่มใหม่: {Clip(o, 120)}").Concat(finished.Select(w =>
                $"{(w.Status == WorkflowStatus.Completed ? "เสร็จแล้ว" : "จบแบบไม่สำเร็จ")}: {Clip(w.Objective, 120)}")).ToList());
        }
        if (done.Count > 0)
        {
            sb.AppendLine().AppendLine("## ✅ งานที่เสร็จ");
            List(sb, done.Select(t => $"{Clip(t.Title, 120)} ({Project(t.ProjectId)})").ToList());
        }
        if (deliverables.Count > 0)
        {
            sb.AppendLine().AppendLine("## 📦 ผลงานที่ AI ส่งมอบ");
            List(sb, deliverables.GroupBy(d => d.EmployeeId).OrderByDescending(g => g.Count())
                .Select(g => $"{Who(g.Key)}: {g.Count()} ชิ้น — {string.Join(", ", g.Take(3).Select(d => Clip(d.Title, 60)))}{(g.Count() > 3 ? " …" : "")}").ToList());
        }
        if (pending.Count > 0)
        {
            sb.AppendLine().AppendLine("## ⏳ รอ CEO อนุมัติ");
            List(sb, pending.Select(p => Clip(p, 120)).ToList());
        }
        if (blocked.Count + failedRuns.Count > 0)
        {
            sb.AppendLine().AppendLine("## ⛔ ติดขัดหรือไม่สำเร็จ");
            List(sb, blocked.Select(t => $"ติดขัด: {Clip(t.Title, 100)} ({Project(t.ProjectId)})")
                .Concat(failedRuns.Select(r => $"{Who(r.EmployeeId)} ทำ \"{Clip(failedTitles.GetValueOrDefault(r.TaskId, "?"), 60)}\" ไม่สำเร็จ: {Clip(r.Error ?? r.Status.ToString(), 100)}")).ToList());
        }
        if (open.Count > 0)
            sb.AppendLine().AppendLine("## 🔜 งานที่ยังเปิดอยู่").AppendLine(string.Join(" · ", open.OrderBy(o => o.Key).Select(o => $"{ChatCommandService.StatusText(o.Key)} {o.Count}")));

        var text = sb.ToString().TrimEnd();
        if (text.Length > ChatService.MaxBody) text = text[..(ChatService.MaxBody - 40)] + "\n…(ตัดเพราะยาวเกิน ดูรายละเอียดในระบบ)";
        return new DailyReportDto(day, text, done.Count, deliverables.Count, runs.Count, failedRuns.Count, pending.Count, includeCost ? cost : null);
    }

    /// <summary>Posts the day's report once (a second call for the same day does nothing). Returns whether it posted.</summary>
    public async Task<bool> PublishAsync(DateOnly day, CancellationToken ct)
    {
        var channel = await db.ChatChannels.FirstOrDefaultAsync(c => c.Kind == ChannelKind.Company && c.Name == Channel, ct);
        if (channel is null)
        {
            channel = new ChatChannel { Kind = ChannelKind.Company, Name = Channel, Description = "สรุปงานของพนักงาน AI ทุกวัน หลังเที่ยงคืน" };
            db.ChatChannels.Add(channel);
            await db.SaveChangesAsync(ct);
        }
        var header = Header(day);
        if (await db.ChatMessages.AnyAsync(m => m.ChannelId == channel.Id && m.Body.StartsWith(header), ct)) return false;

        var report = await BuildAsync(day, ct);
        var message = new ChatMessage { ChannelId = channel.Id, AuthorName = AuthorName, IsAgent = true, Body = report.Text };
        db.ChatMessages.Add(message);
        var people = await db.Employees.AsNoTracking().Where(e => e.Kind == EmployeeKind.Human && e.Status != EmployeeStatus.Inactive).Select(e => e.Id).ToListAsync(ct);
        var users = await NotificationService.AddForEmployeesAsync(db, people, null, "daily_report", $"รายงานประจำวัน {day:yyyy-MM-dd}",
            $"งานเสร็จ {report.TasksDone} · ส่งมอบ {report.Deliverables} · ไม่สำเร็จ {report.FailedRuns} · รออนุมัติ {report.PendingApprovals}",
            $"/chat?channel={channel.Id}", clock.GetUtcNow(), ct);
        await db.SaveChangesAsync(ct);
        await notifier.ChatMessageAsync(channel.Id, message.Id, ct);
        await notifier.NotificationsChangedAsync(users, ct);
        return true;
    }
}
