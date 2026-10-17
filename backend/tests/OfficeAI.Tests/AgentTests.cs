using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Agents;
using OfficeAI.Application.Organization;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Audit;
using OfficeAI.Domain.Work;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class AgentTests(ApiFixture fx)
{
    // ---------- helpers ----------

    private async Task<(HttpClient Ceo, EmployeeDto Agent, TaskDto Task)> AgentWithTaskAsync(string title = "Write API spec")
    {
        var ceo = await fx.CeoAsync();
        var roles = await ceo.GetAsync<List<RoleDefinitionDto>>("/api/role-definitions");
        var role = roles.First(r => r.Code == "BACKEND");
        var agent = await ceo.PostAsync<EmployeeDto>("/api/employees",
            new { displayName = $"Test AI {Guid.NewGuid():N}"[..20], kind = "AI", departmentId = role.DepartmentId, roleDefinitionId = role.Id });

        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"AG-{Guid.NewGuid():N}"[..14], name = "Agent project", status = "Active" });
        var task = await ceo.PostAsync<TaskDto>($"/api/projects/{project.Id}/tasks",
            new { title, description = "Describe the endpoints.", acceptanceCriteria = "Lists every endpoint." });
        task = await ceo.PostAsync<TaskDto>($"/api/tasks/{task.Id}/assign", new { employeeId = agent.Id });
        return (ceo, agent, task);
    }

    private async Task UseModelAsync(HttpClient ceo, Guid employeeId, string provider, string model, Action<Dictionary<string, object>>? tweak = null)
    {
        var current = await ceo.GetAsync<AgentConfigDto>($"/api/agents/{employeeId}/config");
        var body = new Dictionary<string, object>
        {
            ["provider"] = provider, ["modelId"] = model, ["effort"] = "Medium", ["maxTokensPerCall"] = current.MaxTokensPerCall,
            ["maxTurnsPerRun"] = current.MaxTurnsPerRun, ["maxToolCallsPerRun"] = current.MaxToolCallsPerRun,
            ["maxTokensPerRun"] = current.MaxTokensPerRun, ["timeoutSeconds"] = current.TimeoutSeconds,
            ["monthlyBudgetUsd"] = current.MonthlyBudgetUsd, ["isEnabled"] = true,
        };
        tweak?.Invoke(body);
        (await ceo.PutAsJsonAsync($"/api/agents/{employeeId}/config", body, ApiFixture.Json)).EnsureSuccessStatusCode();
    }

    /// <summary>Registers a scripted model with explicit prices and returns its id.</summary>
    private async Task<string> ScriptedModelAsync(Func<AiRequest, CancellationToken, Task<AiResponse>> script, decimal input = 0m, decimal output = 0m)
    {
        var modelId = $"script-{Guid.NewGuid():N}";
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AiModels.Add(new AiModel
        {
            Provider = ScriptedProvider.ProviderName, ModelId = modelId, DisplayName = modelId, Tier = ModelTier.Standard,
            InputUsdPerMTok = input, OutputUsdPerMTok = output,
        });
        await db.SaveChangesAsync();
        fx.Scripted.Register(modelId, script);
        return modelId;
    }

    private static async Task<AgentRunDetailDto> WaitAsync(HttpClient c, Guid runId)
    {
        for (var i = 0; i < 100; i++)
        {
            var detail = await c.GetAsync<AgentRunDetailDto>($"/api/agent-runs/{runId}");
            if (detail.Run.Status is not (AgentRunStatus.Queued or AgentRunStatus.Running)) return detail;
            await Task.Delay(100);
        }
        throw new TimeoutException("Run did not finish.");
    }

    private static async Task<AgentRunDto> StartAsync(HttpClient c, Guid taskId) =>
        await c.PostAsync<AgentRunDto>($"/api/tasks/{taskId}/runs", new { });

    // ---------- defaults ----------

    [Fact]
    public async Task Every_AI_employee_gets_a_default_configuration_and_tools()
    {
        var (ceo, agent, _) = await AgentWithTaskAsync();
        var config = await ceo.GetAsync<AgentConfigDto>($"/api/agents/{agent.Id}/config");
        Assert.Equal("anthropic", config.Provider);
        Assert.Equal("claude-sonnet-5-5", config.ModelId); // Staff level
        Assert.Equal(5m, config.MonthlyBudgetUsd);
        Assert.Equal(ToolRegistry.DefaultGrants.Order(), config.Tools.Order());

        var employees = await ceo.GetAsync<List<EmployeeDto>>("/api/employees");
        var cto = employees.Single(e => e.JobTitle == "CTO");
        Assert.Equal("claude-opus-5-5", (await ceo.GetAsync<AgentConfigDto>($"/api/agents/{cto.Id}/config")).ModelId);
    }

    [Fact]
    public async Task Seeded_prices_are_configured_and_providers_report_availability()
    {
        var ceo = await fx.CeoAsync();
        var models = await ceo.GetAsync<List<AiModelDto>>("/api/ai/models");
        var opus = models.Single(m => m.ModelId == "claude-opus-5-5");
        Assert.Equal(4m, opus.InputUsdPerMTok);
        Assert.Equal(20m, opus.OutputUsdPerMTok);

        var providers = await ceo.GetAsync<List<ProviderDto>>("/api/ai/providers");
        Assert.False(providers.Single(p => p.Name == "anthropic").IsAvailable); // no key in tests
        Assert.True(providers.Single(p => p.Name == "simulated").IsSimulated);
    }

    // ---------- the happy path ----------

    [Fact]
    public async Task Simulated_run_reads_task_submits_deliverable_and_moves_task_to_review()
    {
        var (ceo, agent, task) = await AgentWithTaskAsync();
        await UseModelAsync(ceo, agent.Id, "simulated", "simulated-v1");

        var run = await StartAsync(ceo, task.Id);
        Assert.True(run.IsSimulated);
        var detail = await WaitAsync(ceo, run.Id);

        Assert.Equal(AgentRunStatus.Succeeded, detail.Run.Status);
        Assert.Equal(3, detail.ToolExecutions.Count);
        Assert.All(detail.ToolExecutions, x => Assert.Equal(ToolExecutionStatus.Succeeded, x.Status));
        Assert.Contains(detail.Steps, s => s.Kind == RunStepKind.Instructions);
        Assert.True(detail.Run.InputTokens > 0);

        Assert.Equal(WorkTaskStatus.InReview, (await ceo.GetAsync<TaskDto>($"/api/tasks/{task.Id}")).Status);
        var deliverable = Assert.Single(await ceo.GetAsync<List<DeliverableDto>>($"/api/tasks/{task.Id}/deliverables"));
        Assert.True(deliverable.IsSimulated);
        Assert.Equal(agent.Id, deliverable.EmployeeId);

        // The employee is free again.
        Assert.Equal(Domain.Organization.EmployeeStatus.Available, (await ceo.GetAsync<EmployeeDto>($"/api/employees/{agent.Id}")).Status);
    }

    [Fact]
    public async Task Work_done_by_an_agent_is_attributed_to_it_in_the_audit_log()
    {
        var (ceo, agent, task) = await AgentWithTaskAsync();
        await UseModelAsync(ceo, agent.Id, "simulated", "simulated-v1");
        var run = await StartAsync(ceo, task.Id);
        await WaitAsync(ceo, run.Id);

        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = await db.AuditLogs.SingleAsync(a => a.EntityType == "Deliverable" && a.Action == AuditActions.Created && a.CorrelationId == run.Id.ToString("N"));
        Assert.Equal(agent.DisplayName, log.ActorName);
    }

    // ---------- cost ----------

    [Fact]
    public void Cost_uses_decimal_math_from_per_million_token_prices()
    {
        var opus = new AiModel { Provider = "anthropic", ModelId = "claude-opus-5-5", DisplayName = "x", InputUsdPerMTok = 4m, OutputUsdPerMTok = 20m, CacheReadUsdPerMTok = 0.2m, CacheWriteUsdPerMTok = 5m };
        Assert.Equal(0.026476m, Pricing.CostUsd(opus, 1234, 567, 1000, 2000));
        Assert.Equal(0m, Pricing.CostUsd(opus, 0, 0, 0, 0));
    }

    [Fact]
    public async Task Each_model_call_is_recorded_as_usage_and_actual_cost()
    {
        var (ceo, agent, task) = await AgentWithTaskAsync();
        var model = await ScriptedModelAsync(ScriptedProvider.Sequence(
            ScriptedProvider.Call("task_read_context", new { }, inTok: 1_000_000, outTok: 100_000),
            ScriptedProvider.Text("finished", inTok: 500_000, outTok: 0)), input: 3m, output: 15m);
        await UseModelAsync(ceo, agent.Id, ScriptedProvider.ProviderName, model, b => b["maxTokensPerRun"] = 10_000_000);

        var detail = await WaitAsync(ceo, (await StartAsync(ceo, task.Id)).Id);
        // (1,000,000 × 3 + 100,000 × 15) / 1e6 + 500,000 × 3 / 1e6 = 4.5 + 1.5
        Assert.Equal(6.0m, detail.Run.CostUsd);
        Assert.Equal(1_500_000, detail.Run.InputTokens);

        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var costs = await db.CostRecords.Where(c => c.RunId == detail.Run.Id).ToListAsync();
        Assert.Equal(2, costs.Count);
        Assert.All(costs, c => Assert.Equal(CostKind.Actual, c.Kind));
        Assert.Equal(6.0m, costs.Sum(c => c.AmountUsd));
        Assert.Equal(2, await db.UsageRecords.CountAsync(u => u.RunId == detail.Run.Id));

        var config = await ceo.GetAsync<AgentConfigDto>($"/api/agents/{agent.Id}/config");
        Assert.Equal(6.0m, config.SpentThisMonthUsd);
    }

    // ---------- enforcement ----------

    [Fact]
    public async Task Tool_that_is_not_granted_is_denied_and_nothing_changes()
    {
        var (ceo, agent, task) = await AgentWithTaskAsync();
        (await ceo.PutAsJsonAsync($"/api/agents/{agent.Id}/tools", new { tools = new[] { TaskContextTool.ToolId } })).EnsureSuccessStatusCode();
        var model = await ScriptedModelAsync(ScriptedProvider.Sequence(
            ScriptedProvider.Call("deliverable_submit", new { title = "x", content = "y" }),
            ScriptedProvider.Call("task_request_review", new { })));
        await UseModelAsync(ceo, agent.Id, ScriptedProvider.ProviderName, model);

        var detail = await WaitAsync(ceo, (await StartAsync(ceo, task.Id)).Id);
        Assert.Equal(2, detail.ToolExecutions.Count);
        Assert.All(detail.ToolExecutions, x => Assert.Equal(ToolExecutionStatus.Denied, x.Status));
        Assert.Empty(await ceo.GetAsync<List<DeliverableDto>>($"/api/tasks/{task.Id}/deliverables"));
        Assert.Equal(WorkTaskStatus.InProgress, (await ceo.GetAsync<TaskDto>($"/api/tasks/{task.Id}")).Status);
    }

    [Fact]
    public async Task Tool_that_requires_approval_is_never_executed_directly()
    {
        var (ceo, agent, task) = await AgentWithTaskAsync();
        (await ceo.PutAsJsonAsync($"/api/agents/{agent.Id}/tools", new { tools = new[] { ApprovalRequiredTool.ToolId } })).EnsureSuccessStatusCode();
        var before = ApprovalRequiredTool.Executions;
        var model = await ScriptedModelAsync(ScriptedProvider.Sequence(ScriptedProvider.Call("test_needs_approval", new { })));
        await UseModelAsync(ceo, agent.Id, ScriptedProvider.ProviderName, model);

        var detail = await WaitAsync(ceo, (await StartAsync(ceo, task.Id)).Id);
        Assert.Equal(ToolExecutionStatus.Denied, Assert.Single(detail.ToolExecutions).Status);
        Assert.Equal(before, ApprovalRequiredTool.Executions);
    }

    [Fact]
    public async Task Invalid_tool_input_is_rejected_before_execution()
    {
        var (ceo, agent, task) = await AgentWithTaskAsync();
        var model = await ScriptedModelAsync(ScriptedProvider.Sequence(
            ScriptedProvider.Call("deliverable_submit", new { title = "missing content" }),
            ScriptedProvider.Call("deliverable_submit", new { title = "t", content = "c", extra = true })));
        await UseModelAsync(ceo, agent.Id, ScriptedProvider.ProviderName, model);

        var detail = await WaitAsync(ceo, (await StartAsync(ceo, task.Id)).Id);
        Assert.All(detail.ToolExecutions, x => Assert.Equal(ToolExecutionStatus.InvalidInput, x.Status));
        Assert.Empty(await ceo.GetAsync<List<DeliverableDto>>($"/api/tasks/{task.Id}/deliverables"));
    }

    [Fact]
    public async Task Claiming_completion_without_a_deliverable_does_not_move_the_task()
    {
        var (ceo, agent, task) = await AgentWithTaskAsync();
        var model = await ScriptedModelAsync(ScriptedProvider.Sequence(
            ScriptedProvider.Call("task_request_review", new { note = "All done, trust me" }),
            ScriptedProvider.Text("I have completed the task.")));
        await UseModelAsync(ceo, agent.Id, ScriptedProvider.ProviderName, model);

        var detail = await WaitAsync(ceo, (await StartAsync(ceo, task.Id)).Id);
        Assert.Equal(ToolExecutionStatus.Failed, Assert.Single(detail.ToolExecutions).Status);
        Assert.Equal(WorkTaskStatus.InProgress, (await ceo.GetAsync<TaskDto>($"/api/tasks/{task.Id}")).Status);
    }

    [Fact]
    public async Task Turn_limit_stops_a_run_that_never_finishes()
    {
        var (ceo, agent, task) = await AgentWithTaskAsync();
        var model = await ScriptedModelAsync((_, _) => Task.FromResult(ScriptedProvider.Call("task_read_context", new { })));
        await UseModelAsync(ceo, agent.Id, ScriptedProvider.ProviderName, model, b => b["maxTurnsPerRun"] = 3);

        var detail = await WaitAsync(ceo, (await StartAsync(ceo, task.Id)).Id);
        Assert.Equal(AgentRunStatus.LimitReached, detail.Run.Status);
        Assert.Equal("turns", detail.Run.StopReason);
        Assert.Equal(3, detail.Run.Turns);
    }

    [Fact]
    public async Task Monthly_budget_blocks_new_runs_and_stops_a_running_one()
    {
        var (ceo, agent, task) = await AgentWithTaskAsync();
        var model = await ScriptedModelAsync((_, _) => Task.FromResult(ScriptedProvider.Call("task_read_context", new { }, inTok: 1_000_000, outTok: 0)), input: 1m);
        await UseModelAsync(ceo, agent.Id, ScriptedProvider.ProviderName, model, b => { b["monthlyBudgetUsd"] = 2m; b["maxTokensPerRun"] = 10_000_000; });

        // Each turn costs 1 USD: the run stops once 2 USD are spent.
        var detail = await WaitAsync(ceo, (await StartAsync(ceo, task.Id)).Id);
        Assert.Equal(AgentRunStatus.LimitReached, detail.Run.Status);
        Assert.Equal("budget", detail.Run.StopReason);
        Assert.Equal(2m, detail.Run.CostUsd);

        var res = await ceo.PostAsJsonAsync($"/api/tasks/{task.Id}/runs", new { });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Contains("budget", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Run_can_be_cancelled_while_running()
    {
        var (ceo, agent, task) = await AgentWithTaskAsync();
        var model = await ScriptedModelAsync(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return ScriptedProvider.Text("too late");
        });
        await UseModelAsync(ceo, agent.Id, ScriptedProvider.ProviderName, model);

        var run = await StartAsync(ceo, task.Id);
        for (var i = 0; i < 50 && (await ceo.GetAsync<AgentRunDetailDto>($"/api/agent-runs/{run.Id}")).Run.Status != AgentRunStatus.Running; i++)
            await Task.Delay(100);
        await ceo.PostAsync<AgentRunDto>($"/api/agent-runs/{run.Id}/cancel", new { });

        var detail = await WaitAsync(ceo, run.Id);
        Assert.Equal(AgentRunStatus.Cancelled, detail.Run.Status);
    }

    [Fact]
    public async Task Only_one_active_run_per_task()
    {
        var (ceo, agent, task) = await AgentWithTaskAsync();
        var model = await ScriptedModelAsync(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return ScriptedProvider.Text("x");
        });
        await UseModelAsync(ceo, agent.Id, ScriptedProvider.ProviderName, model);

        var run = await StartAsync(ceo, task.Id);
        var second = await ceo.PostAsJsonAsync($"/api/tasks/{task.Id}/runs", new { });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        await ceo.PostAsync<AgentRunDto>($"/api/agent-runs/{run.Id}/cancel", new { });
        await WaitAsync(ceo, run.Id);
    }

    [Fact]
    public async Task Runs_are_refused_without_a_configured_provider_or_an_AI_assignee()
    {
        var (ceo, agent, task) = await AgentWithTaskAsync();

        // Default provider is Anthropic, which has no key in tests.
        var res = await ceo.PostAsJsonAsync($"/api/tasks/{task.Id}/runs", new { });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Contains("not configured", await res.Content.ReadAsStringAsync());

        // A task assigned to the human CEO cannot be run by AI.
        var me = await ceo.GetAsync<Api.Endpoints.MeDto>("/api/auth/me");
        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"H-{Guid.NewGuid():N}"[..12], name = "Human work", status = "Active" });
        var humanTask = await ceo.PostAsync<TaskDto>($"/api/projects/{project.Id}/tasks", new { title = "Sign contract" });
        await ceo.PostAsync<TaskDto>($"/api/tasks/{humanTask.Id}/assign", new { employeeId = me.EmployeeId });
        res = await ceo.PostAsJsonAsync($"/api/tasks/{humanTask.Id}/runs", new { });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);

        // An unassigned task cannot be run.
        var loose = await ceo.PostAsync<TaskDto>($"/api/projects/{project.Id}/tasks", new { title = "Nobody's task" });
        Assert.Equal(HttpStatusCode.Conflict, (await ceo.PostAsJsonAsync($"/api/tasks/{loose.Id}/runs", new { })).StatusCode);
    }

    [Fact]
    public async Task Config_changes_are_validated()
    {
        var (ceo, agent, _) = await AgentWithTaskAsync();
        var current = await ceo.GetAsync<AgentConfigDto>($"/api/agents/{agent.Id}/config");
        object Body(string provider, string model, decimal budget) => new
        {
            provider, modelId = model, effort = "High", maxTokensPerCall = 8000, maxTurnsPerRun = 12, maxToolCallsPerRun = 20,
            maxTokensPerRun = 200000, timeoutSeconds = 600, monthlyBudgetUsd = budget, isEnabled = true, version = current.Version,
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PutAsJsonAsync($"/api/agents/{agent.Id}/config", Body("nope", "x", 5))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PutAsJsonAsync($"/api/agents/{agent.Id}/config", Body("anthropic", "gpt-x", 5))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PutAsJsonAsync($"/api/agents/{agent.Id}/config", Body("anthropic", "claude-opus-5-5", -1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PutAsJsonAsync($"/api/agents/{agent.Id}/tools", new { tools = new[] { "shell.exec" } })).StatusCode);

        var ok = await ceo.PutAsJsonAsync($"/api/agents/{agent.Id}/config", Body("anthropic", "claude-haiku-5-5", 1));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Usage_summary_separates_simulated_spend()
    {
        var (ceo, agent, task) = await AgentWithTaskAsync();
        await UseModelAsync(ceo, agent.Id, "simulated", "simulated-v1");
        await WaitAsync(ceo, (await StartAsync(ceo, task.Id)).Id);

        var usage = await ceo.GetAsync<UsageSummaryDto>("/api/ai/usage");
        Assert.True(usage.RunsThisMonth >= 1);
        Assert.Contains(usage.ByEmployee, e => e.EmployeeId == agent.Id && e.Runs == 1);
    }
}
