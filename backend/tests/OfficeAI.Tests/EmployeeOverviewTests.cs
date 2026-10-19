using System.Net;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Organization;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Security;
using OfficeAI.Domain.Workflows;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class EmployeeOverviewTests(ApiFixture fx)
{
    [Fact]
    public async Task Overview_shows_role_assignments_deliverables_and_quality()
    {
        var ceo = await fx.CeoAsync();
        var role = (await ceo.GetAsync<List<RoleDefinitionDto>>("/api/role-definitions")).First(r => r.Code == "BACKEND");
        var agent = await ceo.PostAsync<EmployeeDto>("/api/employees",
            new { displayName = $"Ov {Guid.NewGuid():N}"[..20], kind = "AI", departmentId = role.DepartmentId, roleDefinitionId = role.Id });
        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"OV-{Guid.NewGuid():N}"[..14], name = "Overview", status = "Active" });
        var open = await ceo.PostAsync<TaskDto>($"/api/projects/{project.Id}/tasks", new { title = "Open work", priority = "High" });
        var reviewed = await ceo.PostAsync<TaskDto>($"/api/projects/{project.Id}/tasks", new { title = "Reviewed work" });
        await ceo.PostAsync<TaskDto>($"/api/tasks/{open.Id}/assign", new { employeeId = agent.Id });
        await ceo.PostAsync<TaskDto>($"/api/tasks/{reviewed.Id}/assign", new { employeeId = agent.Id });
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Deliverables.Add(new Deliverable { TaskId = reviewed.Id, EmployeeId = agent.Id, Title = "API draft", Content = "…", Status = DeliverableStatus.Accepted });
            // Changes requested once, then approved: not a first-pass approval.
            db.Reviews.AddRange(
                new Review { TaskId = reviewed.Id, Outcome = ReviewOutcome.ChangesRequested, Comments = "fix" },
                new Review { TaskId = reviewed.Id, Outcome = ReviewOutcome.Approved, Comments = "ok" });
            await db.SaveChangesAsync();
        }

        var o = await ceo.GetAsync<EmployeeOverviewDto>($"/api/employees/{agent.Id}/overview");
        Assert.NotEmpty(o.Skills);
        Assert.Equal(2, o.Assignments.Count);
        Assert.Equal(open.Id, o.Assignments[0].Id); // high priority first
        Assert.Equal(project.Code, o.Assignments[0].ProjectCode);
        Assert.Equal("API draft", Assert.Single(o.Deliverables).Title);
        Assert.Equal(1, o.Quality.ReviewsApproved);
        Assert.Equal(1, o.Quality.ChangesRequested);
        Assert.Equal(0m, o.Quality.FirstPassApprovalRate);
        Assert.Equal(1, o.Quality.DeliverablesAccepted);
        Assert.Equal(0, o.Quality.Runs);

        // Assignments in projects the caller can't see are left out.
        var (member, _) = await fx.PersonAsync([SystemRoles.Member]);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/api/employees/{agent.Id}/overview")).StatusCode);
        Assert.Empty((await member.GetAsync<EmployeeOverviewDto>($"/api/employees/{agent.Id}/overview")).Assignments);
    }
}
