using System.Net;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Knowledge;
using OfficeAI.Application.Organization;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Knowledge;
using OfficeAI.Domain.Security;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class ProjectScopeTests(ApiFixture fx)
{
    private static async Task<ProjectDto> NewProjectAsync(HttpClient ceo, string name) =>
        await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"PS-{Guid.NewGuid():N}"[..14], name, status = "Active" });

    [Fact]
    public async Task People_without_project_all_see_only_their_projects()
    {
        var ceo = await fx.CeoAsync();
        var mine = await NewProjectAsync(ceo, "Mine");
        var other = await NewProjectAsync(ceo, "Other");
        var myTask = await ceo.PostAsync<TaskDto>($"/api/projects/{mine.Id}/tasks", new { title = "Visible" });
        var hiddenTask = await ceo.PostAsync<TaskDto>($"/api/projects/{other.Id}/tasks", new { title = "Hidden" });
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.KnowledgeEntries.Add(new KnowledgeEntry { Kind = KnowledgeKind.Lesson, Title = $"Secret {other.Code}", Body = "other project", ProjectId = other.Id, Status = KnowledgeStatus.Validated });
            await db.SaveChangesAsync();
        }

        var (member, employeeId) = await fx.PersonAsync([SystemRoles.Member], mine.Id);

        var projects = await member.GetAsync<List<ProjectDto>>("/api/projects");
        Assert.Contains(projects, p => p.Id == mine.Id);
        Assert.DoesNotContain(projects, p => p.Id == other.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/projects/{other.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/tasks/{hiddenTask.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/api/tasks/{myTask.Id}")).StatusCode);

        var board = await member.GetAsync<List<BoardTaskDto>>("/api/tasks/board");
        Assert.Contains(board, x => x.Task.Id == myTask.Id);
        Assert.DoesNotContain(board, x => x.Task.ProjectId == other.Id);
        Assert.Empty(await member.GetAsync<List<DocumentDto>>($"/api/projects/{other.Id}/documents"));
        Assert.DoesNotContain(await member.GetAsync<List<KnowledgeDto>>($"/api/knowledge?q={other.Code}"), k => k.ProjectId == other.Id);

        // Being assigned a task makes the project visible too.
        await ceo.PostAsync<TaskDto>($"/api/tasks/{hiddenTask.Id}/assign", new { employeeId });
        Assert.Contains(await member.GetAsync<List<ProjectDto>>("/api/projects"), p => p.Id == other.Id);

        // The CEO and managers (project.all) see everything.
        Assert.Contains(await ceo.GetAsync<List<ProjectDto>>("/api/projects"), p => p.Id == other.Id);
        var manager = await fx.UserWithRolesAsync(SystemRoles.Manager);
        Assert.Contains(await manager.GetAsync<List<ProjectDto>>("/api/projects"), p => p.Id == other.Id);
    }

    [Fact]
    public async Task A_login_without_an_employee_profile_sees_no_projects()
    {
        var ceo = await fx.CeoAsync();
        var project = await NewProjectAsync(ceo, "Somebody's");
        var member = await fx.UserWithRolesAsync(SystemRoles.Member);
        Assert.DoesNotContain(await member.GetAsync<List<ProjectDto>>("/api/projects"), p => p.Id == project.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/projects/{project.Id}")).StatusCode);
    }
}
