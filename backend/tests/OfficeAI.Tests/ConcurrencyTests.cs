using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Agents;
using OfficeAI.Application.Organization;
using OfficeAI.Application.Work;
using OfficeAI.Application.Workflows;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Workflows;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class ConcurrencyTests(ApiFixture fx)
{
    private static void AssertOnlyOkOrConflict(IEnumerable<HttpResponseMessage> responses, int expectedOk)
    {
        var codes = responses.Select(r => r.StatusCode).ToList();
        Assert.All(codes, c => Assert.True(c is HttpStatusCode.OK or HttpStatusCode.Conflict, $"Unexpected {c}"));
        Assert.Equal(expectedOk, codes.Count(c => c == HttpStatusCode.OK));
    }

    [Fact]
    public async Task Racing_run_starts_produce_exactly_one_run()
    {
        var ceo = await fx.CeoAsync();
        var role = (await ceo.GetAsync<List<RoleDefinitionDto>>("/api/role-definitions")).First(r => r.Code == "BACKEND");
        var agent = await ceo.PostAsync<EmployeeDto>("/api/employees",
            new { displayName = $"Race {Guid.NewGuid():N}"[..20], kind = "AI", departmentId = role.DepartmentId, roleDefinitionId = role.Id });
        var modelId = $"script-{Guid.NewGuid():N}";
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AiModels.Add(new AiModel { Provider = ScriptedProvider.ProviderName, ModelId = modelId, DisplayName = modelId });
            await db.SaveChangesAsync();
        }
        fx.Scripted.Register(modelId, async (_, ct) => { await Task.Delay(TimeSpan.FromSeconds(30), ct); return ScriptedProvider.Text("x"); });
        var c = await ceo.GetAsync<AgentConfigDto>($"/api/agents/{agent.Id}/config");
        (await ceo.PutAsJsonAsync($"/api/agents/{agent.Id}/config", new
        {
            provider = ScriptedProvider.ProviderName, modelId, effort = "Medium", c.MaxTokensPerCall, c.MaxTurnsPerRun, c.MaxToolCallsPerRun,
            c.MaxTokensPerRun, c.TimeoutSeconds, c.MonthlyBudgetUsd, isEnabled = true,
        }, ApiFixture.Json)).EnsureSuccessStatusCode();
        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"RC-{Guid.NewGuid():N}"[..14], name = "Race", status = "Active" });
        var task = await ceo.PostAsync<TaskDto>($"/api/projects/{project.Id}/tasks", new { title = "Race me" });
        await ceo.PostAsync<TaskDto>($"/api/tasks/{task.Id}/assign", new { employeeId = agent.Id });

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => ceo.PostAsJsonAsync($"/api/tasks/{task.Id}/runs", new { })));
        AssertOnlyOkOrConflict(responses, 1);
        var runs = await ceo.GetAsync<List<AgentRunDto>>($"/api/agent-runs?taskId={task.Id}");
        var run = Assert.Single(runs);
        await ceo.PostAsync<AgentRunDto>($"/api/agent-runs/{run.Id}/cancel", new { });
    }

    [Fact]
    public async Task Racing_edits_with_the_same_version_have_one_winner()
    {
        var ceo = await fx.CeoAsync();
        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"ED-{Guid.NewGuid():N}"[..14], name = "Edits", status = "Active" });
        var task = await ceo.PostAsync<TaskDto>($"/api/projects/{project.Id}/tasks", new { title = "v0" });
        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            ceo.PutAsJsonAsync($"/api/tasks/{task.Id}", new { title = $"v{i + 1}", version = task.Version })));
        AssertOnlyOkOrConflict(responses, 1);
    }

    [Fact]
    public async Task Racing_assignments_leave_one_active_assignment()
    {
        var ceo = await fx.CeoAsync();
        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"AS-{Guid.NewGuid():N}"[..14], name = "Assign", status = "Active" });
        var task = await ceo.PostAsync<TaskDto>($"/api/projects/{project.Id}/tasks", new { title = "Assign me" });
        var dev = (await ceo.GetAsync<List<EmployeeDto>>("/api/employees")).First(e => e.DisplayName == "Frontend Developer AI");
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => ceo.PostAsJsonAsync($"/api/tasks/{task.Id}/assign", new { employeeId = dev.Id })));
        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, $"Unexpected {r.StatusCode}"));
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.TaskAssignments.CountAsync(a => a.TaskId == task.Id && a.UnassignedAt == null));
    }

    [Fact]
    public async Task Racing_approval_decisions_apply_once()
    {
        var ceo = await fx.CeoAsync();
        Guid id;
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var a = Approvals.Create(ApprovalKind.Escalation, "Race decision", null, new { r = 1 }, DateTimeOffset.UtcNow);
            db.ApprovalRequests.Add(a);
            await db.SaveChangesAsync();
            id = a.Id;
        }
        var dto = await ceo.GetAsync<ApprovalDto>($"/api/approvals/{id}");
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            ceo.PostAsJsonAsync($"/api/approvals/{id}/decision", new { approve = i % 2 == 0, payloadHash = dto.PayloadHash })));
        AssertOnlyOkOrConflict(responses, 1);
    }

    [Fact]
    public async Task Only_one_engine_tick_runs_at_a_time()
    {
        await using var holder = fx.Factory.Services.CreateAsyncScope();
        var lockDb = holder.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await lockDb.TryAcquireEngineLockAsync(default));
        try
        {
            await using var scope = fx.Factory.Services.CreateAsyncScope();
            Assert.False(await scope.ServiceProvider.GetRequiredService<WorkflowEngine>().TickAsync(default));
        }
        finally
        {
            await lockDb.ReleaseEngineLockAsync();
        }
        await using var after = fx.Factory.Services.CreateAsyncScope();
        Assert.True(await after.ServiceProvider.GetRequiredService<WorkflowEngine>().TickAsync(default));
    }
}
