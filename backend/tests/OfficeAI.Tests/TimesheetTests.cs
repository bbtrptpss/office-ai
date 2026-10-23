using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Delivery;
using OfficeAI.Application.Organization;
using OfficeAI.Application.Reports;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Delivery;
using OfficeAI.Domain.Security;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

/// <summary>People record their working hours; project closeout and profitability use them as the actual effort.</summary>
[Collection(ApiCollection.Name)]
public class TimesheetTests(ApiFixture fx)
{
    private static string Day(int offset) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");

    /// <summary>A person (role PROCURE, so the shared BACKEND rate is untouched) on one project.</summary>
    private async Task<(HttpClient Ceo, HttpClient Person, Guid EmployeeId, ProjectDto Project, RoleDefinitionDto Role)> SetupAsync()
    {
        var ceo = await fx.CeoAsync();
        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"TS-{Guid.NewGuid():N}"[..14], name = "Timesheet", status = "Active" });
        var person = await fx.UserWithRolesAsync(SystemRoles.Member);
        var me = await person.GetAsync<OfficeAI.Api.Endpoints.MeDto>("/api/auth/me");
        var role = (await ceo.GetAsync<List<RoleDefinitionDto>>("/api/role-definitions")).First(r => r.Code == "PROCURE");
        var employee = await ceo.PostAsync<EmployeeDto>("/api/employees",
            new { displayName = $"Worker {Guid.NewGuid():N}"[..20], kind = "Human", departmentId = role.DepartmentId, roleDefinitionId = role.Id, userId = me.UserId });
        (await ceo.PostAsJsonAsync($"/api/projects/{project.Id}/members/{employee.Id}", new { })).EnsureSuccessStatusCode();
        return (ceo, person, employee.Id, project, role);
    }

    [Fact]
    public async Task People_log_their_own_hours_on_projects_they_can_see()
    {
        var (ceo, person, employeeId, project, _) = await SetupAsync();
        var task = await ceo.PostAsync<TaskDto>($"/api/projects/{project.Id}/tasks", new { title = "Gather quotes" });
        var entry = await person.PostAsync<TimeEntryDto>("/api/timesheet", new { projectId = project.Id, taskId = task.Id, date = Day(0), hours = 6.5m, note = "vendors" });
        Assert.Equal((employeeId, "Gather quotes", 6.5m), (entry.EmployeeId, entry.TaskTitle, entry.Hours));
        Assert.Single(await person.GetAsync<List<TimeEntryDto>>("/api/timesheet"));

        async Task<HttpStatusCode> Post(object body) => (await person.PostAsJsonAsync("/api/timesheet", body, ApiFixture.Json)).StatusCode;
        Assert.Equal(HttpStatusCode.BadRequest, await Post(new { projectId = project.Id, date = Day(0), hours = 18m })); // 24.5 h in one day
        Assert.Equal(HttpStatusCode.BadRequest, await Post(new { projectId = project.Id, date = Day(3), hours = 1m }));  // future
        Assert.Equal(HttpStatusCode.BadRequest, await Post(new { projectId = project.Id, date = Day(-1), hours = 1.1m })); // quarter hours only
        var other = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"TX-{Guid.NewGuid():N}"[..14], name = "Not mine", status = "Active" });
        Assert.Equal(HttpStatusCode.NotFound, await Post(new { projectId = other.Id, date = Day(0), hours = 1m })); // outside the person's projects

        // Only the author changes an entry; the CEO (no employee profile of their own here) cannot edit it.
        var edited = await person.PutAsync<TimeEntryDto>($"/api/timesheet/{entry.Id}", new { projectId = project.Id, date = Day(-1), hours = 7m, entry.Version });
        Assert.Equal(7m, edited.Hours);
        Assert.NotEqual(HttpStatusCode.OK, (await ceo.PutAsJsonAsync($"/api/timesheet/{entry.Id}", new { projectId = project.Id, date = Day(-1), hours = 1m }, ApiFixture.Json)).StatusCode);

        var hours = await ceo.GetAsync<ProjectHoursDto>($"/api/projects/{project.Id}/hours");
        Assert.Equal(7m, hours.TotalHours);
        Assert.Equal(employeeId, Assert.Single(hours.People).EmployeeId);

        (await person.DeleteAsync($"/api/timesheet/{entry.Id}")).EnsureSuccessStatusCode();
        Assert.Empty(await person.GetAsync<List<TimeEntryDto>>("/api/timesheet"));
    }

    [Fact]
    public async Task Closeout_and_profitability_use_logged_hours_and_labour_above_plan()
    {
        var (ceo, person, _, project, role) = await SetupAsync();
        decimal plannedLabour;
        decimal? oldRate;
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            oldRate = (await db.RoleRates.FirstOrDefaultAsync(r => r.RoleDefinitionId == role.Id))?.HourlyRateThb;
            var e = new ProjectEstimate
            {
                ProjectId = project.Id, Revision = 1, Status = EstimateStatus.Approved, Assumptions = "fixed", ContingencyPercent = 0m, TargetMarginPercent = 30m,
                AiCostUsd = 0m, UsdToThb = (await db.CompanySettings.FirstOrDefaultAsync())?.UsdToThb ?? 36m,
                Items = [new EstimateLineItem { Description = "Sourcing", RoleDefinitionId = role.Id, RoleTitle = role.Title, Hours = 10m, RateThb = 500m, Evidence = "past" }],
            };
            EstimateCalculator.Compute(e);
            plannedLabour = e.LaborCost;
            db.Estimates.Add(e);
            await db.SaveChangesAsync();
        }

        var before = await ceo.GetAsync<CloseoutDto>($"/api/projects/{project.Id}/closeout");
        Assert.Null(before.Effort.ActualHours); // nothing logged yet: "none recorded", never a guess

        try
        {
            (await ceo.PutAsJsonAsync($"/api/rate-card/{role.Id}", new { hourlyRateThb = 500m }, ApiFixture.Json)).EnsureSuccessStatusCode();
            foreach (var (day, h) in new[] { (0, 8m), (-1, 8m) }) // 16 h logged against 10 h planned
                await person.PostAsync<TimeEntryDto>("/api/timesheet", new { projectId = project.Id, date = Day(day), hours = h });

            var closeout = await ceo.GetAsync<CloseoutDto>($"/api/projects/{project.Id}/closeout");
            Assert.Equal((16m, 8000m, 1, plannedLabour), (closeout.Effort.ActualHours, closeout.Effort.ActualLabourThb!.Value, closeout.Effort.People, closeout.Effort.PlannedLabourThb!.Value));
            Assert.Equal(8000m - plannedLabour, closeout.Money!.LabourOverrunThb);

            var row = Assert.Single((await ceo.GetAsync<ProfitabilityReportDto>("/api/reports/profitability")).Projects, p => p.ProjectId == project.Id);
            Assert.Equal(row.PlannedCostThb + row.LabourOverrunThb, row.ForecastCostThb);

            var report = await ceo.PostAsync<OfficeAI.Application.Knowledge.DocumentDto>($"/api/projects/{project.Id}/closeout/report", new { lang = "en" });
            Assert.Contains("| Hours | 10.00 | 16.00 (1 people) |", report.Content);
            Assert.Contains("Labour above plan", report.Content);
        }
        finally
        {
            (await ceo.PutAsJsonAsync($"/api/rate-card/{role.Id}", new { hourlyRateThb = oldRate ?? 0m }, ApiFixture.Json)).EnsureSuccessStatusCode();
        }
    }
}
