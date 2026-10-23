using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Agents;
using OfficeAI.Application.Organization;
using OfficeAI.Application.Reports;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Security;
using OfficeAI.Domain.Work;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

/// <summary>Quality per model (to judge a fine-tuned model against the others), and approved real work exported as training data.</summary>
[Collection(ApiCollection.Name)]
public class ModelQualityTests(ApiFixture fx)
{
    private sealed record Seeded(string ModelA, string ModelB, Guid DoneRun, Guid RejectedRun, Guid OpenTaskRun);

    /// <summary>
    /// Model A: one run whose deliverable was accepted and whose task a person marked Done, one rejected, one failed.
    /// Model B: one run that took over from A after a fallback switch, task still open.
    /// </summary>
    private async Task<Seeded> SeedAsync()
    {
        var ceo = await fx.CeoAsync();
        var role = (await ceo.GetAsync<List<RoleDefinitionDto>>("/api/role-definitions")).First(r => r.Code == "BACKEND");
        var agent = await ceo.PostAsync<EmployeeDto>("/api/employees", new { displayName = $"Quality {Guid.NewGuid():N}"[..20], kind = "AI", departmentId = role.DepartmentId, roleDefinitionId = role.Id });
        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"MQ-{Guid.NewGuid():N}"[..14], name = "Model quality", status = "Active" });
        var (a, b) = ($"mq-a-{Guid.NewGuid():N}"[..20], $"mq-b-{Guid.NewGuid():N}"[..20]);
        var tasks = new List<Guid>();
        for (var i = 0; i < 4; i++) tasks.Add((await ceo.PostAsync<TaskDto>($"/api/projects/{project.Id}/tasks", new { title = $"Task {i}" })).Id);

        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AiModels.Add(new AiModel { Provider = ScriptedProvider.ProviderName, ModelId = a, DisplayName = "Model A" });
        db.AiModels.Add(new AiModel { Provider = ScriptedProvider.ProviderName, ModelId = b, DisplayName = "Model B" });
        var conversation = ConversationCodec.Encode([
            new AiMessage(AiRole.User, [new AiText("You have been assigned a task.")]),
            new AiMessage(AiRole.Assistant, [new AiToolCall("c1", "task_read_context", JsonSerializer.SerializeToElement(new { }))]),
            new AiMessage(AiRole.User, [new AiToolResult("c1", "{\"title\":\"Task 0\"}", false)]),
            new AiMessage(AiRole.Assistant, [new AiToolCall("c2", "task_request_review", JsonSerializer.SerializeToElement(new { }))]),
            new AiMessage(AiRole.User, [new AiToolResult("c2", "{\"ok\":true}", false)]),
        ]);
        AgentRun Run(Guid task, string model, AgentRunStatus status) => new()
        {
            TaskId = task, ProjectId = project.Id, EmployeeId = agent.Id, Provider = ScriptedProvider.ProviderName, ModelId = model, Status = status,
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-30), FinishedAt = DateTimeOffset.UtcNow, Turns = 2, CostUsd = 0.01m, ConversationJson = conversation,
        };
        var done = Run(tasks[0], a, AgentRunStatus.Succeeded);
        var rejected = Run(tasks[1], a, AgentRunStatus.Succeeded);
        var failed = Run(tasks[2], a, AgentRunStatus.Failed);
        var tookOver = Run(tasks[3], b, AgentRunStatus.Succeeded);
        db.AgentRuns.AddRange(done, rejected, failed, tookOver);
        foreach (var r in new[] { done, rejected, tookOver })
            db.AgentRunSteps.Add(new AgentRunStep { RunId = r.Id, Sequence = 1, Kind = RunStepKind.Instructions, Content = "You are a test employee.", CreatedAt = DateTimeOffset.UtcNow });
        db.AgentRunSteps.Add(new AgentRunStep { RunId = tookOver.Id, Sequence = 2, Kind = RunStepKind.Notice, CreatedAt = DateTimeOffset.UtcNow,
            Content = $"Switched from scripted/{a} to the fallback model scripted/{b}: Gemini rate limit reached" });
        db.Deliverables.Add(new Deliverable { TaskId = tasks[0], EmployeeId = agent.Id, RunId = done.Id, Title = "Spec", Content = "spec", Status = DeliverableStatus.Accepted });
        db.Deliverables.Add(new Deliverable { TaskId = tasks[1], EmployeeId = agent.Id, RunId = rejected.Id, Title = "Spec", Content = "spec", Status = DeliverableStatus.Rejected });
        db.Deliverables.Add(new Deliverable { TaskId = tasks[3], EmployeeId = agent.Id, RunId = tookOver.Id, Title = "Spec", Content = "spec" });
        await db.SaveChangesAsync();
        await db.Tasks.Where(t => t.Id == tasks[0] || t.Id == tasks[1]).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, WorkTaskStatus.Done));
        return new Seeded(a, b, done.Id, rejected.Id, tookOver.Id);
    }

    [Fact]
    public async Task Each_model_gets_its_success_review_and_fallback_figures()
    {
        var s = await SeedAsync();
        var report = await (await fx.CeoAsync()).GetAsync<ModelPerformanceReportDto>("/api/reports/models");
        var a = Assert.Single(report.Rows, r => r.ModelId == s.ModelA);
        Assert.Equal("Model A", a.DisplayName);
        Assert.Equal((3, 2, 1), (a.Runs, a.Succeeded, a.Failed));
        Assert.Equal(66.7m, a.SuccessRate);
        Assert.Equal((1, 1, 50m), (a.DeliverablesAccepted, a.DeliverablesRejected, a.AcceptanceRate!.Value));
        Assert.Equal(2, a.TasksDone);
        Assert.Equal((1, 0), (a.SwitchedAway, a.TookOver));
        Assert.Equal(30, a.AvgSeconds);
        var b = Assert.Single(report.Rows, r => r.ModelId == s.ModelB);
        Assert.Equal((1, 0, 1), (b.Runs, b.SwitchedAway, b.TookOver));
        Assert.Null(b.AcceptanceRate); // not reviewed yet
    }

    [Fact]
    public async Task Only_approved_real_work_is_exported_as_training_data_by_people_who_manage_AI()
    {
        var s = await SeedAsync();
        var ceo = await fx.CeoAsync();
        var res = await ceo.GetAsync("/api/training/export");
        res.EnsureSuccessStatusCode();
        Assert.Contains(".jsonl", res.Content.Headers.ContentDisposition?.FileName);
        var lines = (await res.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonDocument.Parse(l).RootElement).ToList();

        // Our task Done + accepted deliverable is in; the rejected one, the failed one and the open task are not. Other tests may add their own runs.
        var ours = lines.Where(l => l.GetProperty("messages")[0].GetProperty("content").GetString() == "You are a test employee.").ToList();
        var sample = Assert.Single(ours);
        Assert.Equal("real-work-backend", sample.GetProperty("tag").GetString());
        var messages = sample.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(["system", "user", "assistant", "tool", "assistant"], messages.Select(m => m.GetProperty("role").GetString()));
        Assert.Equal("task_request_review", messages[^1].GetProperty("tool_calls")[0].GetProperty("function").GetProperty("name").GetString()); // trailing tool result trimmed
        Assert.Contains(sample.GetProperty("tools").EnumerateArray(), t => t.GetProperty("function").GetProperty("name").GetString() == "task_read_context");

        var summary = await ceo.GetAsync<OfficeAI.Infrastructure.Ai.TrainingExportSummaryDto>("/api/training/summary");
        Assert.True(summary.WorkRuns >= 1);
        var audit = await ceo.GetAsync<List<OfficeAI.Infrastructure.Security.AuditLogDto>>("/api/audit?take=20");
        Assert.Contains(audit, x => x.Action == "TrainingDataExported");

        var manager = await fx.UserWithRolesAsync(SystemRoles.Manager, SystemRoles.Member);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/training/export")).StatusCode);
    }
}
