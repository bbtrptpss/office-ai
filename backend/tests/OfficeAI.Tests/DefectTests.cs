using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Agents;
using OfficeAI.Application.Delivery;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Delivery;
using OfficeAI.Domain.Organization;
using OfficeAI.Domain.Security;
using OfficeAI.Domain.Work;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class DefectTests(ApiFixture fx)
{
    /// <summary>A project with one task in review, last worked on by an AI developer.</summary>
    private async Task<(ProjectDto Project, Guid TaskId, Guid DeveloperId)> SetupAsync(HttpClient ceo)
    {
        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"DF-{Guid.NewGuid():N}"[..14], name = "Defects", status = "Active" });
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dev = await db.Employees.Where(e => e.Kind == EmployeeKind.AI && e.Status != EmployeeStatus.Inactive).OrderBy(e => e.DisplayName).FirstAsync();
        var task = new WorkTask { ProjectId = project.Id, Title = "Booking form", Status = WorkTaskStatus.InReview, Priority = TaskPriority.Medium };
        db.Tasks.Add(task);
        db.TaskAssignments.Add(new TaskAssignment { TaskId = task.Id, EmployeeId = dev.Id, AssignedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        return (project, task.Id, dev.Id);
    }

    private async Task SetTaskStatusAsync(Guid taskId, WorkTaskStatus status)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Tasks.FirstAsync(t => t.Id == taskId)).Status = status;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Defect_sends_a_fix_task_back_to_the_developer_and_blocks_Done_until_verified()
    {
        var ceo = await fx.CeoAsync();
        var (project, taskId, devId) = await SetupAsync(ceo);

        var bad = await ceo.PostAsJsonAsync($"/api/projects/{project.Id}/defects", new { title = "No steps", stepsToReproduce = " " });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var d = await ceo.PostAsync<DefectDto>($"/api/projects/{project.Id}/defects", new
        {
            title = "Date picker accepts past dates", stepsToReproduce = "1. Open the form\n2. Pick yesterday", expectedResult = "Rejected", actualResult = "Saved",
            severity = "High", taskId,
        });
        Assert.Equal("DEF-001", d.Code);
        Assert.Equal("open", d.State);
        Assert.Equal(taskId, d.TaskId);
        Assert.NotNull(d.FixTaskId);
        Assert.Equal(WorkTaskStatus.Assigned, d.FixTaskStatus);
        Assert.NotNull(d.AssigneeName);

        var fix = await ceo.GetAsync<TaskDto>($"/api/tasks/{d.FixTaskId}");
        Assert.Equal(TaskPriority.High, fix.Priority);
        Assert.Contains("Pick yesterday", fix.Description);
        Assert.Equal(devId, Assert.Single(fix.Assignees).EmployeeId);

        // The affected task cannot pass while the defect is open.
        var done = await ceo.PostAsJsonAsync($"/api/tasks/{taskId}/status", new { status = "Done" });
        Assert.Equal(HttpStatusCode.Conflict, done.StatusCode);
        Assert.Contains("DEF-001", await done.Content.ReadAsStringAsync());

        // Verifying needs the fix to be done first.
        var early = await ceo.PostAsJsonAsync($"/api/defects/{d.Id}/verify", new { });
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);

        await SetTaskStatusAsync(d.FixTaskId!.Value, WorkTaskStatus.Done);
        var list = await ceo.GetAsync<List<DefectDto>>($"/api/projects/{project.Id}/defects");
        Assert.Equal("fixReady", Assert.Single(list).State);

        var stages = await ceo.GetAsync<DeliveryStagesDto>($"/api/projects/{project.Id}/stages");
        Assert.Contains(stages.Stages[5].Exit, c => c.Code == "noOpenDefects" && !c.Met);

        var verified = await ceo.PostAsync<DefectDto>($"/api/defects/{d.Id}/verify", new { note = "Checked on staging" });
        Assert.Equal("verified", verified.State);
        Assert.NotNull(verified.ResolvedAt);

        var ok = await ceo.PostAsync<TaskDto>($"/api/tasks/{taskId}/status", new { status = "Done" });
        Assert.Equal(WorkTaskStatus.Done, ok.Status);
        stages = await ceo.GetAsync<DeliveryStagesDto>($"/api/projects/{project.Id}/stages");
        Assert.Contains(stages.Stages[5].Exit, c => c.Code == "noOpenDefects" && c.Met);
    }

    [Fact]
    public async Task Reopen_creates_a_new_fix_task_and_reject_cancels_an_unstarted_one()
    {
        var ceo = await fx.CeoAsync();
        var (project, taskId, devId) = await SetupAsync(ceo);
        var d = await ceo.PostAsync<DefectDto>($"/api/projects/{project.Id}/defects", new { title = "Crash on save", stepsToReproduce = "Save twice", taskId });

        var noNote = await ceo.PostAsJsonAsync($"/api/defects/{d.Id}/reopen", new { note = "" });
        Assert.Equal(HttpStatusCode.BadRequest, noNote.StatusCode);
        var notYet = await ceo.PostAsJsonAsync($"/api/defects/{d.Id}/reopen", new { note = "still broken" });
        Assert.Equal(HttpStatusCode.Conflict, notYet.StatusCode);

        await SetTaskStatusAsync(d.FixTaskId!.Value, WorkTaskStatus.Done);
        var reopened = await ceo.PostAsync<DefectDto>($"/api/defects/{d.Id}/reopen", new { note = "Still crashes on the third save" });
        Assert.Equal("open", reopened.State);
        Assert.Equal(1, reopened.ReopenCount);
        Assert.NotEqual(d.FixTaskId, reopened.FixTaskId);
        var fix2 = await ceo.GetAsync<TaskDto>($"/api/tasks/{reopened.FixTaskId}");
        Assert.Equal(devId, Assert.Single(fix2.Assignees).EmployeeId);

        var rejected = await ceo.PostAsync<DefectDto>($"/api/defects/{d.Id}/reject", new { note = "Duplicate of DEF-000" });
        Assert.Equal("rejected", rejected.State);
        Assert.Equal(WorkTaskStatus.Cancelled, rejected.FixTaskStatus);
        var again = await ceo.PostAsJsonAsync($"/api/defects/{d.Id}/verify", new { });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Members_cannot_report_or_verify_and_other_projects_are_hidden()
    {
        var ceo = await fx.CeoAsync();
        var (project, taskId, _) = await SetupAsync(ceo);
        var d = await ceo.PostAsync<DefectDto>($"/api/projects/{project.Id}/defects", new { title = "Typo", stepsToReproduce = "Open the page", severity = "Low", taskId });

        var (member, _) = await fx.PersonAsync([SystemRoles.Member], project.Id);
        Assert.Single(await member.GetAsync<List<DefectDto>>($"/api/projects/{project.Id}/defects"));
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync($"/api/projects/{project.Id}/defects", new { title = "x", stepsToReproduce = "y" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync($"/api/defects/{d.Id}/reject", new { note = "no" })).StatusCode);

        var (outsider, _) = await fx.PersonAsync([SystemRoles.Member]);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/projects/{project.Id}/defects")).StatusCode);
    }

    [Fact]
    public async Task Agent_tool_records_a_defect_against_its_current_task()
    {
        var ceo = await fx.CeoAsync();
        var (project, taskId, _) = await SetupAsync(ceo);
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var qa = await db.Employees.Include(e => e.RoleDefinition).FirstAsync(e => e.RoleDefinition!.Code == "QAENG");
        var tool = new ReportDefectTool();
        var ctx = new ToolContext(Guid.NewGuid(), taskId, project.Id, qa.Id, false, db, TimeProvider.System);

        var fail = await tool.ExecuteAsync(ctx, JsonSerializer.SerializeToElement(new { title = "x", stepsToReproduce = "y", actualResult = "z", taskId = Guid.NewGuid().ToString() }), default);
        Assert.False(fail.Success);

        var res = await tool.ExecuteAsync(ctx, JsonSerializer.SerializeToElement(new
        {
            title = "Total ignores discount", stepsToReproduce = "Add a 10% code", actualResult = "Full price", severity = "Critical",
        }), default);
        Assert.True(res.Success, res.Output);

        var list = await ceo.GetAsync<List<DefectDto>>($"/api/projects/{project.Id}/defects");
        var d = Assert.Single(list);
        Assert.True(d.ReportedByAi);
        Assert.Equal(qa.DisplayName, d.ReporterName);
        Assert.Equal(DefectSeverity.Critical, d.Severity);
        Assert.Contains(ReportDefectTool.ToolId, AgentDefaults.ToolsFor(OrgLevel.Head, "QAENG"));
        Assert.DoesNotContain(ReportDefectTool.ToolId, AgentDefaults.ToolsFor(OrgLevel.Staff, "BACKEND"));
    }
}
