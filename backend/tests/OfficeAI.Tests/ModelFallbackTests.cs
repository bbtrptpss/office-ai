using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Agents;
using OfficeAI.Application.Communication;
using OfficeAI.Application.Organization;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Communication;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

/// <summary>When a model cannot answer (quota used up, provider down, key unusable), the company fallback order takes over.</summary>
[Collection(ApiCollection.Name)]
public class ModelFallbackTests(ApiFixture fx)
{
    private static AiProviderException DailyQuota() =>
        new("Gemini rate limit reached: Quota exceeded for GenerateRequestsPerDay", retryable: true, rateLimited: true);

    [Fact]
    public void Only_provider_problems_switch_models_not_bad_requests()
    {
        Assert.True(AiFallback.ShouldSwitch(DailyQuota()));
        Assert.True(AiFallback.ShouldSwitch(new AiProviderException("Could not reach the API.", retryable: true)));
        Assert.True(AiFallback.ShouldSwitch(new AiProviderException("OpenAI rejected the request (401): bad key", retryable: false, accountProblem: true)));
        Assert.True(AiFallback.ShouldSwitch(new AiProviderException("Anthropic rejected the request: Your credit balance is too low", retryable: false)));
        Assert.True(AiFallback.ShouldSwitch(new AiProviderException("Ollama is not configured.", retryable: false)));
        Assert.False(AiFallback.ShouldSwitch(new AiProviderException("OpenAI rejected the request (400): context too long", retryable: false)));
    }

