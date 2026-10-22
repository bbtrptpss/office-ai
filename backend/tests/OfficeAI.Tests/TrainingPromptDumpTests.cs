using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Agents;
using OfficeAI.Application.Organization;
using OfficeAI.Application.Work;
using OfficeAI.Application.Workflows;
using OfficeAI.Domain.Agents;
using OfficeAI.Infrastructure.Ai;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

/// <summary>
/// Not a check: writes the exact requests the agents send to a model (system prompt, tool definitions, real tool outputs) as
/// OpenAI-style JSON, so fine-tuning data can use the same format. Runs only when OFFICEAI_DUMP_DIR is set.
/// </summary>
[Collection(ApiCollection.Name)]
public class TrainingPromptDumpTests(ApiFixture fx)
{
    private static readonly string[] SupportTools = ["chat.post", "chat.read", "deliverable.submit", "knowledge.search", "meeting.list", "org.find_employees",
        "project.list_tasks", "project.read_requirements", "review.submit", "task.delegate", "task.read_context", "task.report_blocked", "task.request_review"];
    private static readonly string[] HrTools = ["chat.post", "chat.read", "deliverable.submit", "knowledge.propose", "knowledge.search", "meeting.draft_minutes",
        "meeting.list", "org.find_employees", "plan.submit", "project.list_tasks", "project.read_requirements", "review.submit", "task.delegate",
        "task.read_context", "task.report_blocked", "task.request_review"];
    private static readonly string[] OperationsTools = ["chat.post", "chat.read", "deliverable.submit", "knowledge.search", "meeting.list", "org.find_employees",
        "plan.submit", "project.list_tasks", "project.read_requirements", "review.submit", "task.read_context", "task.report_blocked", "task.request_review"];

    private const string SupportInstructions =
        "Work in the customer's language (usually Thai). For every customer issue:\n" +
        "1. Search the support knowledge with knowledge_search first (SLA, severity P1–P4, escalation, warranty, reply templates) and follow it.\n" +
        "2. Classify the issue as P1–P4 and put the level at the start of any sub-task title, e.g. \"[P2] ...\".\n" +
        "3. If it is a bug that needs code changes, open a sub-task with task_delegate that carries every detail the customer gave (symptom, steps, time, number of users affected, devices); then end your turn and wait for it.\n" +
        "4. Write the customer reply as your deliverable: polite, no internal tool or role names, no invented links, no promises of dates, prices or free fixes.";

    private readonly ConcurrentDictionary<string, AiRequest> _captured = new();

