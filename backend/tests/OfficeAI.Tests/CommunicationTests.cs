using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Agents;
using OfficeAI.Application.Communication;
using OfficeAI.Application.Knowledge;
using OfficeAI.Application.Organization;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Communication;
using OfficeAI.Domain.Knowledge;
using OfficeAI.Domain.Security;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class CommunicationTests(ApiFixture fx)
{
    private async Task<(HttpClient Ceo, Guid TaskId, Guid ProjectId, Guid AgentId)> AgentTaskAsync(string roleCode, AiResponse[] script, Guid? projectId = null)
    {
        var ceo = await fx.CeoAsync();
        var role = (await ceo.GetAsync<List<RoleDefinitionDto>>("/api/role-definitions")).First(r => r.Code == roleCode);
        var agent = await ceo.PostAsync<EmployeeDto>("/api/employees",
            new { displayName = $"Cm {Guid.NewGuid():N}"[..20], kind = "AI", departmentId = role.DepartmentId, roleDefinitionId = role.Id });
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
        var pid = projectId ?? await NewProjectAsync(ceo);
        var task = await ceo.PostAsync<TaskDto>($"/api/projects/{pid}/tasks", new { title = "Communication work" });
        await ceo.PostAsync<TaskDto>($"/api/tasks/{task.Id}/assign", new { employeeId = agent.Id });
        return (ceo, task.Id, pid, agent.Id);
    }

    private static async Task<Guid> NewProjectAsync(HttpClient ceo) =>
        (await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"CM-{Guid.NewGuid():N}"[..14], name = "Comms project", status = "Active" })).Id;

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

    private static async Task<ChannelDto> GeneralAsync(HttpClient c) =>
        (await c.GetAsync<List<ChannelDto>>("/api/chat/channels")).Single(x => x.Kind == ChannelKind.Company && x.Name == ChatService.GeneralChannel);

    [Fact]
    public async Task People_chat_in_General_with_unread_counts_and_author_only_deletion()
    {
        var alice = await fx.UserWithRolesAsync(SystemRoles.Member);
        var bob = await fx.UserWithRolesAsync(SystemRoles.Member);
        var general = await GeneralAsync(alice);
        await bob.PostAsync($"/api/chat/channels/{general.Id}/read", null);
        var before = (await GeneralAsync(bob)).Unread;

        var msg = await alice.PostAsync<MessageDto>($"/api/chat/channels/{general.Id}/messages", new { body = "  สวัสดีทุกคน  " });
        Assert.Equal("สวัสดีทุกคน", msg.Body);
        Assert.False(msg.IsAgent);
        Assert.True(msg.CanDelete);

        Assert.Equal(before + 1, (await GeneralAsync(bob)).Unread);
        Assert.Equal(0, (await GeneralAsync(alice)).Unread); // posting marks my own read
        var seen = Assert.Single(await bob.GetAsync<List<MessageDto>>($"/api/chat/channels/{general.Id}/messages"), m => m.Id == msg.Id);
        Assert.False(seen.CanDelete);
        Assert.Equal(HttpStatusCode.NoContent, (await bob.PostAsync($"/api/chat/channels/{general.Id}/read", null)).StatusCode);
        Assert.Equal(0, (await GeneralAsync(bob)).Unread);

        Assert.Equal(HttpStatusCode.Forbidden, (await bob.DeleteAsync($"/api/chat/messages/{msg.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/chat/messages/{msg.Id}")).StatusCode);
        var gone = (await bob.GetAsync<List<MessageDto>>($"/api/chat/channels/{general.Id}/messages")).Single(m => m.Id == msg.Id);
        Assert.True(gone.IsDeleted);
        Assert.Null(gone.Body);

        // Validation and permissions.
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync($"/api/chat/channels/{general.Id}/messages", new { body = "   " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync($"/api/chat/channels/{general.Id}/messages", new { body = new string('x', 4001) })).StatusCode);
        var auditor = await fx.UserWithRolesAsync(SystemRoles.Auditor);
        Assert.Equal(HttpStatusCode.Forbidden, (await auditor.PostAsJsonAsync($"/api/chat/channels/{general.Id}/messages", new { body = "hi" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.PostAsJsonAsync("/api/chat/channels", new { name = "random" })).StatusCode);
    }

    [Fact]
    public async Task Moderator_can_remove_any_message_and_General_cannot_be_archived()
    {
        var ceo = await fx.CeoAsync();
        var member = await fx.UserWithRolesAsync(SystemRoles.Member);
        var general = await GeneralAsync(ceo);
        var msg = await member.PostAsync<MessageDto>($"/api/chat/channels/{general.Id}/messages", new { body = "off topic" });
        Assert.True((await ceo.GetAsync<List<MessageDto>>($"/api/chat/channels/{general.Id}/messages")).Single(m => m.Id == msg.Id).CanDelete);
        Assert.Equal(HttpStatusCode.NoContent, (await ceo.DeleteAsync($"/api/chat/messages/{msg.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PostAsync($"/api/chat/channels/{general.Id}/archive", null)).StatusCode);

        var custom = await ceo.PostAsync<ChannelDto>("/api/chat/channels", new { name = $"team-{Guid.NewGuid():N}"[..12] });
        await ceo.PostAsync<ChannelDto>($"/api/chat/channels/{custom.Id}/archive", new { });
        Assert.Equal(HttpStatusCode.BadRequest, (await member.PostAsJsonAsync($"/api/chat/channels/{custom.Id}/messages", new { body = "hello?" })).StatusCode);
        Assert.DoesNotContain(await member.GetAsync<List<ChannelDto>>("/api/chat/channels"), c => c.Id == custom.Id);
    }

    [Fact]
    public async Task Opening_a_project_channel_concurrently_creates_exactly_one()
    {
        var ceo = await fx.CeoAsync();
        var project = await NewProjectAsync(ceo);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => ceo.PostAsJsonAsync("/api/chat/channels/open", new { projectId = project })));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var ids = (await Task.WhenAll(results.Select(r => r.ReadAsync<ChannelDto>()))).Select(c => c.Id).Distinct();
        Assert.Single(ids);
        Assert.Single(await ceo.GetAsync<List<ChannelDto>>("/api/chat/channels"), c => c.ProjectId == project);
    }

    [Fact]
    public async Task Agents_post_and_read_only_their_own_projects_channel()
    {
        var marker = $"status{Guid.NewGuid():N}"[..14];
        var (ceo, task, project, agentId) = await AgentTaskAsync("BACKEND", [ScriptedProvider.Call("chat_post", new { text = $"API done {marker}" })]);
        var run = await RunAsync(ceo, task);
        Assert.Equal(ToolExecutionStatus.Succeeded, Assert.Single(run.ToolExecutions).Status);

        var channel = await ceo.PostAsync<ChannelDto>("/api/chat/channels/open", new { projectId = project });
        var posted = (await ceo.GetAsync<List<MessageDto>>($"/api/chat/channels/{channel.Id}/messages")).Single(m => m.Body!.Contains(marker));
        Assert.True(posted.IsAgent);
        Assert.Equal(agentId, posted.AuthorEmployeeId);

        // An agent working on another project never sees this message.
        var (_, otherTask, _, _) = await AgentTaskAsync("BACKEND", [ScriptedProvider.Call("chat_read", new { })]);
        var exec = Assert.Single((await RunAsync(ceo, otherTask)).ToolExecutions);
        Assert.DoesNotContain(marker, exec.Output);

        // A teammate on the same project does.
        var (_, sameTask, _, _) = await AgentTaskAsync("FRONTEND", [ScriptedProvider.Call("chat_read", new { })], project);
        Assert.Contains(marker, Assert.Single((await RunAsync(ceo, sameTask)).ToolExecutions).Output);
    }

    [Fact]
    public async Task Announcements_are_drafts_until_a_person_publishes_and_respect_the_audience()
    {
        var ceo = await fx.CeoAsync();
        var member = await fx.UserWithRolesAsync(SystemRoles.Member);
        var title = $"Holiday {Guid.NewGuid():N}"[..20];
        var draft = await ceo.PostAsync<AnnouncementDto>("/api/announcements", new { title, body = "Office closed on Friday.", priority = "Important" });
        Assert.Equal(AnnouncementStatus.Draft, draft.Status);
        Assert.DoesNotContain(await member.GetAsync<List<AnnouncementDto>>("/api/announcements"), a => a.Id == draft.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/announcements/{draft.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsync($"/api/announcements/{draft.Id}/publish", null)).StatusCode);

        var published = await ceo.PostAsync<AnnouncementDto>($"/api/announcements/{draft.Id}/publish", new { });
        Assert.NotNull(published.PublishedAt);
        var seen = Assert.Single(await member.GetAsync<List<AnnouncementDto>>("/api/announcements"), a => a.Id == draft.Id);
        Assert.False(seen.IsRead);
        Assert.Equal(HttpStatusCode.NoContent, (await member.PostAsync($"/api/announcements/{draft.Id}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await member.PostAsync($"/api/announcements/{draft.Id}/read", null)).StatusCode); // idempotent
        Assert.True((await member.GetAsync<AnnouncementDto>($"/api/announcements/{draft.Id}")).IsRead);
        Assert.Equal(1, (await ceo.GetAsync<AnnouncementDto>($"/api/announcements/{draft.Id}")).ReadCount);

        // Department announcements reach only that department (this member has no employee profile, so only company-wide ones).
        var dept = (await ceo.GetAsync<List<DepartmentDto>>("/api/departments")).First();
        var scoped = await ceo.PostAsync<AnnouncementDto>("/api/announcements", new { title = "Dept only", body = "x", departmentId = dept.Id, publish = true });
        Assert.Equal(AnnouncementStatus.Published, scoped.Status);
        Assert.DoesNotContain(await member.GetAsync<List<AnnouncementDto>>("/api/announcements"), a => a.Id == scoped.Id);

        await ceo.PostAsync<AnnouncementDto>($"/api/announcements/{draft.Id}/archive", new { });
        Assert.DoesNotContain(await member.GetAsync<List<AnnouncementDto>>("/api/announcements"), a => a.Id == draft.Id);
    }

    [Fact]
    public async Task An_agent_drafted_announcement_needs_a_person_to_publish_it()
    {
        var title = $"Release {Guid.NewGuid():N}"[..20];
        var (ceo, task, _, _) = await AgentTaskAsync("COO", [ScriptedProvider.Call("announcement_draft", new { title, body = "v1.0 is live." })]);
        var run = await RunAsync(ceo, task);
        Assert.Equal(ToolExecutionStatus.Succeeded, Assert.Single(run.ToolExecutions).Status);
        var draft = (await ceo.GetAsync<List<AnnouncementDto>>("/api/announcements?status=Draft")).Single(a => a.Title == title);
        Assert.NotNull(draft.AuthorName);
        Assert.Equal(AnnouncementStatus.Published, (await ceo.PostAsync<AnnouncementDto>($"/api/announcements/{draft.Id}/publish", new { })).Status);
    }

    [Fact]
    public async Task Meeting_minutes_drafted_by_an_agent_are_filed_as_a_project_document_once_approved()
    {
        var ceo = await fx.CeoAsync();
        var project = await NewProjectAsync(ceo);
        var employees = await ceo.GetAsync<List<EmployeeDto>>("/api/employees");
        var owner = employees.First();
        var meeting = await ceo.PostAsync<MeetingDto>("/api/meetings", new
        {
            title = "Sprint planning", projectId = project, scheduledAt = DateTimeOffset.UtcNow.AddDays(1), durationMinutes = 45,
            participantIds = employees.Take(3).Select(e => e.Id),
            agenda = new[] { new { title = "Review backlog", ownerEmployeeId = owner.Id }, new { title = "Commit sprint goal", ownerEmployeeId = owner.Id } },
        });
        Assert.Equal(2, meeting.Agenda.Count);
        Assert.Equal(3, meeting.Participants.Count);
        Assert.Equal(MinutesStatus.None, meeting.MinutesStatus);

        var (_, task, _, _) = await AgentTaskAsync("PM",
            [ScriptedProvider.Call("meeting_draft_minutes", new { meetingId = meeting.Id, minutes = "We agreed on the login flow.", actionItems = new[] { "Write login API" } })], project);
        var exec = Assert.Single((await RunAsync(ceo, task)).ToolExecutions);
        Assert.Equal(ToolExecutionStatus.Succeeded, exec.Status);

        var drafted = await ceo.GetAsync<MeetingDto>($"/api/meetings/{meeting.Id}");
        Assert.Equal(MinutesStatus.Draft, drafted.MinutesStatus);
        Assert.NotNull(drafted.MinutesAuthorName);
        var item = Assert.Single(drafted.ActionItems);

        await ceo.PostAsync<MeetingDto>($"/api/meetings/{meeting.Id}/attendance", new { employeeId = drafted.Participants[0].EmployeeId, attendance = "Attended" });
        var approved = await ceo.PostAsync<MeetingDto>($"/api/meetings/{meeting.Id}/minutes/approve", new { });
        Assert.Equal(MinutesStatus.Approved, approved.MinutesStatus);
        Assert.Equal(MeetingStatus.Held, approved.Status);
        var doc = await ceo.GetAsync<DocumentDto>($"/api/documents/{approved.MinutesDocumentId}");
        Assert.Equal(DocumentKind.MeetingMinutes, doc.Kind);
        Assert.Equal(DocumentStatus.Final, doc.Status);
        Assert.Contains("We agreed on the login flow.", doc.Content);
        Assert.Contains("Write login API", doc.Content);

        // The agent may not overwrite approved minutes.
        var (_, task2, _, _) = await AgentTaskAsync("PM",
            [ScriptedProvider.Call("meeting_draft_minutes", new { meetingId = meeting.Id, minutes = "Rewritten." })], project);
        Assert.Equal(ToolExecutionStatus.Failed, Assert.Single((await RunAsync(ceo, task2)).ToolExecutions).Status);
        Assert.Equal("We agreed on the login flow.", (await ceo.GetAsync<MeetingDto>($"/api/meetings/{meeting.Id}")).Minutes);

        // Action item → task, exactly once even when clicked twice at the same time.
        var clicks = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ceo.PostAsync($"/api/meetings/{meeting.Id}/action-items/{item.Id}/task", null)));
        Assert.Single(clicks, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(clicks.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        var tasks = await ceo.GetAsync<List<TaskDto>>($"/api/projects/{project}/tasks");
        Assert.Single(tasks, t => t.Title == "Write login API");
    }

    [Fact]
    public async Task Agents_cannot_draft_minutes_for_another_projects_meeting()
    {
        var ceo = await fx.CeoAsync();
        var otherProject = await NewProjectAsync(ceo);
        var meeting = await ceo.PostAsync<MeetingDto>("/api/meetings", new { title = "Other team sync", projectId = otherProject, scheduledAt = DateTimeOffset.UtcNow });
        var (_, task, _, _) = await AgentTaskAsync("PM", [ScriptedProvider.Call("meeting_draft_minutes", new { meetingId = meeting.Id, minutes = "Injected." })]);
        var exec = Assert.Single((await RunAsync(ceo, task)).ToolExecutions);
        Assert.Equal(ToolExecutionStatus.Failed, exec.Status);
        Assert.Null((await ceo.GetAsync<MeetingDto>($"/api/meetings/{meeting.Id}")).Minutes);
    }

    [Fact]
    public async Task Meeting_validation_and_permissions()
    {
        var ceo = await fx.CeoAsync();
        var member = await fx.UserWithRolesAsync(SystemRoles.Member);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/meetings", new { title = "x", scheduledAt = DateTimeOffset.UtcNow })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PostAsJsonAsync("/api/meetings", new { title = "x", scheduledAt = DateTimeOffset.UtcNow, durationMinutes = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ceo.PostAsJsonAsync("/api/meetings", new { title = "x", scheduledAt = DateTimeOffset.UtcNow, participantIds = new[] { Guid.NewGuid() } })).StatusCode);

        var m = await ceo.PostAsync<MeetingDto>("/api/meetings", new { title = "All hands", scheduledAt = DateTimeOffset.UtcNow });
        // Members write draft minutes but cannot approve them.
        var withMinutes = await member.PutAsync<MeetingDto>($"/api/meetings/{m.Id}/minutes", new { minutes = "Notes from the floor." });
        Assert.Equal(MinutesStatus.Draft, withMinutes.MinutesStatus);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsync($"/api/meetings/{m.Id}/minutes/approve", null)).StatusCode);
        // A company meeting has no project, so its approved minutes are not filed as a document and items cannot become tasks.
        var approved = await ceo.PostAsync<MeetingDto>($"/api/meetings/{m.Id}/minutes/approve", new { });
        Assert.Null(approved.MinutesDocumentId);
        var item = (await member.PostAsync<MeetingDto>($"/api/meetings/{m.Id}/action-items", new { title = "Book venue" })).ActionItems.Single();
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PostAsync($"/api/meetings/{m.Id}/action-items/{item.Id}/task", null)).StatusCode);
        var done = await member.PutAsync<MeetingDto>($"/api/meetings/{m.Id}/action-items/{item.Id}", new { isDone = true });
        Assert.True(done.ActionItems.Single().IsDone);
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PostAsJsonAsync($"/api/meetings/{m.Id}/status", new { status = "Cancelled" })).StatusCode);
    }
}
