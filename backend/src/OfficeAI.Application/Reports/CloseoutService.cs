using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Application.Delivery;
using OfficeAI.Application.Knowledge;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Code;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Delivery;
using OfficeAI.Domain.Finance;
using OfficeAI.Domain.Knowledge;
using OfficeAI.Domain.Work;
using OfficeAI.Domain.Workflows;

namespace OfficeAI.Application.Reports;

public record CloseoutTasksDto(int Total, int Done, int Open, int Overdue, int Failed, int Cancelled);
public record CloseoutMilestoneDto(string Name, DateOnly? DueDate, int Tasks, int Done);
public record CloseoutEffortDto(decimal? PlannedHours, int AiRuns, int AiRunsSucceeded, int AiRunsFailed, int SimulatedRuns, decimal AiRunHours, long Tokens);
public record CloseoutQualityDto(int TestRuns, int TestsPassed, int MergeRequestsMerged, int MergeRequestsOpen, int ReviewsApproved,
    int ChangesRequested, int DeliverablesAccepted, int DeliverablesRejected);
public record CloseoutKnowledgeDto(int LessonsProposed, int LessonsValidated, int Documents, int FinalDocuments);
public record CloseoutDto(Guid ProjectId, string Code, string Name, ProjectStatus Status, DateOnly? StartDate, DateOnly? DueDate, DateTimeOffset GeneratedAt,
    CloseoutTasksDto Tasks, List<CloseoutMilestoneDto> Milestones, CloseoutEffortDto Effort, ProfitabilityRowDto? Money, decimal ActualAiUsd,
    CloseoutQualityDto Quality, CloseoutKnowledgeDto Knowledge, List<string> Warnings);
public record CompletionReportRequest(string Lang = "th");

/// <summary>
/// Stage 8 of the delivery workflow: planned versus actual for one project, from recorded data only. Human effort is not
/// recorded in the system, so it is never reported as actual. The completion report draft is generated from these figures.
/// </summary>
public class CloseoutService(IAppDbContext db, TimeProvider clock)
{
    public async Task<CloseoutDto> GetAsync(Guid projectId, CancellationToken ct)
    {
        var p = await db.Projects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == projectId, ct) ?? throw new NotFoundException("Project", projectId);
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var tasks = await db.Tasks.AsNoTracking().Where(t => t.ProjectId == projectId)
            .Select(t => new { t.Id, t.Status, t.DueDate, t.MilestoneId }).ToListAsync(ct);
        var live = tasks.Where(t => t.Status != WorkTaskStatus.Cancelled).ToList();
        var done = live.Count(t => t.Status == WorkTaskStatus.Done);
        var taskDto = new CloseoutTasksDto(live.Count, done, live.Count(t => t.Status is not (WorkTaskStatus.Done or WorkTaskStatus.Failed)),
            live.Count(t => t.Status != WorkTaskStatus.Done && t.DueDate is { } d && d < today),
            live.Count(t => t.Status == WorkTaskStatus.Failed), tasks.Count - live.Count);

        var milestones = (await db.Milestones.AsNoTracking().Where(m => m.ProjectId == projectId).OrderBy(m => m.SortOrder).ThenBy(m => m.DueDate).ToListAsync(ct))
            .Select(m => new CloseoutMilestoneDto(m.Name, m.DueDate, live.Count(t => t.MilestoneId == m.Id), live.Count(t => t.MilestoneId == m.Id && t.Status == WorkTaskStatus.Done)))
            .ToList();

        var runs = await db.AgentRuns.AsNoTracking().Where(r => r.ProjectId == projectId)
            .Select(r => new { r.Status, r.IsSimulated, r.StartedAt, r.FinishedAt, Tokens = r.InputTokens + r.OutputTokens }).ToListAsync(ct);
        var runHours = runs.Where(r => r.StartedAt != null && r.FinishedAt != null).Sum(r => (decimal)(r.FinishedAt!.Value - r.StartedAt!.Value).TotalHours);

