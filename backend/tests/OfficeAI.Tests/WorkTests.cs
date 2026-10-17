using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OfficeAI.Application.Organization;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Audit;
using OfficeAI.Domain.Security;
using OfficeAI.Domain.Work;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class WorkTests(ApiFixture fx)
{
    private static async Task<ProjectDto> NewProjectAsync(HttpClient c) =>
        await c.PostAsync<ProjectDto>("/api/projects",
            new { code = $"P-{Guid.NewGuid():N}"[..14], name = "ระบบจองห้องประชุม", status = "Active", estimatedBudget = 150000m });

    private static Task<TaskDto> NewTaskAsync(HttpClient c, Guid projectId, string title) =>
        c.PostAsync<TaskDto>($"/api/projects/{projectId}/tasks", new { title, priority = "High" });

    private static async Task<EmployeeDto> AiEmployeeAsync(HttpClient c, string jobTitle) =>
        (await c.GetAsync<List<EmployeeDto>>("/api/employees")).First(e => e.JobTitle == jobTitle && e.DisplayName == jobTitle.Replace(" Agent", "") + " AI");

    [Fact]
    public async Task Create_project_milestone_task_and_assign_moves_task_to_Assigned()
    {
        var ceo = await fx.CeoAsync();
        var project = await NewProjectAsync(ceo);
        var milestone = await ceo.PostAsync<MilestoneDto>($"/api/projects/{project.Id}/milestones", new { name = "MVP", dueDate = "2026-12-31" });
        var task = await ceo.PostAsync<TaskDto>($"/api/projects/{project.Id}/tasks", new { title = "Design API", milestoneId = milestone.Id });
        Assert.Equal(WorkTaskStatus.Todo, task.Status);

        var dev = await AiEmployeeAsync(ceo, "Backend Developer Agent");
        task = await ceo.PostAsync<TaskDto>($"/api/tasks/{task.Id}/assign", new { employeeId = dev.Id });
        Assert.Equal(WorkTaskStatus.Assigned, task.Status);
        Assert.Equal(dev.Id, Assert.Single(task.Assignees).EmployeeId);

        // Reassigning closes the previous assignment.
        var fe = await AiEmployeeAsync(ceo, "Frontend Developer Agent");
        task = await ceo.PostAsync<TaskDto>($"/api/tasks/{task.Id}/assign", new { employeeId = fe.Id });
        Assert.Equal(fe.Id, Assert.Single(task.Assignees).EmployeeId);

        var reloaded = await ceo.GetAsync<ProjectDto>($"/api/projects/{project.Id}");
        Assert.Equal(1, reloaded.TaskCount);
    }

    [Fact]
    public async Task Every_read_endpoint_returns_200_for_the_CEO()
    {
        var ceo = await fx.CeoAsync();
        var project = await NewProjectAsync(ceo);
        var task = await NewTaskAsync(ceo, project.Id, "Smoke");
        var employee = (await ceo.GetAsync<List<EmployeeDto>>("/api/employees"))[0];

        string[] urls =
        [
            "/api/auth/me", "/api/departments", "/api/role-definitions", "/api/employees", $"/api/employees/{employee.Id}",
            "/api/org-chart", "/api/dashboard", "/api/projects", $"/api/projects/{project.Id}", $"/api/projects/{project.Id}/milestones",
            $"/api/projects/{project.Id}/tasks", $"/api/tasks/{task.Id}", "/api/security/permissions", "/api/security/roles",
            "/api/security/users", "/api/audit", "/api/audit?entityType=WorkTask&take=5", "/health",
        ];
        foreach (var url in urls)
        {
            var res = await ceo.GetAsync(url);
            Assert.True(res.StatusCode == HttpStatusCode.OK, $"{url} returned {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        }
    }

    [Fact]
    public async Task Dashboard_reflects_projects_tasks_and_workload()
    {
        var ceo = await fx.CeoAsync();
        var project = await NewProjectAsync(ceo);
        var task = await NewTaskAsync(ceo, project.Id, "Counted");
        var dev = await AiEmployeeAsync(ceo, "Mobile Developer Agent");
        await ceo.PostAsync<TaskDto>($"/api/tasks/{task.Id}/assign", new { employeeId = dev.Id });

        var dash = await ceo.GetAsync<DashboardDto>("/api/dashboard");
        Assert.True(dash.ActiveProjects >= 1);
        Assert.True(dash.TotalTasks >= 1);
        Assert.True(dash.AiEmployees >= 30);
        Assert.Equal(1, dash.HumanEmployees);
        Assert.Contains(dash.Workload, w => w.EmployeeId == dev.Id && w.OpenTasks >= 1);
    }

    [Fact]
    public async Task Illegal_status_jump_is_rejected()
    {
        var ceo = await fx.CeoAsync();
        var project = await NewProjectAsync(ceo);
        var task = await NewTaskAsync(ceo, project.Id, "Skip ahead");

        var res = await ceo.PostAsJsonAsync($"/api/tasks/{task.Id}/status", new { status = "Done" });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        res = await ceo.PostAsJsonAsync($"/api/tasks/{task.Id}/status", new { status = "InProgress" });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Full_workflow_to_Done_and_only_an_approver_can_close()
    {
        var ceo = await fx.CeoAsync();
        var member = await fx.UserWithRolesAsync(SystemRoles.Member);
        var project = await NewProjectAsync(ceo);
        var task = await NewTaskAsync(ceo, project.Id, "Build login page");
        var dev = await AiEmployeeAsync(ceo, "Frontend Developer Agent");
        await ceo.PostAsync<TaskDto>($"/api/tasks/{task.Id}/assign", new { employeeId = dev.Id });

        // A Member may move work along...
        task = await member.PostAsync<TaskDto>($"/api/tasks/{task.Id}/status", new { status = "InProgress" });
        task = await member.PostAsync<TaskDto>($"/api/tasks/{task.Id}/status", new { status = "InReview" });
        Assert.Contains(WorkTaskStatus.Done, task.NextStates);

        // ...but cannot approve it.
        var res = await member.PostAsJsonAsync($"/api/tasks/{task.Id}/status", new { status = "Done" });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);

        task = await ceo.PostAsync<TaskDto>($"/api/tasks/{task.Id}/status", new { status = "WaitingApproval" });
        task = await ceo.PostAsync<TaskDto>($"/api/tasks/{task.Id}/status", new { status = "Done" });
        Assert.Equal(WorkTaskStatus.Done, task.Status);
        Assert.Empty(task.NextStates);
    }

    [Fact]
    public async Task Dependency_cycle_is_rejected_and_open_dependency_blocks_start()
    {
        var ceo = await fx.CeoAsync();
        var project = await NewProjectAsync(ceo);
        var a = await NewTaskAsync(ceo, project.Id, "A: schema");
        var b = await NewTaskAsync(ceo, project.Id, "B: API");
        var c = await NewTaskAsync(ceo, project.Id, "C: UI");

        await ceo.PostAsync<TaskDto>($"/api/tasks/{b.Id}/dependencies", new { dependsOnTaskId = a.Id });
        await ceo.PostAsync<TaskDto>($"/api/tasks/{c.Id}/dependencies", new { dependsOnTaskId = b.Id });

        // A -> C would close the loop A -> C -> B -> A.
        var res = await ceo.PostAsJsonAsync($"/api/tasks/{a.Id}/dependencies", new { dependsOnTaskId = c.Id });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        res = await ceo.PostAsJsonAsync($"/api/tasks/{a.Id}/dependencies", new { dependsOnTaskId = a.Id });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);

        // B cannot start while A is not Done.
        var dev = await AiEmployeeAsync(ceo, "Backend Developer Agent");
        await ceo.PostAsync<TaskDto>($"/api/tasks/{b.Id}/assign", new { employeeId = dev.Id });
        res = await ceo.PostAsJsonAsync($"/api/tasks/{b.Id}/status", new { status = "InProgress" });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Contains("A: schema", await res.Content.ReadAsStringAsync());

        // Finish A, then B can start.
        await ceo.PostAsync<TaskDto>($"/api/tasks/{a.Id}/assign", new { employeeId = dev.Id });
        foreach (var s in new[] { "InProgress", "InReview", "Done" })
            await ceo.PostAsync<TaskDto>($"/api/tasks/{a.Id}/status", new { status = s });
        var started = await ceo.PostAsync<TaskDto>($"/api/tasks/{b.Id}/status", new { status = "InProgress" });
        Assert.Equal(WorkTaskStatus.InProgress, started.Status);
    }

    [Fact]
    public async Task Concurrent_edit_with_stale_version_returns_409()
    {
        var ceo = await fx.CeoAsync();
        var other = await fx.UserWithRolesAsync(SystemRoles.Manager);
        var project = await NewProjectAsync(ceo);
        var task = await NewTaskAsync(ceo, project.Id, "Original");

        var first = await ceo.PutAsync<TaskDto>($"/api/tasks/{task.Id}", new { title = "Edited by CEO", version = task.Version });
        Assert.NotEqual(task.Version, first.Version);

        var res = await other.PutAsJsonAsync($"/api/tasks/{task.Id}", new { title = "Edited by manager", version = task.Version });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("Edited by CEO", (await ceo.GetAsync<TaskDto>($"/api/tasks/{task.Id}")).Title);
    }

    [Fact]
    public async Task Task_change_is_pushed_over_SignalR()
    {
        var ceo = await fx.CeoAsync();
        var project = await NewProjectAsync(ceo);

        // Reuse the logged-in cookie for the hub connection.
        var cookieClient = fx.Anonymous();
        var login = await cookieClient.PostAsJsonAsync("/api/auth/login", new { email = ApiFixture.CeoEmail, password = ApiFixture.CeoPassword });
        var cookie = login.Headers.GetValues("Set-Cookie").First().Split(';')[0];

        var server = fx.Factory.Server;
        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "/hubs/events"), o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => server.CreateHandler();
                o.Headers["Cookie"] = cookie;
            })
            .Build();

        var received = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<TaskChanged>("taskChanged", e => { if (e.ProjectId == project.Id) received.TrySetResult(e.TaskId); });
        await hub.StartAsync();
        try
        {
            var task = await NewTaskAsync(ceo, project.Id, "Realtime");
            var taskId = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(task.Id, taskId);
        }
        finally
        {
            await hub.DisposeAsync();
        }
    }

    [Fact]
    public async Task Hub_rejects_anonymous_connections()
    {
        var server = fx.Factory.Server;
        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "/hubs/events"), o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => server.CreateHandler();
            })
            .Build();
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => hub.StartAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    private record TaskChanged(Guid ProjectId, Guid TaskId);
}

