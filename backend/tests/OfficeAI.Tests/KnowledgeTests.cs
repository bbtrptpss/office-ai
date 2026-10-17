using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Agents;
using OfficeAI.Application.Knowledge;
using OfficeAI.Application.Organization;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Knowledge;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class KnowledgeTests(ApiFixture fx)
{
    private async Task<(HttpClient Ceo, Guid TaskId, Guid ProjectId)> AgentTaskAsync(string roleCode, AiResponse[] script, Guid? projectId = null)
    {
        var ceo = await fx.CeoAsync();
        var role = (await ceo.GetAsync<List<RoleDefinitionDto>>("/api/role-definitions")).First(r => r.Code == roleCode);
        var agent = await ceo.PostAsync<EmployeeDto>("/api/employees",
            new { displayName = $"Kn {Guid.NewGuid():N}"[..20], kind = "AI", departmentId = role.DepartmentId, roleDefinitionId = role.Id });
        var modelId = $"script-{Guid.NewGuid():N}";
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AiModels.Add(new AiModel { Provider = ScriptedProvider.ProviderName, ModelId = modelId, DisplayName = modelId });
            await db.SaveChangesAsync();
        }
        fx.Scripted.Register(modelId, ScriptedProvider.Sequence(script));
        var c = await ceo.GetAsync<AgentConfigDto>($"/api/agents/{agent.Id}/config");
        (await ceo.PutAsJsonAsync($"/api/agents/{agent.Id}/config", new
        {
            provider = ScriptedProvider.ProviderName, modelId, effort = "Medium", c.MaxTokensPerCall, c.MaxTurnsPerRun, c.MaxToolCallsPerRun,
            c.MaxTokensPerRun, c.TimeoutSeconds, c.MonthlyBudgetUsd, isEnabled = true,
        }, ApiFixture.Json)).EnsureSuccessStatusCode();
        var pid = projectId ?? (await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"KN-{Guid.NewGuid():N}"[..14], name = "Knowledge project", status = "Active" })).Id;
        var task = await ceo.PostAsync<TaskDto>($"/api/projects/{pid}/tasks", new { title = "Knowledge work" });
        await ceo.PostAsync<TaskDto>($"/api/tasks/{task.Id}/assign", new { employeeId = agent.Id });
        return (ceo, task.Id, pid);
    }

    private static async Task<AgentRunDetailDto> RunAsync(HttpClient c, Guid taskId)
    {
        var run = await c.PostAsync<AgentRunDto>($"/api/tasks/{taskId}/runs", new { });
        for (var i = 0; i < 100; i++)
        {
            var d = await c.GetAsync<AgentRunDetailDto>($"/api/agent-runs/{run.Id}");
            if (d.Run.Status is not (AgentRunStatus.Queued or AgentRunStatus.Running)) return d;
            await Task.Delay(100);
        }
        throw new TimeoutException();
    }

    private static AiResponse Search(string q) => ScriptedProvider.Call("knowledge_search", new { query = q });

    [Fact]
    public async Task Agent_knowledge_is_not_trusted_until_a_person_validates_it()
    {
        var marker = $"zebra{Guid.NewGuid():N}"[..14];
        var (ceo, task, project) = await AgentTaskAsync("PM",
            [ScriptedProvider.Call("knowledge_propose", new { kind = "Lesson", title = $"Estimate tests at 1:3 {marker}", body = "Testing took a third of build time.", scope = "company", tags = "estimation" })]);
        await RunAsync(ceo, task);
        var proposed = Assert.Single(await ceo.GetAsync<List<KnowledgeDto>>($"/api/knowledge?q={marker}"));
        Assert.Equal(KnowledgeStatus.Proposed, proposed.Status);
        Assert.StartsWith("task:", proposed.Source);

        // Another agent searching cannot see the unvalidated entry.
        var (_, searchTask, _) = await AgentTaskAsync("BACKEND", [Search(marker)]);
        var exec = Assert.Single((await RunAsync(ceo, searchTask)).ToolExecutions);
        Assert.Equal("[]", exec.Output);

        await ceo.PostAsync<KnowledgeDto>($"/api/knowledge/{proposed.Id}/status", new { status = "Validated" });
        var (_, searchTask2, _) = await AgentTaskAsync("BACKEND", [Search(marker)]);
        exec = Assert.Single((await RunAsync(ceo, searchTask2)).ToolExecutions);
        Assert.Contains(proposed.Id.ToString(), exec.Output);
        Assert.Contains("task:", exec.Output); // the source travels with the answer
    }

    [Fact]
    public async Task Project_knowledge_stays_inside_its_project()
    {
        var ceo = await fx.CeoAsync();
        var marker = $"otter{Guid.NewGuid():N}"[..14];
        var secret = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"SC-{Guid.NewGuid():N}"[..14], name = "Secret client", status = "Active" });
        var entry = await ceo.PostAsync<KnowledgeDto>("/api/knowledge", new { kind = "Reference", title = $"Client API keys location {marker}", body = "Vault path x", projectId = secret.Id });
        Assert.Equal(KnowledgeStatus.Validated, entry.Status); // people's own entries are validated

        var (_, inside, _) = await AgentTaskAsync("BACKEND", [Search(marker)], secret.Id);
        Assert.Contains(entry.Id.ToString(), Assert.Single((await RunAsync(ceo, inside)).ToolExecutions).Output);

        var (_, outside, _) = await AgentTaskAsync("BACKEND", [Search(marker)]);
        Assert.Equal("[]", Assert.Single((await RunAsync(ceo, outside)).ToolExecutions).Output);
    }

    [Fact]
    public async Task Documents_are_versioned_and_only_people_finalize_them()
    {
        var (ceo, task, project) = await AgentTaskAsync("DOCS",
        [
            ScriptedProvider.Call("document_write", new { kind = "ReleaseNotes", title = "Release 1.0", content = "# 1.0\n- first" }),
            ScriptedProvider.Call("document_write", new { kind = "ReleaseNotes", title = "Release 1.0", content = "# 1.0\n- first\n- second" }),
        ]);
        var run = await RunAsync(ceo, task);
        Assert.True(run.ToolExecutions.All(x => x.Status == ToolExecutionStatus.Succeeded), string.Join(" | ", run.ToolExecutions.Select(x => x.ToolId + ": " + x.Error)));
        var docs = await ceo.GetAsync<List<DocumentDto>>($"/api/projects/{project}/documents");
        Assert.Equal([2, 1], docs.Select(d => d.Version));
        Assert.All(docs, d => Assert.Equal(DocumentStatus.Draft, d.Status));

        var latest = await ceo.GetAsync<DocumentDto>($"/api/documents/{docs[0].Id}");
        Assert.Contains("- second", latest.Content);
        var file = await ceo.GetAsync($"/api/documents/{latest.Id}/download");
        Assert.Equal("text/markdown", file.Content.Headers.ContentType!.MediaType);

        var member = await fx.UserWithRolesAsync("Member");
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync($"/api/documents/{latest.Id}/finalize", new { })).StatusCode);
        Assert.Equal(DocumentStatus.Final, (await ceo.PostAsync<DocumentDto>($"/api/documents/{latest.Id}/finalize", new { })).Status);
    }
}