        var estimate = await db.Estimates.AsNoTracking().Where(e => e.ProjectId == projectId && e.Status == EstimateStatus.Approved)
            .OrderByDescending(e => e.Revision).FirstOrDefaultAsync(ct);
        var actualUsd = await db.CostRecords.Where(c => c.Kind == CostKind.Actual && c.ProjectId == projectId).SumAsync(c => (decimal?)c.AmountUsd, ct) ?? 0m;
        var settings = await DeliveryService.SettingsAsync(db, ct);
        var money = estimate is null ? null : ReportsService.Forecast(p, estimate, actualUsd, settings.UsdToThb);
        var effort = new CloseoutEffortDto(estimate?.TotalHours, runs.Count, runs.Count(r => r.Status == AgentRunStatus.Succeeded),
            runs.Count(r => r.Status is AgentRunStatus.Failed or AgentRunStatus.LimitReached), runs.Count(r => r.IsSimulated),
            Math.Round(runHours, 2, MidpointRounding.AwayFromZero), runs.Sum(r => r.Tokens));

        var taskIds = tasks.Select(t => t.Id).ToList();
        var repoIds = db.CodeRepositories.Where(r => r.ProjectId == projectId).Select(r => r.Id);
        var tests = await db.TestRuns.AsNoTracking().Where(t => repoIds.Contains(t.RepositoryId)).Select(t => t.Status).ToListAsync(ct);
        var mrs = await db.MergeRequests.AsNoTracking().Where(m => m.ProjectId == projectId).Select(m => m.Status).ToListAsync(ct);
        var reviews = await db.Reviews.AsNoTracking().Where(r => taskIds.Contains(r.TaskId)).Select(r => r.Outcome).ToListAsync(ct);
        var deliverables = await db.Deliverables.AsNoTracking().Where(d => taskIds.Contains(d.TaskId)).Select(d => d.Status).ToListAsync(ct);
        var quality = new CloseoutQualityDto(tests.Count, tests.Count(s => s == TestRunStatus.Passed),
            mrs.Count(s => s == MergeRequestStatus.Merged), mrs.Count(s => s is MergeRequestStatus.Open or MergeRequestStatus.Conflict),
            reviews.Count(o => o == ReviewOutcome.Approved), reviews.Count(o => o == ReviewOutcome.ChangesRequested),
            deliverables.Count(s => s == DeliverableStatus.Accepted), deliverables.Count(s => s == DeliverableStatus.Rejected));

        var lessons = await db.KnowledgeEntries.AsNoTracking().Where(k => k.ProjectId == projectId && k.Kind == KnowledgeKind.Lesson)
            .Select(k => k.Status).ToListAsync(ct);
        var docs = await db.Documents.AsNoTracking().Where(d => d.ProjectId == projectId).Select(d => d.Status).ToListAsync(ct);
        var knowledge = new CloseoutKnowledgeDto(lessons.Count(s => s == KnowledgeStatus.Proposed), lessons.Count(s => s == KnowledgeStatus.Validated),
            docs.Count, docs.Count(s => s == DocumentStatus.Final));

        // Codes the UI translates; the report writes them out in the chosen language.
        var warnings = new List<string>();
        if (estimate is null) warnings.Add("noEstimate");
        if (taskDto.Open > 0) warnings.Add("openTasks");
        if (taskDto.Overdue > 0) warnings.Add("overdueTasks");
        if (effort.SimulatedRuns > 0) warnings.Add("simulatedRuns");
        if (quality.MergeRequestsOpen > 0) warnings.Add("openMergeRequests");
        if (knowledge.LessonsValidated == 0) warnings.Add("noLessons");
        if (money is { AtRisk: true }) warnings.Add("marginAtRisk");

        return new CloseoutDto(p.Id, p.Code, p.Name, p.Status, p.StartDate, p.DueDate, now, taskDto, milestones, effort, money, actualUsd,
            quality, knowledge, warnings);
    }

    /// <summary>Writes a new draft version of the project's completion report from the closeout figures.</summary>
    public async Task<DocumentDto> WriteCompletionReportAsync(Guid projectId, CompletionReportRequest req, CancellationToken ct)
    {
        var c = await GetAsync(projectId, ct);
        var th = req.Lang != "en";
        var doc = await KnowledgeService.AddVersionAsync(db, projectId, DocumentKind.CompletionReport,
            th ? $"รายงานปิดโปรเจกต์ {c.Code}" : $"Completion report {c.Code}", CompletionReport.Markdown(c, th), ct);
        await db.SaveChangesAsync(ct);
        return new DocumentDto(doc.Id, doc.ProjectId, doc.Kind, doc.Title, doc.Revision, doc.Status, null, doc.IsSimulated, doc.CreatedAt, doc.Content);
    }
}

