using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Organization;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Organization;
using OfficeAI.Domain.Work;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class StaleFailureTests(ApiFixture fx)
{
    [Fact]
    public async Task Failed_employee_goes_back_to_available_once_the_failed_work_is_cancelled_or_closed()
    {
        var ceo = await fx.CeoAsync();
        var role = (await ceo.GetAsync<List<RoleDefinitionDto>>("/api/role-definitions")).First(r => r.Code == "BACKEND");
        async Task<Guid> Agent() => (await ceo.PostAsync<EmployeeDto>("/api/employees",
            new { displayName = $"Sf {Guid.NewGuid():N}"[..20], kind = "AI", departmentId = role.DepartmentId, roleDefinitionId = role.Id })).Id;
        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"SF-{Guid.NewGuid():N}"[..14], name = "Stale", status = "Active" });
        var closedProject = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"SG-{Guid.NewGuid():N}"[..14], name = "Gone", status = "Active" });
        var cancelledTask = await ceo.PostAsync<TaskDto>($"/api/projects/{project.Id}/tasks", new { title = "Cancelled later" });
        var openTask = await ceo.PostAsync<TaskDto>($"/api/projects/{project.Id}/tasks", new { title = "Still open" });
        var orphanTask = await ceo.PostAsync<TaskDto>($"/api/projects/{closedProject.Id}/tasks", new { title = "Project cancelled" });
        var (cancelled, open, orphan) = (await Agent(), await Agent(), await Agent());

        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var (employee, task, projectId) in new[] { (cancelled, cancelledTask.Id, project.Id), (open, openTask.Id, project.Id), (orphan, orphanTask.Id, closedProject.Id) })
            {
                db.AgentRuns.Add(new AgentRun { EmployeeId = employee, TaskId = task, ProjectId = projectId, Status = AgentRunStatus.Failed, Provider = "simulated", ModelId = "sim" });
                (await db.Employees.FirstAsync(e => e.Id == employee)).Status = EmployeeStatus.Failed;
            }
            (await db.Tasks.FirstAsync(t => t.Id == cancelledTask.Id)).Status = WorkTaskStatus.Cancelled;
            (await db.Projects.FirstAsync(p => p.Id == closedProject.Id)).Status = ProjectStatus.Cancelled;
            await db.SaveChangesAsync();

            Assert.True(await scope.ServiceProvider.GetRequiredService<StaleFailureService>().ClearAsync(CancellationToken.None) >= 2);
        }

        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var status = await db.Employees.AsNoTracking().Where(e => e.Id == cancelled || e.Id == open || e.Id == orphan).ToDictionaryAsync(e => e.Id, e => e.Status);
            Assert.Equal(EmployeeStatus.Available, status[cancelled]);
            Assert.Equal(EmployeeStatus.Available, status[orphan]);
            Assert.Equal(EmployeeStatus.Failed, status[open]); // the failed task can still be retried
        }
    }
}
