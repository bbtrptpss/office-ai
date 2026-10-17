using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using OfficeAI.Application;
using OfficeAI.Application.Agents;
using OfficeAI.Application.Organization;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Agents;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class ObservabilityTests(ApiFixture fx)
{
    [Fact]
    public async Task Readiness_checks_the_database()
    {
        var res = await fx.Anonymous().GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Healthy", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Runs_and_tool_calls_are_counted()
    {
        var seen = new ConcurrentBag<(string Name, long Value, string Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (i, l) => { if (i.Meter.Name == Telemetry.Name) l.EnableMeasurementEvents(i); };
        listener.SetMeasurementEventCallback<long>((i, v, tags, _) =>
            seen.Add((i.Name, v, string.Join(",", tags.ToArray().Select(t => $"{t.Key}={t.Value}")))));
        listener.Start();

        var ceo = await fx.CeoAsync();
        var role = (await ceo.GetAsync<List<RoleDefinitionDto>>("/api/role-definitions")).First(r => r.Code == "BACKEND");
        var agent = await ceo.PostAsync<EmployeeDto>("/api/employees",
            new { displayName = $"Obs {Guid.NewGuid():N}"[..20], kind = "AI", departmentId = role.DepartmentId, roleDefinitionId = role.Id });
        var c = await ceo.GetAsync<AgentConfigDto>($"/api/agents/{agent.Id}/config");
        (await ceo.PutAsJsonAsync($"/api/agents/{agent.Id}/config", new
        {
            provider = "simulated", modelId = "simulated-v1", effort = "Medium", c.MaxTokensPerCall, c.MaxTurnsPerRun, c.MaxToolCallsPerRun,
            c.MaxTokensPerRun, c.TimeoutSeconds, c.MonthlyBudgetUsd, isEnabled = true,
        }, ApiFixture.Json)).EnsureSuccessStatusCode();
        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"OB-{Guid.NewGuid():N}"[..14], name = "Observed", status = "Active" });
        var task = await ceo.PostAsync<TaskDto>($"/api/projects/{project.Id}/tasks", new { title = "Observe me" });
        await ceo.PostAsync<TaskDto>($"/api/tasks/{task.Id}/assign", new { employeeId = agent.Id });
        var run = await ceo.PostAsync<AgentRunDto>($"/api/tasks/{task.Id}/runs", new { });
        for (var i = 0; i < 100 && (await ceo.GetAsync<AgentRunDetailDto>($"/api/agent-runs/{run.Id}")).Run.Status is AgentRunStatus.Queued or AgentRunStatus.Running; i++)
            await Task.Delay(100);

        Assert.Contains(seen, m => m.Name == "officeai.agent_runs.finished" && m.Tags.Contains("status=Succeeded") && m.Tags.Contains("simulated=True"));
        Assert.Contains(seen, m => m.Name == "officeai.tool_executions" && m.Tags.Contains("tool=deliverable.submit"));
        Assert.Contains(seen, m => m.Name == "officeai.ai_tokens" && m.Tags.Contains("direction=output"));
    }
}