/// <summary>Markdown for the completion report. Pure function of the closeout figures, so the same data gives the same report.</summary>
public static class CompletionReport
{
    public static string Markdown(CloseoutDto c, bool th)
    {
        string T(string thai, string en) => th ? thai : en;
        static string N(decimal v) => v.ToString("#,0.00", CultureInfo.InvariantCulture);
        static string D(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—";
        var sb = new StringBuilder();
        sb.AppendLine($"# {T("รายงานปิดโปรเจกต์", "Completion report")}: {c.Code} {c.Name}");
        sb.AppendLine();
        sb.AppendLine(T($"สร้างจากข้อมูลในระบบเมื่อ {c.GeneratedAt:yyyy-MM-dd HH:mm} UTC · ตัวเลขทั้งหมดคำนวณจากข้อมูลที่บันทึกไว้ ไม่ได้ประมาณใหม่",
            $"Generated from recorded data at {c.GeneratedAt:yyyy-MM-dd HH:mm} UTC · every figure is calculated from what the system recorded, not re-estimated"));
        sb.AppendLine();
        sb.AppendLine($"- {T("สถานะ", "Status")}: {c.Status}");
        sb.AppendLine($"- {T("ช่วงเวลาตามแผน", "Planned dates")}: {D(c.StartDate)} → {D(c.DueDate)}");
        sb.AppendLine();

        sb.AppendLine($"## {T("งาน", "Tasks")}");
        sb.AppendLine();
        sb.AppendLine($"| {T("ทั้งหมด", "Total")} | {T("เสร็จ", "Done")} | {T("ค้าง", "Open")} | {T("เลยกำหนด", "Overdue")} | {T("ล้มเหลว", "Failed")} | {T("ยกเลิก", "Cancelled")} |");
        sb.AppendLine("| ---: | ---: | ---: | ---: | ---: | ---: |");
        sb.AppendLine($"| {c.Tasks.Total} | {c.Tasks.Done} | {c.Tasks.Open} | {c.Tasks.Overdue} | {c.Tasks.Failed} | {c.Tasks.Cancelled} |");
        if (c.Milestones.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"| Milestone | {T("กำหนด", "Due")} | {T("งานเสร็จ", "Tasks done")} |");
            sb.AppendLine("| --- | --- | ---: |");
            foreach (var m in c.Milestones) sb.AppendLine($"| {Cell(m.Name)} | {D(m.DueDate)} | {m.Done}/{m.Tasks} |");
        }
        sb.AppendLine();

        sb.AppendLine($"## {T("แรงงานและค่าใช้จ่าย: แผนเทียบกับจริง", "Effort and cost: planned versus actual")}");
        sb.AppendLine();
        if (c.Money is { } m2)
        {
            sb.AppendLine($"| | {T("แผน (ใบประเมิน rev", "Planned (estimate rev")} {m2.EstimateRevision}) | {T("จริง / คาดการณ์", "Actual / forecast")} |");
            sb.AppendLine("| --- | ---: | ---: |");
            sb.AppendLine($"| {T("ชั่วโมงทำงาน", "Hours")} | {N(c.Effort.PlannedHours ?? 0)} | {T("ไม่ได้บันทึกในระบบ", "not recorded")} |");
            sb.AppendLine($"| {T("ค่า AI (THB)", "AI cost (THB)")} | {N(m2.PlannedAiThb)} | {N(m2.ActualAiThb)} |");
            sb.AppendLine($"| {T("ต้นทุนรวม (THB)", "Total cost (THB)")} | {N(m2.PlannedCostThb)} | {N(m2.ForecastCostThb)} |");
            sb.AppendLine($"| {T("ราคา (THB)", "Price (THB)")} | {N(m2.PriceThb)} | {N(m2.PriceThb)} |");
            sb.AppendLine($"| {T("กำไรขั้นต้น (THB)", "Gross margin (THB)")} | {N(m2.PlannedMarginThb)} ({N(m2.PlannedMarginPercent)}%) | {N(m2.ForecastMarginThb)} ({N(m2.ForecastMarginPercent)}%) |");
            sb.AppendLine();
            sb.AppendLine(T("ต้นทุนรวมฝั่งจริงคือต้นทุนตามแผนบวกค่า AI จริงส่วนที่เกินแผน ค่าแรงคนยังใช้ตัวเลขตามแผนเพราะระบบไม่ได้บันทึกชั่วโมงจริง",
                "Actual total cost is the planned cost plus actual AI spend above plan; labour stays at plan because actual hours are not recorded."));
        }
        else
        {
            sb.AppendLine(T("ไม่มีใบประเมินที่อนุมัติแล้ว จึงเทียบกับแผนไม่ได้", "There is no approved estimate, so nothing to compare against."));
            sb.AppendLine();
            sb.AppendLine($"- {T("ค่า AI จริง", "Actual AI cost")}: {N(c.ActualAiUsd)} USD");
        }
        sb.AppendLine();
        sb.AppendLine($"- {T("งาน AI", "AI runs")}: {c.Effort.AiRuns} ({T("สำเร็จ", "succeeded")} {c.Effort.AiRunsSucceeded}, {T("ไม่สำเร็จ", "failed")} {c.Effort.AiRunsFailed}) · {N(c.Effort.AiRunHours)} {T("ชั่วโมง", "hours")} · {c.Effort.Tokens:N0} tokens");
        if (c.Effort.SimulatedRuns > 0)
            sb.AppendLine($"- ⚠ {T($"มีงานจำลอง (simulated) {c.Effort.SimulatedRuns} รอบ ไม่ใช่งานจริง", $"{c.Effort.SimulatedRuns} runs used the simulated provider and are not real work")}");
        sb.AppendLine();

        sb.AppendLine($"## {T("คุณภาพ", "Quality")}");
        sb.AppendLine();
        sb.AppendLine($"- {T("เทสต์ผ่าน", "Test runs passed")}: {c.Quality.TestsPassed}/{c.Quality.TestRuns}");
        sb.AppendLine($"- Merge requests: {T("merge แล้ว", "merged")} {c.Quality.MergeRequestsMerged}, {T("ยังเปิด", "still open")} {c.Quality.MergeRequestsOpen}");
        sb.AppendLine($"- {T("รีวิว", "Reviews")}: {T("ผ่าน", "approved")} {c.Quality.ReviewsApproved}, {T("ขอให้แก้", "changes requested")} {c.Quality.ChangesRequested}");
        sb.AppendLine($"- {T("ผลงานส่งมอบ", "Deliverables")}: {T("รับ", "accepted")} {c.Quality.DeliverablesAccepted}, {T("ไม่รับ", "rejected")} {c.Quality.DeliverablesRejected}");
        sb.AppendLine();

        sb.AppendLine($"## {T("ประเด็นที่ยังค้าง", "Open issues")}");
        sb.AppendLine();
        var issues = c.Warnings.Select(w => w switch
        {
            "noEstimate" => T("ไม่มีใบประเมินที่อนุมัติแล้ว", "No approved estimate"),
            "openTasks" => T($"งานยังไม่เสร็จ {c.Tasks.Open} งาน", $"{c.Tasks.Open} tasks are not finished"),
            "overdueTasks" => T($"งานเลยกำหนด {c.Tasks.Overdue} งาน", $"{c.Tasks.Overdue} tasks are overdue"),
            "simulatedRuns" => T("มีงานจากผู้ให้บริการจำลอง", "Some work came from the simulated provider"),
            "openMergeRequests" => T($"merge request ยังเปิด {c.Quality.MergeRequestsOpen} รายการ", $"{c.Quality.MergeRequestsOpen} merge requests are still open"),
            "noLessons" => T("ยังไม่มีบทเรียนที่ผ่านการตรวจ", "No validated lessons yet"),
            "marginAtRisk" => T("กำไรคาดการณ์ต่ำกว่าครึ่งของแผนหรือติดลบ", "Forecast margin is below half of plan or negative"),
            _ => w,
        }).ToList();
        if (issues.Count == 0) sb.AppendLine(T("- ไม่มี", "- None"));
        foreach (var i in issues) sb.AppendLine($"- {i}");
        sb.AppendLine();

        sb.AppendLine($"## {T("บทเรียน", "Lessons learned")}");
        sb.AppendLine();
        sb.AppendLine(T($"บทเรียนของโปรเจกต์นี้: ผ่านการตรวจ {c.Knowledge.LessonsValidated} รอตรวจ {c.Knowledge.LessonsProposed} — ดูและตรวจได้ที่หน้าความรู้ เฉพาะบทเรียนที่ผ่านการตรวจเท่านั้นที่เป็นความรู้ของบริษัท",
            $"Lessons for this project: {c.Knowledge.LessonsValidated} validated, {c.Knowledge.LessonsProposed} awaiting review — see the Knowledge page. Only validated lessons count as company knowledge."));
        return sb.ToString();
    }

    private static string Cell(string s) => s.Replace("|", "\\|").Replace("\n", " ");
}