    /// <summary>A scripted model priced at 1 USD per million tokens; returns its AiModel id and model id.</summary>
    private async Task<(Guid Id, string ModelId)> ModelAsync(Func<AiRequest, CancellationToken, Task<AiResponse>> script)
    {
        var modelId = $"script-fb-{Guid.NewGuid():N}";
        var model = new AiModel { Provider = ScriptedProvider.ProviderName, ModelId = modelId, DisplayName = modelId, Tier = ModelTier.Standard, InputUsdPerMTok = 1m, OutputUsdPerMTok = 1m };
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AiModels.Add(model);
            await db.SaveChangesAsync();
        }
        fx.Scripted.Register(modelId, script);
        return (model.Id, modelId);
    }

    private static async Task<EmployeeDto> AgentAsync(HttpClient ceo, string provider, string modelId)
    {
        var role = (await ceo.GetAsync<List<RoleDefinitionDto>>("/api/role-definitions")).First(r => r.Code == "BACKEND");
        var agent = await ceo.PostAsync<EmployeeDto>("/api/employees",
            new { displayName = $"Fallback {Guid.NewGuid():N}"[..20], kind = "AI", departmentId = role.DepartmentId, roleDefinitionId = role.Id });
        var c = await ceo.GetAsync<AgentConfigDto>($"/api/agents/{agent.Id}/config");
        (await ceo.PutAsJsonAsync($"/api/agents/{agent.Id}/config", new
        {
            provider, modelId, effort = "Medium", c.MaxTokensPerCall, c.MaxTurnsPerRun, c.MaxToolCallsPerRun,
            c.MaxTokensPerRun, c.TimeoutSeconds, monthlyBudgetUsd = 100m, isEnabled = true,
        }, ApiFixture.Json)).EnsureSuccessStatusCode();
        return agent;
    }

    private static async Task<TaskDto> TaskForAsync(HttpClient ceo, Guid agentId)
    {
        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"FB-{Guid.NewGuid():N}"[..14], name = "Fallback", status = "Active" });
        var task = await ceo.PostAsync<TaskDto>($"/api/projects/{project.Id}/tasks", new { title = "Write the spec" });
        return await ceo.PostAsync<TaskDto>($"/api/tasks/{task.Id}/assign", new { employeeId = agentId });
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

    /// <summary>The fallback order is company-wide: every test that sets one clears it again so other tests are unaffected.</summary>
    private static async Task WithFallbackAsync(HttpClient ceo, Guid[] order, Func<Task> body)
    {
        (await ceo.PutAsJsonAsync("/api/ai/fallback", new { modelIds = order }, ApiFixture.Json)).EnsureSuccessStatusCode();
        try
        {
            await body();
        }
        finally
        {
            (await ceo.PutAsJsonAsync("/api/ai/fallback", new { modelIds = Array.Empty<Guid>() }, ApiFixture.Json)).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task A_run_whose_model_runs_out_of_quota_continues_on_the_fallback_and_the_next_run_starts_there()
    {
        var ceo = await fx.CeoAsync();
        var primaryCalls = 0;
        var (_, primary) = await ModelAsync((_, _) =>
        {
            // First turn works (reads the task), then the daily quota runs out mid-run.
            if (Interlocked.Increment(ref primaryCalls) == 1) return Task.FromResult(ScriptedProvider.Call("task_read_context", new { }));
            throw DailyQuota();
        });
        var seen = new List<AiRequest>();
        var (backupId, backup) = await ModelAsync((req, _) => { lock (seen) seen.Add(req); return Task.FromResult(ScriptedProvider.Text("done", inTok: 1_000, outTok: 0)); });
        var agent = await AgentAsync(ceo, ScriptedProvider.ProviderName, primary);

        await WithFallbackAsync(ceo, [backupId], async () =>
        {
            Assert.Equal(1, (await ceo.GetAsync<List<AiModelDto>>("/api/ai/models")).Single(m => m.Id == backupId).FallbackRank);

            var task = await TaskForAsync(ceo, agent.Id);
            var run = await ceo.PostAsync<AgentRunDto>($"/api/tasks/{task.Id}/runs", new { });
            Assert.Equal(primary, run.ModelId);
            var done = await WaitAsync(ceo, run.Id);
            Assert.Equal(AgentRunStatus.Succeeded, done.Run.Status);
            Assert.Equal(backup, done.Run.ModelId);
            Assert.Contains(done.Steps, s => s.Kind == RunStepKind.Notice && s.Content.Contains($"to the fallback model scripted/{backup}") && s.Content.Contains("PerDay"));
            // The fallback continued the same conversation: it saw the tool result from the first model's turn.
            Assert.Contains(seen.Single().Messages, m => m.Content.OfType<AiToolResult>().Any());

            await using (var scope = fx.Factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var usage = await db.UsageRecords.AsNoTracking().Where(u => u.RunId == run.Id).OrderBy(u => u.OccurredAt).Select(u => u.ModelId).ToListAsync();
                Assert.Equal([primary, backup], usage); // each turn priced on the model that answered it
                Assert.Equal(0.000120m + 0.001m, await db.CostRecords.Where(c => c.RunId == run.Id).SumAsync(c => c.AmountUsd));
            }

            // The quota is still used up: the next run starts on the fallback without calling the primary again.
            var next = await ceo.PostAsync<AgentRunDto>($"/api/tasks/{(await TaskForAsync(ceo, agent.Id)).Id}/runs", new { });
            var nextDone = await WaitAsync(ceo, next.Id);
            Assert.Equal(AgentRunStatus.Succeeded, nextDone.Run.Status);
            Assert.Equal(backup, nextDone.Run.ModelId);
            Assert.Equal(2, primaryCalls);
        });
    }

    [Fact]
    public async Task Without_a_fallback_the_run_fails_as_before()
    {
        var ceo = await fx.CeoAsync();
        var (_, primary) = await ModelAsync((_, _) => throw DailyQuota());
        var agent = await AgentAsync(ceo, ScriptedProvider.ProviderName, primary);
        var run = await ceo.PostAsync<AgentRunDto>($"/api/tasks/{(await TaskForAsync(ceo, agent.Id)).Id}/runs", new { });
        var done = await WaitAsync(ceo, run.Id);
        Assert.Equal(AgentRunStatus.Failed, done.Run.Status);
        Assert.Contains("PerDay", done.Run.Error);
    }

    [Fact]
    public async Task An_employee_whose_provider_is_not_set_up_works_on_the_fallback()
    {
        var ceo = await fx.CeoAsync();
        var gemini = $"gemini-fb-{Guid.NewGuid():N}";
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AiModels.Add(new AiModel { Provider = "gemini", ModelId = gemini, DisplayName = gemini, InputUsdPerMTok = 1m, OutputUsdPerMTok = 1m });
            await db.SaveChangesAsync();
        }
        var agent = await AgentAsync(ceo, "gemini", gemini); // no Gemini key in tests
        var task = await TaskForAsync(ceo, agent.Id);
        var refused = await ceo.PostAsJsonAsync($"/api/tasks/{task.Id}/runs", new { }, ApiFixture.Json);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        var (backupId, backup) = await ModelAsync((_, _) => Task.FromResult(ScriptedProvider.Text("done")));
        await WithFallbackAsync(ceo, [backupId], async () =>
        {
            var run = await ceo.PostAsync<AgentRunDto>($"/api/tasks/{task.Id}/runs", new { });
            Assert.Equal(backup, run.ModelId);
            Assert.Equal(AgentRunStatus.Succeeded, (await WaitAsync(ceo, run.Id)).Run.Status);
        });
    }

    [Fact]
    public async Task A_chat_answer_falls_back_too()
    {
        var ceo = await fx.CeoAsync();
        var (_, primary) = await ModelAsync((_, _) => throw new AiProviderException("OpenAI rejected the request (402): billing", retryable: false, accountProblem: true));
        var (backupId, backup) = await ModelAsync((_, _) => Task.FromResult(ScriptedProvider.Text("ตอบจากโมเดลสำรองครับ")));
        var agent = await AgentAsync(ceo, ScriptedProvider.ProviderName, primary);
        var general = (await ceo.GetAsync<List<ChannelDto>>("/api/chat/channels")).Single(x => x.Name == ChatService.GeneralChannel);

        await WithFallbackAsync(ceo, [backupId], async () =>
        {
            var question = await ceo.PostAsync<MessageDto>($"/api/chat/channels/{general.Id}/messages",
                new { body = $"@{agent.DisplayName} สวัสดี", mentionedEmployeeIds = new[] { agent.Id } });
            ChatReply? row = null;
            for (var i = 0; i < 100 && row?.Status is null or ChatReplyStatus.Queued or ChatReplyStatus.Running; i++)
            {
                await Task.Delay(100);
                await using var scope = fx.Factory.Services.CreateAsyncScope();
                row = await scope.ServiceProvider.GetRequiredService<AppDbContext>().ChatReplies.AsNoTracking()
                    .SingleOrDefaultAsync(r => r.TriggerMessageId == question.Id && r.EmployeeId == agent.Id);
            }
            Assert.Equal(ChatReplyStatus.Answered, row!.Status);
            Assert.Equal(backup, row.ModelId);
        });
    }

    [Fact]
    public async Task The_fallback_order_takes_only_enabled_real_models_listed_once()
    {
        var ceo = await fx.CeoAsync();
        var models = await ceo.GetAsync<List<AiModelDto>>("/api/ai/models");
        var simulated = models.First(m => m.Provider == AgentDefaults.SimulatedProvider);
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PutAsJsonAsync("/api/ai/fallback", new { modelIds = new[] { simulated.Id } }, ApiFixture.Json)).StatusCode);

        var (id, _) = await ModelAsync((_, _) => Task.FromResult(ScriptedProvider.Text("x")));
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PutAsJsonAsync("/api/ai/fallback", new { modelIds = new[] { id, id } }, ApiFixture.Json)).StatusCode);

        await WithFallbackAsync(ceo, [id], async () =>
        {
            // Disabling a model takes it out of the order.
            var m = (await ceo.GetAsync<List<AiModelDto>>("/api/ai/models")).Single(x => x.Id == id);
            var updated = await ceo.PutAsync<AiModelDto>($"/api/ai/models/{id}", new
            {
                m.InputUsdPerMTok, m.OutputUsdPerMTok, m.CacheReadUsdPerMTok, m.CacheWriteUsdPerMTok, isEnabled = false, m.Version,
            });
            Assert.Null(updated.FallbackRank);
        });
    }
}