    /// <summary>Calls the given tools one per turn, then records the request that carries all their results.</summary>
    private async Task<string> CapturingModelAsync(string name, params AiResponse[] calls)
    {
        var modelId = $"dump-{name}-{Guid.NewGuid():N}"[..30];
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AiModels.Add(new AiModel { Provider = ScriptedProvider.ProviderName, ModelId = modelId, DisplayName = modelId });
            await db.SaveChangesAsync();
        }
        var i = 0;
        fx.Scripted.Register(modelId, (req, _) =>
        {
            _captured[name] = req;
            return Task.FromResult(i < calls.Length ? calls[i++] : ScriptedProvider.Text("done"));
        });
        return modelId;
    }

    private async Task<EmployeeDto> AgentAsync(HttpClient ceo, string roleCode, string modelId, string[] tools)
    {
        var role = (await ceo.GetAsync<List<RoleDefinitionDto>>("/api/role-definitions")).First(r => r.Code == roleCode);
        var agent = await ceo.PostAsync<EmployeeDto>("/api/employees",
            new { displayName = $"{roleCode} {Guid.NewGuid():N}"[..20], kind = "AI", departmentId = role.DepartmentId, roleDefinitionId = role.Id });
        var c = await ceo.GetAsync<AgentConfigDto>($"/api/agents/{agent.Id}/config");
        (await ceo.PutAsJsonAsync($"/api/agents/{agent.Id}/config", new
        {
            provider = ScriptedProvider.ProviderName, modelId, effort = "Medium", c.MaxTokensPerCall, c.MaxTurnsPerRun, c.MaxToolCallsPerRun,
            c.MaxTokensPerRun, c.TimeoutSeconds, monthlyBudgetUsd = 1000m, isEnabled = true,
        }, ApiFixture.Json)).EnsureSuccessStatusCode();
        (await ceo.PutAsJsonAsync($"/api/agents/{agent.Id}/tools", new { tools }, ApiFixture.Json)).EnsureSuccessStatusCode();
        return agent;
    }

    private async Task OnlyAsync(params Guid[] keep)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.AgentConfigurations.ExecuteUpdateAsync(s => s.SetProperty(c => c.IsEnabled, c => keep.Contains(c.EmployeeId)));
    }

    private async Task TickUntilIdleAsync()
    {
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<WorkflowEngine>().TickAsync(default);
        for (var i = 0; i < 200; i++)
        {
            await using var scope = fx.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await db.AgentRuns.AnyAsync(r => r.Status == AgentRunStatus.Queued || r.Status == AgentRunStatus.Running)) return;
            await Task.Delay(50);
        }
    }

    /// <summary>
    /// One captured work request per role, with the role's production tools read from OFFICEAI_DUMP_ROLES (a JSON map of role code to tool ids).
    /// Written to OFFICEAI_DUMP_DIR/roles/CODE.json.
    /// </summary>
    [Fact]
    public async Task Dump_role_requests_for_training()
    {
        if (Environment.GetEnvironmentVariable("OFFICEAI_DUMP_DIR") is not { } dir || Environment.GetEnvironmentVariable("OFFICEAI_DUMP_ROLES") is not { } rolesFile) return;
        var roleTools = JsonSerializer.Deserialize<Dictionary<string, string[]>>(await File.ReadAllTextAsync(rolesFile))!;
        Directory.CreateDirectory(Path.Combine(dir, "roles"));
        var ceo = await fx.CeoAsync();
        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"DUMPR-{Guid.NewGuid():N}"[..14], name = "Role capture", status = "Active" });
        var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        foreach (var (code, tools) in roleTools)
        {
            var name = $"role-{code}";
            var worker = await AgentAsync(ceo, code, await CapturingModelAsync(name, ScriptedProvider.Call("task_read_context", new { })), tools);
            var plannerModel = await CapturingModelAsync($"{name}-planner", ScriptedProvider.Call("plan_submit", new
            {
                summary = "one task", tasks = new[] { new { key = "t", title = "capture", requiredSkills = Array.Empty<string>() } },
            }));
            var planner = await AgentAsync(ceo, "OPERATIONS", plannerModel, OperationsTools);
            await OnlyAsync(planner.Id, worker.Id);
            var wf = await ceo.PostAsync<WorkflowDto>($"/api/projects/{project.Id}/objectives", new { objective = "capture", plannerEmployeeId = planner.Id });
            await TickUntilIdleAsync();
            var approval = (await ceo.GetAsync<List<ApprovalDto>>($"/api/approvals?workflowId={wf.Id}&status=Pending")).Single();
            (await ceo.PostAsJsonAsync($"/api/approvals/{approval.Id}/decision", new { approve = true, payloadHash = approval.PayloadHash }, ApiFixture.Json)).EnsureSuccessStatusCode();
            await OnlyAsync(worker.Id);
            await TickUntilIdleAsync();
            await ceo.PostAsync<WorkflowDto>($"/api/workflows/{wf.Id}/cancel", new { });
            Assert.True(_captured.TryGetValue(name, out var request), $"no request captured for {code}");
            await File.WriteAllTextAsync(Path.Combine(dir, "roles", $"{code}.json"), OpenAiProvider.BuildRequest(request).ToJsonString(options));
        }
    }

    [Fact]
    public async Task Dump_agent_requests_for_training()
    {
        if (Environment.GetEnvironmentVariable("OFFICEAI_DUMP_DIR") is not { } dir) return;
        Directory.CreateDirectory(dir);
        var ceo = await fx.CeoAsync();
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.RoleDefinitions.Where(r => r.Code == "SUPPORT").ExecuteUpdateAsync(s => s.SetProperty(r => r.SystemInstructions, SupportInstructions));
        }
        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"DUMP-{Guid.NewGuid():N}"[..14], name = "ระบบจองคิวคลินิกฟันดี", status = "Active" });
        var context = ScriptedProvider.Call("task_read_context", new { });

        // Planning (Operations AI)
        var planner = await AgentAsync(ceo, "OPERATIONS", await CapturingModelAsync("plan", context), OperationsTools);
        await OnlyAsync(planner.Id);
        var workflow = await ceo.PostAsync<WorkflowDto>($"/api/projects/{project.Id}/objectives",
            new { objective = "ลูกค้าแจ้งว่ากดยืนยันการจองแล้วขึ้น Error 500 ให้ฝ่ายซัพพอร์ตรับเรื่องและประสานทีมพัฒนา", plannerEmployeeId = planner.Id });
        await TickUntilIdleAsync();
        await ceo.PostAsync<WorkflowDto>($"/api/workflows/{workflow.Id}/cancel", new { });

        // Work (Customer Support, HR): the task arrives inside a workflow so task_delegate is usable.
        foreach (var (name, role, tools, title, description) in new[]
        {
            ("support", "SUPPORT", SupportTools, "ลูกค้าแจ้งปัญหา: ระบบจองคิว", "ข้อความจากลูกค้า: กดยืนยันการจองแล้วขึ้น Error 500"),
            ("hr", "HR", HrTools, "ตอบคำถามพนักงานเรื่องวันลา", "พนักงานถามว่าทดลองงานอยู่ลาป่วยได้ไหม"),
        })
        {
            var model = await CapturingModelAsync(name, context, ScriptedProvider.Call("org_find_employees", new { query = "backend" }),
                ScriptedProvider.Call("project_read_requirements", new { }));
            var worker = await AgentAsync(ceo, role, model, tools);
            var plannerModel = await CapturingModelAsync($"{name}-planner", ScriptedProvider.Call("plan_submit", new
            {
                summary = "one task", tasks = new[] { new { key = "t", title, description, requiredSkills = new[] { role == "HR" ? "staffing" : "customer-support" } } },
            }));
            var p = await AgentAsync(ceo, "OPERATIONS", plannerModel, OperationsTools);
            await OnlyAsync(p.Id, worker.Id);
            var wf = await ceo.PostAsync<WorkflowDto>($"/api/projects/{project.Id}/objectives", new { objective = title, plannerEmployeeId = p.Id });
            await TickUntilIdleAsync();
            var approval = (await ceo.GetAsync<List<ApprovalDto>>($"/api/approvals?workflowId={wf.Id}&status=Pending")).Single();
            (await ceo.PostAsJsonAsync($"/api/approvals/{approval.Id}/decision", new { approve = true, payloadHash = approval.PayloadHash }, ApiFixture.Json)).EnsureSuccessStatusCode();
            await OnlyAsync(worker.Id);
            await TickUntilIdleAsync();
            await ceo.PostAsync<WorkflowDto>($"/api/workflows/{wf.Id}/cancel", new { });
        }

        // Chat (HR answering a person who mentioned it), with a knowledge entry that matches the question.
        await ceo.PostAsJsonAsync("/api/knowledge", new { kind = "Reference", title = "กฎหมายแรงงาน: การลาป่วย", body = "- ลาป่วยได้เท่าที่ป่วยจริง ได้ค่าจ้างไม่เกิน 30 วันทำงานต่อปี", tags = new[] { "ลาป่วย" } }, ApiFixture.Json);
        var chatModel = await CapturingModelAsync("chat");
        var hr = await AgentAsync(ceo, "HR", chatModel, HrTools);
        var general = (await ceo.GetAsync<List<OfficeAI.Application.Communication.ChannelDto>>("/api/chat/channels")).Single(x => x.Name == OfficeAI.Application.Communication.ChatService.GeneralChannel);
        await ceo.PostAsync<OfficeAI.Application.Communication.MessageDto>($"/api/chat/channels/{general.Id}/messages",
            new { body = $"@{hr.DisplayName} ทดลองงานอยู่ลาป่วยได้ไหมครับ", mentionedEmployeeIds = new[] { hr.Id } });
        for (var i = 0; i < 100 && !_captured.ContainsKey("chat"); i++) await Task.Delay(100);

        var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        foreach (var name in new[] { "plan", "support", "hr", "chat" })
        {
            Assert.True(_captured.TryGetValue(name, out var request), $"no request captured for {name}");
            await File.WriteAllTextAsync(Path.Combine(dir, $"{name}.json"), OpenAiProvider.BuildRequest(request).ToJsonString(options));
        }
    }
}