[Collection(ApiCollection.Name)]
public class AuditTests(ApiFixture fx)
{
    [Fact]
    public async Task Changes_are_audited_with_actor_values_and_correlation_id()
    {
        var ceo = await fx.CeoAsync();
        var correlationId = Guid.NewGuid().ToString("N");
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/departments")
        {
            Content = JsonContent.Create(new { code = $"AUD{correlationId[..6]}", name = "Audit me" }),
        };
        req.Headers.Add("X-Correlation-ID", correlationId);
        var dept = await (await ceo.SendAsync(req)).ReadAsync<DepartmentDto>();
        await ceo.PutAsync<DepartmentDto>($"/api/departments/{dept.Id}", new { code = dept.Code, name = "Audited", isActive = true, version = dept.Version });

        var logs = await ceo.GetAsync<List<Infrastructure.Security.AuditLogDto>>($"/api/audit?entityType=Department&entityId={dept.Id}");
        var created = Assert.Single(logs, l => l.Action == AuditActions.Created);
        var updated = Assert.Single(logs, l => l.Action == AuditActions.Updated);

        Assert.Equal(correlationId, created.CorrelationId);
        Assert.Equal("Test CEO", created.ActorName);
        Assert.NotNull(created.ActorUserId);
        Assert.Contains("Audit me", created.NewValues);
        Assert.Contains("Audit me", updated.OldValues);
        Assert.Contains("Audited", updated.NewValues);
    }

    [Fact]
    public async Task Audit_log_rows_cannot_be_updated_or_deleted()
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.AuditLogs.AnyAsync());

        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE \"AuditLogs\" SET \"Action\" = 'tampered'"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM \"AuditLogs\""));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("TRUNCATE \"AuditLogs\""));
    }
}
