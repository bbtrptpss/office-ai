using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Knowledge;
using OfficeAI.Application.Organization;
using OfficeAI.Application.Reports;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Delivery;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Finance;
using OfficeAI.Domain.Security;
using OfficeAI.Domain.Knowledge;
using OfficeAI.Domain.Work;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class CloseoutTests(ApiFixture fx)
{
    [Fact]
    public async Task Closeout_compares_plan_with_recorded_data_and_writes_a_versioned_report()
    {
        var ceo = await fx.CeoAsync();
        var role = (await ceo.GetAsync<List<RoleDefinitionDto>>("/api/role-definitions")).First(r => r.Code == "BACKEND");
        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"CL-{Guid.NewGuid():N}"[..14], name = "Closeout project", status = "Active" });
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var milestone = new Milestone { ProjectId = project.Id, Name = "Launch", DueDate = today };
            db.Milestones.Add(milestone);
            db.Tasks.AddRange(
                new WorkTask { ProjectId = project.Id, Title = "Done", Status = WorkTaskStatus.Done, MilestoneId = milestone.Id },
                new WorkTask { ProjectId = project.Id, Title = "Late", Status = WorkTaskStatus.InProgress, DueDate = today.AddDays(-3), MilestoneId = milestone.Id },
                new WorkTask { ProjectId = project.Id, Title = "Dropped", Status = WorkTaskStatus.Cancelled });
            var e = new ProjectEstimate
            {
                ProjectId = project.Id, Revision = 1, Status = EstimateStatus.Approved, ContingencyPercent = 10m, TargetMarginPercent = 30m,
                AiCostUsd = 10m, UsdToThb = (await db.CompanySettings.FirstOrDefaultAsync())?.UsdToThb ?? 36m,
                Items = [new EstimateLineItem { Description = "API", RoleDefinitionId = role.Id, RoleTitle = "Dev", Hours = 100m, RateThb = 1000m, Evidence = "past project" }],
            };
            EstimateCalculator.Compute(e);
            db.Estimates.Add(e);
            db.CostRecords.Add(new CostRecord { Kind = CostKind.Actual, Category = "ai", ProjectId = project.Id, AmountUsd = 20m, OccurredAt = DateTimeOffset.UtcNow });
            db.KnowledgeEntries.Add(new KnowledgeEntry { Kind = KnowledgeKind.Lesson, Title = "Lesson", Body = "Agree scope early", ProjectId = project.Id });
            await db.SaveChangesAsync();
        }

        var c = await ceo.GetAsync<CloseoutDto>($"/api/projects/{project.Id}/closeout");
        Assert.Equal(new CloseoutTasksDto(Total: 2, Done: 1, Open: 1, Overdue: 1, Failed: 0, Cancelled: 1), c.Tasks);
        Assert.Equal(new CloseoutMilestoneDto("Launch", today, 2, 1), Assert.Single(c.Milestones));
        Assert.Equal(100m, c.Effort.PlannedHours);
        Assert.Equal(20m, c.ActualAiUsd);
        var money = Assert.IsType<ProfitabilityRowDto>(c.Money);
        Assert.Equal(money.PlannedCostThb + money.AiOverrunThb, money.ForecastCostThb); // AI spent 20 USD against 10 planned
        Assert.True(money.AiOverrunThb > 0);
        Assert.Equal(1, c.Knowledge.LessonsProposed);
        Assert.Contains("overdueTasks", c.Warnings);
        Assert.Contains("noLessons", c.Warnings); // proposed is not validated
        Assert.DoesNotContain("noEstimate", c.Warnings);

        var first = await ceo.PostAsync<DocumentDto>($"/api/projects/{project.Id}/closeout/report", new { lang = "en" });
        var second = await ceo.PostAsync<DocumentDto>($"/api/projects/{project.Id}/closeout/report", new { lang = "en" });
        Assert.Equal(DocumentKind.CompletionReport, first.Kind);
        Assert.Equal(DocumentStatus.Draft, first.Status);
        Assert.Equal(1, first.Version);
        Assert.Equal(2, second.Version);
        Assert.Contains(project.Code, first.Content);
        Assert.Contains("1 tasks are overdue", first.Content);
        Assert.Contains("not recorded", first.Content); // actual human hours are never invented

        var th = await ceo.PostAsync<DocumentDto>($"/api/projects/{project.Id}/closeout/report", new { lang = "th" });
        Assert.StartsWith("รายงานปิดโปรเจกต์", th.Title);
    }

    [Fact]
    public async Task Closeout_without_estimate_says_so_and_needs_finance_access()
    {
        var ceo = await fx.CeoAsync();
        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"CN-{Guid.NewGuid():N}"[..14], name = "No estimate", status = "Active" });
        var c = await ceo.GetAsync<CloseoutDto>($"/api/projects/{project.Id}/closeout");
        Assert.Null(c.Money);
        Assert.Contains("noEstimate", c.Warnings);
        var report = await ceo.PostAsync<DocumentDto>($"/api/projects/{project.Id}/closeout/report", new { lang = "en" });
        Assert.Contains("no approved estimate", report.Content);

        var member = await fx.UserWithRolesAsync(SystemRoles.Member);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync($"/api/projects/{project.Id}/closeout")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync($"/api/projects/{project.Id}/closeout/report", new { lang = "en" })).StatusCode);
    }
}
