using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Work;
using OfficeAI.Domain.Workflows;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class ProjectCancelTests(ApiFixture fx)
{
    [Fact]
    public async Task Cancelling_a_project_cancels_open_tasks_stops_runs_and_withdraws_approvals()
    {
        var ceo = await fx.CeoAsync();
        var code = $"PC-{Guid.NewGuid():N}"[..14];
        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code, name = "To cancel", status = "Active" });
        var open = await ceo.PostAsync<TaskDto>($"/api/projects/{project.Id}/tasks", new { title = "Open" });
        var done = await ceo.PostAsync<TaskDto>($"/api/projects/{project.Id}/tasks", new { title = "Finished" });
        var employee = (await ceo.GetAsync<List<Application.Organization.EmployeeDto>>("/api/employees")).First(e => e.Kind == Domain.Organization.EmployeeKind.AI).Id;
        Guid runId, approvalId;
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Tasks.FirstAsync(t => t.Id == done.Id)).Status = WorkTaskStatus.Done;
            var run = new AgentRun { EmployeeId = employee, TaskId = open.Id, ProjectId = project.Id, Status = AgentRunStatus.Running, Provider = "simulated", ModelId = "sim" };
            var approval = new ApprovalRequest
            {
                Kind = ApprovalKind.TaskCompletion, Title = "Finish?", PayloadJson = "{}", PayloadHash = ApprovalRequest.Hash("{}"),
                ProjectId = project.Id, TaskId = open.Id, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            };
            db.AgentRuns.Add(run);
            db.ApprovalRequests.Add(approval);
            await db.SaveChangesAsync();
            (runId, approvalId) = (run.Id, approval.Id);
        }

        var cancelled = await ceo.PutAsync<ProjectDto>($"/api/projects/{project.Id}", new { code, name = "To cancel", status = "Cancelled", version = project.Version });
        Assert.Equal(ProjectStatus.Cancelled, cancelled.Status);

        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(WorkTaskStatus.Cancelled, (await db.Tasks.AsNoTracking().FirstAsync(t => t.Id == open.Id)).Status);
            Assert.Equal(WorkTaskStatus.Done, (await db.Tasks.AsNoTracking().FirstAsync(t => t.Id == done.Id)).Status);
            Assert.True((await db.AgentRuns.AsNoTracking().FirstAsync(r => r.Id == runId)).CancelRequested);
            Assert.Equal(ApprovalStatus.Cancelled, (await db.ApprovalRequests.AsNoTracking().FirstAsync(a => a.Id == approvalId)).Status);
        }
    }
}
