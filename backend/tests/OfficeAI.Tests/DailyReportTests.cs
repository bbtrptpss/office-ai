using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Abstractions;
using OfficeAI.Application.Communication;
using OfficeAI.Application.Reports;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Communication;
using OfficeAI.Domain.Work;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class DailyReportTests(ApiFixture fx)
{
    [Fact]
    public async Task The_daily_report_counts_the_day_posts_once_and_notifies_the_ceo()
    {
        var ceo = await fx.CeoAsync();
        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"DR-{Guid.NewGuid():N}"[..14], name = "Daily report project", status = "Active" });
        var task = await ceo.PostAsync<TaskDto>($"/api/projects/{project.Id}/tasks", new { title = "งานที่เสร็จวันนี้" });

        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reports = scope.ServiceProvider.GetRequiredService<DailyReportService>();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, reports.Zone).DateTime);
        var employee = await db.Employees.OrderBy(e => e.DisplayName).FirstAsync();
        var before = await reports.BuildAsync(today, default); // other tests also work "today"; compare against this
        await db.Tasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, WorkTaskStatus.Done).SetProperty(t => t.UpdatedAt, DateTimeOffset.UtcNow));
        db.Deliverables.Add(new Deliverable { TaskId = task.Id, EmployeeId = employee.Id, Title = "ผลงานทดสอบ", Content = "x" });
        await db.SaveChangesAsync();

        var report = await reports.BuildAsync(today, default);
        Assert.StartsWith($"# รายงานประจำวัน {today:yyyy-MM-dd}", report.Text);
        Assert.Equal(before.TasksDone + 1, report.TasksDone);
        Assert.Equal(before.Deliverables + 1, report.Deliverables);
        Assert.Contains("## ✅ งานที่เสร็จ", report.Text);
        Assert.Contains("## 📦 ผลงานที่ AI ส่งมอบ", report.Text);
        Assert.DoesNotContain("InReview", report.Text); // open tasks use the task board's Thai status names

        using (ActingAs.Use(new ActingIdentity(null, "Daily report", IsAgent: false, "test")))
        {
            Assert.True(await reports.PublishAsync(today, default));
            Assert.False(await reports.PublishAsync(today, default)); // once per day
        }
        var channel = await db.ChatChannels.SingleAsync(c => c.Kind == ChannelKind.Company && c.Name == DailyReportService.Channel);
        Assert.Single(await db.ChatMessages.Where(m => m.ChannelId == channel.Id && m.Body.StartsWith($"# รายงานประจำวัน {today:yyyy-MM-dd}")).ToListAsync());
        var notes = await ceo.GetAsync<NotificationListDto>("/api/notifications");
        Assert.Contains(notes.Items, n => n.Kind == "daily_report" && n.Title.Contains(today.ToString("yyyy-MM-dd")));

        var viaApi = await ceo.GetAsync<DailyReportDto>($"/api/reports/daily?date={today:yyyy-MM-dd}");
        Assert.Equal(report.TasksDone, viaApi.TasksDone);
        Assert.NotNull(viaApi.CostUsd);
        Assert.Contains("ค่าใช้จ่าย AI", viaApi.Text);

        // Finance data needs finance.read: the Member role may read projects, not costs.
        var member = await fx.UserWithRolesAsync(Domain.Security.SystemRoles.Member);
        var limited = await member.GetAsync<DailyReportDto>($"/api/reports/daily?date={today:yyyy-MM-dd}");
        Assert.Null(limited.CostUsd);
        Assert.DoesNotContain("ค่าใช้จ่าย AI", limited.Text);
    }
}
