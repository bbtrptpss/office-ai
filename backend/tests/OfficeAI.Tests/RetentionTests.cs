using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Communication;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Code;
using OfficeAI.Domain.Communication;
using OfficeAI.Domain.Integrations;
using OfficeAI.Domain.Organization;
using OfficeAI.Domain.Security;
using OfficeAI.Infrastructure.Persistence;
using OfficeAI.Infrastructure.Security;

namespace OfficeAI.Tests;

// Changes a company-wide policy, so it runs on its own and resets the policy when done.
[Collection(ApiCollection.Name)]
public class RetentionTests(ApiFixture fx)
{
    private static readonly object KeepAll = new { runTranscriptDays = (int?)null, testOutputDays = (int?)null, notificationDays = (int?)null, integrationDeliveryDays = (int?)null, chatMessageDays = (int?)null };

    [Fact]
    public async Task Retention_removes_only_old_operational_data_and_is_audited()
    {
        var ceo = await fx.CeoAsync();
        await ceo.PutAsync<RetentionDto>("/api/security/retention", KeepAll);
        // Default: keep everything, so applying changes nothing.
        Assert.Equal(0, (await ceo.PostAsync<RetentionCounts>("/api/security/retention/apply", new { })).Total);

        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"RT-{Guid.NewGuid():N}"[..14], name = "Retention", status = "Active" });
        var task = await ceo.PostAsync<TaskDto>($"/api/projects/{project.Id}/tasks", new { title = "Old work" });
        var channel = await ceo.PostAsync<ChannelDto>("/api/chat/channels/open", new { projectId = project.Id });
        var oldMsg = await ceo.PostAsync<MessageDto>($"/api/chat/channels/{channel.Id}/messages", new { body = "old secret" });
        var newMsg = await ceo.PostAsync<MessageDto>($"/api/chat/channels/{channel.Id}/messages", new { body = "fresh" });
        var old = DateTimeOffset.UtcNow.AddDays(-100);
        Guid oldRun, newRun, oldTest, oldCost;
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var agent = await db.Employees.FirstAsync(e => e.Kind == EmployeeKind.AI);
            AgentRun Run(DateTimeOffset finished) => new()
            {
                TaskId = task.Id, ProjectId = project.Id, EmployeeId = agent.Id, Provider = "test", ModelId = "test", Status = AgentRunStatus.Succeeded,
                StartedAt = finished, FinishedAt = finished, ConversationJson = "[\"secret\"]",
            };
            var r1 = Run(old);
            var r2 = Run(DateTimeOffset.UtcNow);
            db.AgentRuns.AddRange(r1, r2);
            await db.SaveChangesAsync();
            (oldRun, newRun) = (r1.Id, r2.Id);
            db.AgentRunSteps.AddRange(
                new AgentRunStep { RunId = oldRun, Sequence = 1, Kind = RunStepKind.AssistantText, Content = "old step", CreatedAt = old },
                new AgentRunStep { RunId = newRun, Sequence = 1, Kind = RunStepKind.AssistantText, Content = "new step", CreatedAt = DateTimeOffset.UtcNow });
            db.ToolExecutions.Add(new ToolExecution
            {
                RunId = oldRun, ToolId = "file.write", ToolVersion = "1", ToolCallId = "c1", IdempotencyKey = $"{oldRun}:c1",
                InputJson = "{\"content\":\"secret\"}", Output = "secret", Status = ToolExecutionStatus.Succeeded, StartedAt = old,
            });
            var repo = new CodeRepository { ProjectId = project.Id, Name = "r", SandboxImage = "img", TestCommand = "test" };
            db.CodeRepositories.Add(repo);
            var test = new TestRun { RepositoryId = repo.Id, Branch = "main", Image = "img", Command = "test", Status = TestRunStatus.Passed, Output = "log", StartedAt = old };
            db.TestRuns.Add(test);
            var cost = new CostRecord { Kind = CostKind.Actual, Category = "ai", ProjectId = project.Id, RunId = oldRun, AmountUsd = 1m, OccurredAt = old };
            db.CostRecords.Add(cost);
            var hook = new IntegrationConfiguration { Name = $"hook {Guid.NewGuid():N}", SecretEnvVar = "X" };
            db.Integrations.Add(hook);
            await db.SaveChangesAsync();
            db.IntegrationDeliveries.Add(new IntegrationDelivery { IntegrationId = hook.Id, Event = "e", Title = "old", Status = DeliveryStatus.Sent, CreatedAt = old });
            var ceoUser = (await ceo.GetAsync<OfficeAI.Api.Endpoints.MeDto>("/api/auth/me")).UserId;
            db.Notifications.Add(new Notification { UserId = ceoUser, Kind = "test", Title = "old", CreatedAt = old });
            await db.SaveChangesAsync();
            await db.ChatMessages.Where(m => m.Id == oldMsg.Id).ExecuteUpdateAsync(s => s.SetProperty(m => m.CreatedAt, old));
            (oldTest, oldCost) = (test.Id, cost.Id);
        }

        var policy = await ceo.GetAsync<RetentionDto>("/api/security/retention");
        await ceo.PutAsync<RetentionDto>("/api/security/retention", new
        {
            runTranscriptDays = 30, testOutputDays = 30, notificationDays = 30, integrationDeliveryDays = 30, chatMessageDays = 30, version = policy.Version,
        });
        try
        {
            var preview = await ceo.GetAsync<RetentionCounts>("/api/security/retention/preview");
            Assert.True(preview.RunSteps >= 1 && preview.ToolOutputs >= 1 && preview.TestOutputs >= 1 && preview.ChatMessages >= 1);
            var applied = await ceo.PostAsync<RetentionCounts>("/api/security/retention/apply", new { });
            Assert.Equal(preview, applied);
            Assert.Equal(0, (await ceo.GetAsync<RetentionCounts>("/api/security/retention/preview")).Total);

            await using var scope = fx.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.AgentRunSteps.AnyAsync(s => s.RunId == oldRun));
            Assert.True(await db.AgentRunSteps.AnyAsync(s => s.RunId == newRun));
            Assert.Null((await db.AgentRuns.AsNoTracking().FirstAsync(r => r.Id == oldRun)).ConversationJson);
            Assert.NotNull((await db.AgentRuns.AsNoTracking().FirstAsync(r => r.Id == newRun)).ConversationJson);
            var tool = await db.ToolExecutions.AsNoTracking().FirstAsync(t => t.RunId == oldRun);
            Assert.Null(tool.Output);
            Assert.DoesNotContain("secret", tool.InputJson);
            var testRun = await db.TestRuns.AsNoTracking().FirstAsync(t => t.Id == oldTest);
            Assert.Null(testRun.Output);
            Assert.Equal(TestRunStatus.Passed, testRun.Status); // the result stays
            Assert.True(await db.CostRecords.AnyAsync(c => c.Id == oldCost)); // money is never removed

            var messages = await ceo.GetAsync<List<MessageDto>>($"/api/chat/channels/{channel.Id}/messages");
            Assert.True(messages.Single(m => m.Id == oldMsg.Id).IsDeleted);
            Assert.Equal("fresh", messages.Single(m => m.Id == newMsg.Id).Body);
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == RetentionService.AuditAction));
        }
        finally
        {
            await ceo.PutAsync<RetentionDto>("/api/security/retention", KeepAll);
        }
    }

    [Fact]
    public async Task Retention_periods_are_validated_and_need_security_manage()
    {
        var ceo = await fx.CeoAsync();
        var res = await ceo.PutAsJsonAsync("/api/security/retention", new { notificationDays = 5 });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        var manager = await fx.UserWithRolesAsync(SystemRoles.Manager);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/security/retention")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync("/api/security/retention/apply", new { })).StatusCode);
    }
}
