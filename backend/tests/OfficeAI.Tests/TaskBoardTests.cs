using System.Net;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Work;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class TaskBoardTests(ApiFixture fx)
{
    [Fact]
    public async Task Board_lists_open_tasks_across_projects_with_filters()
    {
        var ceo = await fx.CeoAsync();
        var a = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"BA-{Guid.NewGuid():N}"[..14], name = "Board A", status = "Active" });
        var b = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"BB-{Guid.NewGuid():N}"[..14], name = "Board B", status = "Active" });
        var late = await ceo.PostAsync<TaskDto>($"/api/projects/{a.Id}/tasks",
            new { title = "Late", priority = "Critical", dueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3) });
        var other = await ceo.PostAsync<TaskDto>($"/api/projects/{b.Id}/tasks", new { title = "Other" });
        var cancelled = await ceo.PostAsync<TaskDto>($"/api/projects/{b.Id}/tasks", new { title = "Dropped" });
        await ceo.PostAsync<TaskDto>($"/api/tasks/{cancelled.Id}/status", new { status = "Cancelled" });

        var all = await ceo.GetAsync<List<BoardTaskDto>>("/api/tasks/board");
        Assert.Contains(all, x => x.Task.Id == late.Id && x.IsOverdue && x.ProjectCode == a.Code);
        Assert.Contains(all, x => x.Task.Id == other.Id && !x.IsOverdue && x.ProjectName == "Board B");
        Assert.DoesNotContain(all, x => x.Task.Id == cancelled.Id);

        var onlyA = await ceo.GetAsync<List<BoardTaskDto>>($"/api/tasks/board?projectId={a.Id}");
        Assert.Equal([late.Id], onlyA.Select(x => x.Task.Id));
        var overdue = await ceo.GetAsync<List<BoardTaskDto>>("/api/tasks/board?overdue=true");
        Assert.All(overdue, x => Assert.True(x.IsOverdue));
        Assert.Contains(overdue, x => x.Task.Id == late.Id);
        var critical = await ceo.GetAsync<List<BoardTaskDto>>("/api/tasks/board?priority=Critical");
        Assert.All(critical, x => Assert.Equal(TaskPriority.Critical, x.Task.Priority));
    }

    [Fact]
    public async Task Board_needs_task_read()
    {
        var anon = fx.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/tasks/board")).StatusCode);
    }
}
