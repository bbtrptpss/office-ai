using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Agents;
using OfficeAI.Application.Communication;
using OfficeAI.Application.Organization;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Communication;
using OfficeAI.Domain.Security;
using OfficeAI.Infrastructure.Persistence;
using OfficeAI.Infrastructure.Security;

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
        Assert.Contains("Company snapshot", seen.SystemPrompt); // real data for "summarize today"
        Assert.Contains("Approvals waiting for a person", seen.SystemPrompt);

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

    private static async Task<(Guid Project, ChannelDto Channel)> ProjectChannelAsync(HttpClient ceo, params Guid[] members)
    {
        var project = (await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"RUN-{Guid.NewGuid():N}"[..14], name = "Chat run", status = "Active" })).Id;
        foreach (var m in members) (await ceo.PostAsync($"/api/projects/{project}/members/{m}", null)).EnsureSuccessStatusCode();
        return (project, await ceo.PostAsync<ChannelDto>("/api/chat/channels/open", new { projectId = project }));
    }

    [Fact]
    public async Task Run_in_a_project_channel_creates_an_assigned_task_runs_it_with_tools_and_reports_back()
    {
        var ceo = await fx.CeoAsync();
        AiRequest? seen = null;
        var ai = await AiAsync(ceo, (req, _) =>
        {
            seen = req;
            return Task.FromResult(ScriptedProvider.Text("เพิ่มหน้า login เรียบร้อยครับ"));
        });
        var (project, channel) = await ProjectChannelAsync(ceo, ai.Id);
        var msg = await ceo.PostAsync<MessageDto>($"/api/chat/channels/{channel.Id}/messages",
            new { body = $"@{ai.DisplayName} /run เพิ่มหน้า login", mentionedEmployeeIds = new[] { ai.Id } });

        var said = await RepliesFromAsync(ceo, channel.Id, ai.Id, 2);
        Assert.Contains("รับงานแล้ว", said[0].Body);
        Assert.Contains("✅", said[1].Body);
        Assert.Contains("เพิ่มหน้า login เรียบร้อยครับ", said[1].Body);
        Assert.DoesNotContain("InProgress", said[1].Body); // task status is shown in Thai

        var task = Assert.Single(await ceo.GetAsync<List<TaskDto>>($"/api/projects/{project}/tasks"));
        Assert.Equal("เพิ่มหน้า login", task.Title);
        Assert.Equal(ai.Id, Assert.Single(task.Assignees).EmployeeId);
        Assert.NotEmpty(seen!.Tools); // a real work run, unlike a chat answer

        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var command = await db.ChatCommands.AsNoTracking().SingleAsync(c => c.TriggerMessageId == msg.Id);
        Assert.Equal(ChatCommandStatus.Finished, command.Status);
        Assert.Equal(task.Id, command.TaskId);
        Assert.False(await db.ChatReplies.AnyAsync(r => r.TriggerMessageId == msg.Id)); // a command is not also answered as chat
        var run = await db.AgentRuns.AsNoTracking().SingleAsync(r => r.Id == command.RunId);
        Assert.Equal(AgentRunStatus.Succeeded, run.Status);
    }

    [Fact]
    public async Task Run_is_refused_without_permission_outside_project_channels_and_for_several_AIs()
    {
        var ceo = await fx.CeoAsync();
        var calls = 0;
        var ai = await AiAsync(ceo, (_, _) => { calls++; return Task.FromResult(ScriptedProvider.Text("x")); });
        var other = await AiAsync(ceo, (_, _) => { calls++; return Task.FromResult(ScriptedProvider.Text("x")); });
        var member = await fx.UserWithRolesAsync(SystemRoles.Member);

        var general = await GeneralAsync(ceo);
        await ceo.PostAsync<MessageDto>($"/api/chat/channels/{general.Id}/messages",
            new { body = $"@{ai.DisplayName} /run something", mentionedEmployeeIds = new[] { ai.Id } });
        Assert.Contains("ห้องแชทของโปรเจกต์", Assert.Single(await RepliesFromAsync(ceo, general.Id, ai.Id, 1)).Body);

        var (project, channel) = await ProjectChannelAsync(ceo, ai.Id, other.Id);
        await member.PostAsync<MessageDto>($"/api/chat/channels/{general.Id}/messages",
            new { body = $"@{other.DisplayName} /run something", mentionedEmployeeIds = new[] { other.Id } });
        Assert.Contains("สิทธิ์", Assert.Single(await RepliesFromAsync(ceo, general.Id, other.Id, 1)).Body);

        await ceo.PostAsync<MessageDto>($"/api/chat/channels/{channel.Id}/messages",
            new { body = $"@{ai.DisplayName} @{other.DisplayName} /run something", mentionedEmployeeIds = new[] { ai.Id, other.Id } });
        Assert.Contains("ทีละ 1 คน", Assert.Single(await RepliesFromAsync(ceo, channel.Id, ai.Id, 1)).Body);

        Assert.Empty(await ceo.GetAsync<List<TaskDto>>($"/api/projects/{project}/tasks"));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task The_dashboard_assistant_has_its_channel_and_recent_tasks_span_projects()
    {
        var ceo = await fx.CeoAsync();
        var assistant = await ceo.GetAsync<AssistantDto>("/api/assistant");
        var channels = await ceo.GetAsync<List<ChannelDto>>("/api/chat/channels");
        Assert.Contains(channels, c => c.Id == assistant.ChannelId && c.Name == ChatService.AssistantChannel);
        Assert.All(assistant.Members, m => Assert.NotEqual(Domain.Organization.EmployeeStatus.Inactive, m.Status));

        var (project, _) = await ProjectChannelAsync(ceo);
        var task = await ceo.PostAsync<TaskDto>($"/api/projects/{project}/tasks", new { title = $"Recent {Guid.NewGuid():N}" });
        var recent = await ceo.GetAsync<List<RecentTaskDto>>("/api/tasks/recent?take=5");
        Assert.Equal(task.Id, recent[0].Id);
        Assert.Equal("Chat run", recent[0].ProjectName);
    }

    [Fact]
    public async Task Claude_chat_continues_one_conversation_per_person_is_recorded_and_is_for_AI_managers_only()
    {
        var ceo = await fx.CeoAsync();
        Assert.True((await ceo.GetAsync<AssistantDto>("/api/assistant")).ClaudeAvailable);

        var first = await ceo.PostAsync<ClaudeReplyDto>("/api/assistant/claude", new { message = "สรุปวันนี้", newChat = true });
        Assert.Equal("echo: สรุปวันนี้", first.Reply);
        Assert.False(first.Continued);
        var call = fx.Claude.Calls.Last();
        Assert.Null(call.SessionId);
        Assert.Contains("<action>", call.System); // it hands work to AI employees through action blocks
        Assert.Contains("Approvals waiting for a person", call.System); // live company data

        var second = await ceo.PostAsync<ClaudeReplyDto>("/api/assistant/claude", new { message = "แล้วพรุ่งนี้ล่ะ" });
        Assert.True(second.Continued);
        Assert.NotNull(fx.Claude.Calls.Last().SessionId);
        var fresh = await ceo.PostAsync<ClaudeReplyDto>("/api/assistant/claude", new { message = "เริ่มใหม่", newChat = true });
        Assert.False(fresh.Continued);

        var channel = (await ceo.GetAsync<AssistantDto>("/api/assistant")).ChannelId;
        var messages = await ceo.GetAsync<List<MessageDto>>($"/api/chat/channels/{channel}/messages?limit=10");
        Assert.Contains(messages, m => m.AuthorName == ClaudeChatService.ClaudeName && m.IsAgent && m.Body == "echo: เริ่มใหม่");

        var member = await fx.UserWithRolesAsync(SystemRoles.Member);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/assistant/claude", new { message = "hi" })).StatusCode);
    }

    [Fact]
    public async Task Terminal_runs_commands_for_the_CEO_only_and_audits_them()
    {
        var ceo = await fx.CeoAsync();
        var result = await ceo.PostAsync<TerminalResultDto>("/api/assistant/terminal", new { command = "git status" });
        Assert.Equal("ran: git status", result.Output);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("git status", fx.Claude.Commands.Last());
        var audit = await ceo.GetAsync<List<AuditLogDto>>("/api/audit?entityType=Terminal&take=5");
        Assert.Contains(audit, a => a.Action == "TerminalCommand" && (a.NewValues ?? "").Contains("git status"));

        var manager = await fx.UserWithRolesAsync(SystemRoles.Manager, SystemRoles.Member);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync("/api/assistant/terminal", new { command = "dir" })).StatusCode);
        Assert.Equal("git status", fx.Claude.Commands.Last());
    }

    [Fact]
    public async Task Claude_hands_work_to_an_AI_employee_which_runs_it_and_reports_in_the_assistant_channel()
    {
        var ceo = await fx.CeoAsync();
        var ai = await AiAsync(ceo, (_, _) => Task.FromResult(ScriptedProvider.Text("ทำหน้า login เสร็จแล้วครับ")));
        var (project, _) = await ProjectChannelAsync(ceo, ai.Id);
        var code = (await ceo.GetAsync<ProjectDto>($"/api/projects/{project}")).Code;
        fx.Claude.Script = _ =>
            $"ได้เลยครับ ส่งให้ {ai.DisplayName} ทำแล้ว\n<action>{{\"project\":\"{code.ToLower()}\",\"employee\":\"{ai.DisplayName}\",\"title\":\"ทำหน้า login\",\"details\":\"มีช่องอีเมลและรหัสผ่าน\"}}</action>" +
            "<action>{\"project\":\"NOPE\",\"employee\":\"x\",\"title\":\"ไม่มีโปรเจกต์นี้\"}</action>";
        try
        {
            var reply = await ceo.PostAsync<ClaudeReplyDto>("/api/assistant/claude", new { message = "ให้ทำหน้า login หน่อย", newChat = true });
            Assert.Equal(1, reply.TasksStarted);
            Assert.DoesNotContain("<action>", reply.Reply);
            Assert.Contains("✅ มอบงาน “ทำหน้า login”", reply.Reply);
            Assert.Contains("ไม่พบโปรเจกต์ “NOPE”", reply.Reply);
            Assert.Contains("Projects (code: name):", fx.Claude.Calls.Last().System);
            Assert.Contains(ai.DisplayName, fx.Claude.Calls.Last().System);
        }
        finally
        {
            fx.Claude.Script = null;
        }

        var task = Assert.Single(await ceo.GetAsync<List<TaskDto>>($"/api/projects/{project}/tasks"));
        Assert.Equal(ai.Id, Assert.Single(task.Assignees).EmployeeId);
        Assert.StartsWith("มีช่องอีเมลและรหัสผ่าน", task.Description);
        var channel = (await ceo.GetAsync<AssistantDto>("/api/assistant")).ChannelId;
        var said = await RepliesFromAsync(ceo, channel, ai.Id, 1);
        Assert.Contains(said, m => m.Body.Contains("✅") && m.Body.Contains("ทำหน้า login เสร็จแล้วครับ"));
    }

    [Theory]
    [InlineData("@Dev /run add login", "add login")]
    [InlineData("/ทำ  เพิ่มหน้า   รายงาน @Dev", "เพิ่มหน้า รายงาน")]
    [InlineData("@Dev /run", "")]
    [InlineData("@Dev please check /runbook", null)]
    [InlineData("@Dev see src/run/x", null)]
    public void Parse_finds_the_instruction(string body, string? expected) =>
        Assert.Equal(expected, ChatCommandService.Parse(body, ["Dev"]));
}
