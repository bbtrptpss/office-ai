using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OfficeAI.Api.Infrastructure;
using OfficeAI.Application.Agents;
using OfficeAI.Application.Organization;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Agents;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

/// <summary>Runs interrupted by a server restart continue from their last checkpoint without repeating side effects.</summary>
[Collection(ApiCollection.Name)]
public class RunResumeTests(ApiFixture fx)
{
    private async Task<(HttpClient Ceo, Guid TaskId, string ModelId)> AgentTaskAsync(AiResponse[] script)
    {
        var ceo = await fx.CeoAsync();
        var role = (await ceo.GetAsync<List<RoleDefinitionDto>>("/api/role-definitions")).First(r => r.Code == "BACKEND");
        var agent = await ceo.PostAsync<EmployeeDto>("/api/employees",
            new { displayName = $"Rs {Guid.NewGuid():N}"[..20], kind = "AI", departmentId = role.DepartmentId, roleDefinitionId = role.Id });
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
        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"RS-{Guid.NewGuid():N}"[..14], name = "Resume project", status = "Active" });
        var task = await ceo.PostAsync<TaskDto>($"/api/projects/{project.Id}/tasks", new { title = "Resumable work" });
        await ceo.PostAsync<TaskDto>($"/api/tasks/{task.Id}/assign", new { employeeId = agent.Id });
        return (ceo, task.Id, modelId);
    }

    private static async Task<AgentRunDetailDto> WaitAsync(HttpClient c, Guid runId)
    {
        for (var i = 0; i < 100; i++)
        {
            var d = await c.GetAsync<AgentRunDetailDto>($"/api/agent-runs/{runId}");
            if (d.Run.Status is not (AgentRunStatus.Queued or AgentRunStatus.Running)) return d;
            await Task.Delay(100);
        }
        throw new TimeoutException();
    }

    private AgentRunWorker Worker => fx.Factory.Services.GetServices<IHostedService>().OfType<AgentRunWorker>().Single();

    /// <summary>Puts a finished run back into the state of a server that died mid-run with the given checkpoint.</summary>
    private async Task SimulateCrashAsync(Guid runId, Func<List<AiMessage>, List<AiMessage>> checkpoint, int resumeCount = 0)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var run = await db.AgentRuns.FirstAsync(r => r.Id == runId);
        var conversation = checkpoint(ConversationCodec.Decode(run.ConversationJson!));
        run.ConversationJson = ConversationCodec.Encode(conversation);
        run.Turns = conversation.Count(m => m.Role == AiRole.Assistant);
        (run.Status, run.FinishedAt, run.StopReason, run.Error, run.ResumeCount) = (AgentRunStatus.Running, null, null, null, resumeCount);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task An_interrupted_run_resumes_from_its_checkpoint_without_repeating_tool_side_effects()
    {
        var (ceo, task, modelId) = await AgentTaskAsync(
            [ScriptedProvider.Call("deliverable_submit", new { title = "Report", content = "Findings" }), ScriptedProvider.Text("done")]);
        var run = await ceo.PostAsync<AgentRunDto>($"/api/tasks/{task}/runs", new { });
        Assert.Equal(AgentRunStatus.Succeeded, (await WaitAsync(ceo, run.Id)).Run.Status);
        Assert.Single(await ceo.GetAsync<List<DeliverableDto>>($"/api/tasks/{task}/deliverables"));

        // The server died after the model asked for the tool (the tool had already run) but before the next checkpoint.
        await SimulateCrashAsync(run.Id, c => c.Take(2).ToList());
        var calls = 0;
        fx.Scripted.Register(modelId, (_, _) => { Interlocked.Increment(ref calls); return Task.FromResult(ScriptedProvider.Text("finished after restart")); });

        await Worker.RecoverAsync(CancellationToken.None);
        var resumed = await WaitAsync(ceo, run.Id);

        Assert.Equal(AgentRunStatus.Succeeded, resumed.Run.Status);
        Assert.Single(await ceo.GetAsync<List<DeliverableDto>>($"/api/tasks/{task}/deliverables")); // not submitted twice
        Assert.Single(resumed.ToolExecutions);
        Assert.Equal(1, calls); // only the lost model call was repeated
        Assert.Contains(resumed.Steps, s => s.Kind == RunStepKind.Notice && s.Content.Contains("restarted"));
        Assert.Contains(resumed.Steps, s => s.Kind == RunStepKind.AssistantText && s.Content == "finished after restart");
        Assert.Single(resumed.Steps, s => s.Kind == RunStepKind.Instructions); // the run was not started over
    }

    [Fact]
    public async Task A_run_that_keeps_getting_interrupted_fails_instead_of_looping()
    {
        var (ceo, task, _) = await AgentTaskAsync([ScriptedProvider.Text("done")]);
        var run = await ceo.PostAsync<AgentRunDto>($"/api/tasks/{task}/runs", new { });
        await WaitAsync(ceo, run.Id);

        await SimulateCrashAsync(run.Id, c => c.Take(1).ToList(), resumeCount: AgentRunWorker.MaxResumes);
        await Worker.RecoverAsync(CancellationToken.None);
        var detail = await ceo.GetAsync<AgentRunDetailDto>($"/api/agent-runs/{run.Id}");
        Assert.Equal(AgentRunStatus.Failed, detail.Run.Status);
        Assert.Equal("interrupted", detail.Run.StopReason);
    }

    [Fact]
    public void The_conversation_codec_round_trips_every_content_kind()
    {
        List<AiMessage> messages =
        [
            new(AiRole.User, [new AiText("สวัสดี \"quoted\"")]),
            new(AiRole.Assistant, [new AiText("ok"), new AiToolCall("c1", "task_read_context", System.Text.Json.JsonSerializer.SerializeToElement(new { a = 1, b = new[] { "x" } })),
                new AiOpaque("anthropic", System.Text.Json.JsonSerializer.SerializeToElement(new { type = "thinking", thinking = "t", signature = "s" }))]),
            new(AiRole.User, [new AiToolResult("c1", "result", true)]),
        ];
        var back = ConversationCodec.Decode(ConversationCodec.Encode(messages));
        Assert.Equal(ConversationCodec.Encode(messages), ConversationCodec.Encode(back));
        Assert.Equal("สวัสดี \"quoted\"", Assert.IsType<AiText>(back[0].Content[0]).Text);
        Assert.True(Assert.IsType<AiToolResult>(back[2].Content[0]).IsError);
    }
}
