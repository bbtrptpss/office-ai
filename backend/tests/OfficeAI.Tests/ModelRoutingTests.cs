using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Agents;
using OfficeAI.Application.Organization;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Work;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class ModelRoutingTests(ApiFixture fx)
{
    private static AiModel M(string id, ModelTier tier, decimal price, string provider = "p") =>
        new() { Provider = provider, ModelId = id, DisplayName = id, Tier = tier, InputUsdPerMTok = price, OutputUsdPerMTok = price };

    [Fact]
    public void Routine_work_goes_to_the_cheapest_routine_model_and_complex_work_to_an_advanced_one()
    {
        var standard = M("std", ModelTier.Standard, 2m);
        var models = new[] { standard, M("cheap", ModelTier.Routine, 0.1m), M("cheaper", ModelTier.Routine, 0.05m), M("big", ModelTier.Advanced, 5m),
            M("other-provider", ModelTier.Routine, 0.01m, "q") };
        Assert.Equal("std", ModelRouter.Pick(standard, TaskComplexity.Standard, models).ModelId);
        Assert.Equal("cheaper", ModelRouter.Pick(standard, TaskComplexity.Routine, models).ModelId);
        Assert.Equal("big", ModelRouter.Pick(standard, TaskComplexity.Complex, models).ModelId);

        // Nothing suitable for the provider: keep the configured model.
        Assert.Equal("std", ModelRouter.Pick(standard, TaskComplexity.Routine, [standard]).ModelId);
        // Already at or past the wanted tier: keep it (a routine model is never upgraded for routine work, and vice versa).
        var advanced = models[3];
        Assert.Equal("big", ModelRouter.Pick(advanced, TaskComplexity.Complex, models).ModelId);
        var routine = models[1];
        Assert.Equal("cheap", ModelRouter.Pick(routine, TaskComplexity.Routine, models).ModelId);
    }

    [Fact]
    public async Task A_routine_task_runs_on_the_routine_model()
    {
        var ceo = await fx.CeoAsync();
        var role = (await ceo.GetAsync<List<RoleDefinitionDto>>("/api/role-definitions")).First(r => r.Code == "BACKEND");
        var agent = await ceo.PostAsync<EmployeeDto>("/api/employees",
            new { displayName = $"Route {Guid.NewGuid():N}"[..20], kind = "AI", departmentId = role.DepartmentId, roleDefinitionId = role.Id });
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (std, cheap) = ($"script-std-{suffix}", $"script-cheap-{suffix}");
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Routine models of other tests may exist for this provider; this one is priced below any of them.
            db.AiModels.Add(new AiModel { Provider = ScriptedProvider.ProviderName, ModelId = std, DisplayName = std, Tier = ModelTier.Standard, InputUsdPerMTok = 1m });
            db.AiModels.Add(new AiModel { Provider = ScriptedProvider.ProviderName, ModelId = cheap, DisplayName = cheap, Tier = ModelTier.Routine, InputUsdPerMTok = 0m });
            await db.SaveChangesAsync();
        }
        fx.Scripted.Register(std, (_, _) => Task.FromResult(ScriptedProvider.Text("done")));
        fx.Scripted.Register(cheap, (_, _) => Task.FromResult(ScriptedProvider.Text("done")));
        var c = await ceo.GetAsync<AgentConfigDto>($"/api/agents/{agent.Id}/config");
        (await ceo.PutAsJsonAsync($"/api/agents/{agent.Id}/config", new
        {
            provider = ScriptedProvider.ProviderName, modelId = std, effort = "Medium", c.MaxTokensPerCall, c.MaxTurnsPerRun, c.MaxToolCallsPerRun,
            c.MaxTokensPerRun, c.TimeoutSeconds, monthlyBudgetUsd = 100m, isEnabled = true,
        }, ApiFixture.Json)).EnsureSuccessStatusCode();
        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"MR-{Guid.NewGuid():N}"[..14], name = "Routing", status = "Active" });

        var routineTask = await ceo.PostAsync<TaskDto>($"/api/projects/{project.Id}/tasks", new { title = "Format the changelog", complexity = "Routine" });
        Assert.Equal(TaskComplexity.Routine, routineTask.Complexity);
        await ceo.PostAsync<TaskDto>($"/api/tasks/{routineTask.Id}/assign", new { employeeId = agent.Id });
        var run = await ceo.PostAsync<AgentRunDto>($"/api/tasks/{routineTask.Id}/runs", new { });
        Assert.StartsWith("script-cheap-", run.ModelId); // the cheapest routine model (an earlier test run may have left an equal one)

        var normal = await ceo.PostAsync<TaskDto>($"/api/projects/{project.Id}/tasks", new { title = "Build the API" });
        Assert.Equal(TaskComplexity.Standard, normal.Complexity);
        await ceo.PostAsync<TaskDto>($"/api/tasks/{normal.Id}/assign", new { employeeId = agent.Id });
        Assert.Equal(std, (await ceo.PostAsync<AgentRunDto>($"/api/tasks/{normal.Id}/runs", new { })).ModelId);
    }
}
