using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Agents;
using OfficeAI.Application.Communication;
using OfficeAI.Application.Organization;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Communication;
using OfficeAI.Domain.Security;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

/// <summary>AI employees answer people who @mention them in chat: talk only, priced, budgeted, never AI-to-AI.</summary>
[Collection(ApiCollection.Name)]
public class ChatAiReplyTests(ApiFixture fx)
{
    /// <summary>An AI employee on a scripted model priced at 1 USD per million input and output tokens.</summary>
    private async Task<EmployeeDto> AiAsync(HttpClient ceo, Func<AiRequest, CancellationToken, Task<AiResponse>> script, decimal budget = 5m, string provider = ScriptedProvider.ProviderName)
    {
        var role = (await ceo.GetAsync<List<RoleDefinitionDto>>("/api/role-definitions")).First(r => r.Code == "BACKEND");
        var agent = await ceo.PostAsync<EmployeeDto>("/api/employees",
            new { displayName = $"Chatty {Guid.NewGuid():N}"[..20], kind = "AI", departmentId = role.DepartmentId, roleDefinitionId = role.Id });
        var modelId = $"script-{Guid.NewGuid():N}";
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AiModels.Add(new AiModel { Provider = provider, ModelId = modelId, DisplayName = modelId, InputUsdPerMTok = 1m, OutputUsdPerMTok = 1m });
            await db.SaveChangesAsync();
        }
        fx.Scripted.Register(modelId, script);
        var c = await ceo.GetAsync<AgentConfigDto>($"/api/agents/{agent.Id}/config");
        (await ceo.PutAsJsonAsync($"/api/agents/{agent.Id}/config", new
        {
            provider, modelId, effort = "Medium", c.MaxTokensPerCall, c.MaxTurnsPerRun, c.MaxToolCallsPerRun,
            c.MaxTokensPerRun, c.TimeoutSeconds, monthlyBudgetUsd = budget, isEnabled = true,
        }, ApiFixture.Json)).EnsureSuccessStatusCode();
        return agent;
    }

    private static async Task<ChannelDto> GeneralAsync(HttpClient c) =>
        (await c.GetAsync<List<ChannelDto>>("/api/chat/channels")).Single(x => x.Name == ChatService.GeneralChannel);

    private static async Task<List<MessageDto>> RepliesFromAsync(HttpClient c, Guid channelId, Guid employeeId, int expected)
    {
        for (var i = 0; i < 100; i++)
        {
            var found = (await c.GetAsync<List<MessageDto>>($"/api/chat/channels/{channelId}/messages?limit=200"))
                .Where(m => m.AuthorEmployeeId == employeeId).ToList();
            if (found.Count >= expected) return found;
            await Task.Delay(100);
        }
        throw new TimeoutException("No AI reply appeared.");
    }

    private async Task<ChatReply> ReplyRowAsync(Guid triggerId, Guid employeeId)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().ChatReplies.AsNoTracking()
            .SingleAsync(r => r.TriggerMessageId == triggerId && r.EmployeeId == employeeId);
    }

    [Fact]
    public async Task A_mentioned_AI_answers_in_the_channel_with_the_conversation_as_context_and_the_cost_is_recorded()
    {
        var ceo = await fx.CeoAsync();
        AiRequest? seen = null;
        var ai = await AiAsync(ceo, (req, _) =>
        {
            seen = req;
            return Task.FromResult(ScriptedProvider.Text("ใช้ PostgreSQL ครับ", inTok: 2_000, outTok: 500));
        });
        var general = await GeneralAsync(ceo);
        await ceo.PostAsync<MessageDto>($"/api/chat/channels/{general.Id}/messages", new { body = "เราจะใช้ระบบเก็บข้อมูลอะไรดี" });
        var question = await ceo.PostAsync<MessageDto>($"/api/chat/channels/{general.Id}/messages",
            new { body = $"@{ai.DisplayName} คิดว่ายังไง", mentionedEmployeeIds = new[] { ai.Id } });

        var answer = Assert.Single(await RepliesFromAsync(ceo, general.Id, ai.Id, 1));
        Assert.Equal("ใช้ PostgreSQL ครับ", answer.Body);
        Assert.True(answer.IsAgent);

        // Context: the earlier message and the question; no tools offered (talk only).
        var prompt = Assert.IsType<AiText>(Assert.Single(Assert.Single(seen!.Messages).Content)).Text;
        Assert.Contains("เราจะใช้ระบบเก็บข้อมูลอะไรดี", prompt);
        Assert.Contains("คิดว่ายังไง", prompt);
        Assert.Empty(seen.Tools);
        Assert.True(seen.MaxTokens <= ChatReplyService.MaxReplyTokens);

        var row = await ReplyRowAsync(question.Id, ai.Id);
        Assert.Equal(ChatReplyStatus.Answered, row.Status);
        Assert.Equal(0.0025m, row.CostUsd); // (2000 + 500) tokens at 1 USD / M
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var cost = await scope.ServiceProvider.GetRequiredService<AppDbContext>().CostRecords.SingleAsync(c => c.EmployeeId == ai.Id);
        Assert.Equal(CostKind.Actual, cost.Kind);
        Assert.Equal("ai_chat", cost.Category);
        Assert.Equal(0.0025m, cost.AmountUsd);
    }

    [Fact]
    public async Task Without_budget_or_a_working_provider_the_AI_declines_politely_and_nothing_is_charged()
    {
        var ceo = await fx.CeoAsync();
        var calls = 0;
        var broke = await AiAsync(ceo, (_, _) => { calls++; return Task.FromResult(ScriptedProvider.Text("x")); }, budget: 0m);
        var offline = await AiAsync(ceo, (_, _) => { calls++; return Task.FromResult(ScriptedProvider.Text("x")); }, provider: "gemini"); // no key in tests
        var general = await GeneralAsync(ceo);
        await ceo.PostAsync<MessageDto>($"/api/chat/channels/{general.Id}/messages",
            new { body = "ช่วยตอบหน่อย", mentionedEmployeeIds = new[] { broke.Id, offline.Id } });

        Assert.Contains("งบ", Assert.Single(await RepliesFromAsync(ceo, general.Id, broke.Id, 1)).Body);
        Assert.Contains("API key", Assert.Single(await RepliesFromAsync(ceo, general.Id, offline.Id, 1)).Body);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Provider_errors_are_reported_in_chat_and_at_most_three_AIs_answer_one_message()
    {
        var ceo = await fx.CeoAsync();
        var failing = await AiAsync(ceo, (_, _) => throw new AiProviderException("Your credit balance is too low.", retryable: false));
        var general = await GeneralAsync(ceo);
        var q = await ceo.PostAsync<MessageDto>($"/api/chat/channels/{general.Id}/messages", new { body = "hello", mentionedEmployeeIds = new[] { failing.Id } });
        Assert.Contains("เครดิต", Assert.Single(await RepliesFromAsync(ceo, general.Id, failing.Id, 1)).Body);
        var row = await ReplyRowAsync(q.Id, failing.Id);
        Assert.Equal(ChatReplyStatus.Failed, row.Status);
        Assert.Contains("credit balance", row.Error);

        var many = new List<EmployeeDto>();
        for (var i = 0; i < 4; i++) many.Add(await AiAsync(ceo, (_, _) => Task.FromResult(ScriptedProvider.Text("hi"))));
        var msg = await ceo.PostAsync<MessageDto>($"/api/chat/channels/{general.Id}/messages", new { body = "everyone?", mentionedEmployeeIds = many.Select(m => m.Id) });
        await RepliesFromAsync(ceo, general.Id, many[2].Id, 1);
        await Task.Delay(500);
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        Assert.Equal(ChatService.MaxAiRepliesPerMessage, await scope.ServiceProvider.GetRequiredService<AppDbContext>().ChatReplies.CountAsync(r => r.TriggerMessageId == msg.Id));
    }

    [Fact]
    public async Task Mentioning_a_person_or_being_a_viewer_never_triggers_AI()
    {
        var ceo = await fx.CeoAsync();
        var auditor = await fx.UserWithRolesAsync(SystemRoles.Auditor);
        var general = await GeneralAsync(ceo);
        var person = (await ceo.GetAsync<List<EmployeeDto>>("/api/employees")).First(e => e.Kind == Domain.Organization.EmployeeKind.Human);
        var msg = await ceo.PostAsync<MessageDto>($"/api/chat/channels/{general.Id}/messages", new { body = "hi", mentionedEmployeeIds = new[] { person.Id } });
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().ChatReplies.AnyAsync(r => r.TriggerMessageId == msg.Id));
        Assert.Equal(System.Net.HttpStatusCode.Forbidden,
            (await auditor.PostAsJsonAsync($"/api/chat/channels/{general.Id}/messages", new { body = "x", mentionedEmployeeIds = new[] { person.Id } })).StatusCode);
    }
}
