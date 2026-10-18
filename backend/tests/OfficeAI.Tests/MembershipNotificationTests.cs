using System.Net;
using System.Net.Http.Json;
using OfficeAI.Api.Endpoints;
using OfficeAI.Application.Communication;
using OfficeAI.Application.Organization;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Communication;
using OfficeAI.Domain.Security;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class MembershipNotificationTests(ApiFixture fx)
{
    /// <summary>A person with a login, a Member role and an employee profile.</summary>
    private async Task<(HttpClient Client, EmployeeDto Employee)> PersonAsync(HttpClient ceo, string name)
    {
        var client = await fx.UserWithRolesAsync(SystemRoles.Member);
        var me = await client.GetAsync<MeDto>("/api/auth/me");
        var role = (await ceo.GetAsync<List<RoleDefinitionDto>>("/api/role-definitions")).First(r => r.Code == "BACKEND");
        var employee = await ceo.PostAsync<EmployeeDto>("/api/employees",
            new { displayName = $"{name} {Guid.NewGuid():N}"[..20], kind = "Human", departmentId = role.DepartmentId, roleDefinitionId = role.Id, userId = me.UserId });
        return (client, employee);
    }

    private static async Task<Guid> NewProjectAsync(HttpClient ceo) =>
        (await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"MB-{Guid.NewGuid():N}"[..14], name = "Members project", status = "Active" })).Id;

    [Fact]
    public async Task Project_channels_are_visible_only_to_project_members()
    {
        var ceo = await fx.CeoAsync();
        var (alice, aliceEmp) = await PersonAsync(ceo, "Alice");
        var (bob, bobEmp) = await PersonAsync(ceo, "Bob");
        var project = await NewProjectAsync(ceo);
        var channel = await ceo.PostAsync<ChannelDto>("/api/chat/channels/open", new { projectId = project });
        await ceo.PostAsync<MessageDto>($"/api/chat/channels/{channel.Id}/messages", new { body = "project secret" });

        // Nobody is a member yet: the channel does not exist for them.
        Assert.DoesNotContain(await alice.GetAsync<List<ChannelDto>>("/api/chat/channels"), c => c.Id == channel.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/chat/channels/{channel.Id}/messages")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.PostAsJsonAsync($"/api/chat/channels/{channel.Id}/messages", new { body = "hi" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.PostAsJsonAsync("/api/chat/channels/open", new { projectId = project })).StatusCode);

        // Being assigned a task makes Alice a member.
        var task = await ceo.PostAsync<TaskDto>($"/api/projects/{project}/tasks", new { title = "Member task" });
        await ceo.PostAsync<TaskDto>($"/api/tasks/{task.Id}/assign", new { employeeId = aliceEmp.Id });
        Assert.Contains(await alice.GetAsync<List<ChannelDto>>("/api/chat/channels"), c => c.Id == channel.Id);
        Assert.Contains(await alice.GetAsync<List<MessageDto>>($"/api/chat/channels/{channel.Id}/messages"), m => m.Body == "project secret");

        // Adding Bob by hand works too, and removing him takes it away again.
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.PostAsync($"/api/projects/{project}/members/{bobEmp.Id}", null)).StatusCode);
        var members = await ceo.PostAsync<List<ChannelMemberDto>>($"/api/projects/{project}/members/{bobEmp.Id}", new { });
        Assert.Contains(members, m => m.EmployeeId == bobEmp.Id && m.IsExplicit);
        Assert.Contains(members, m => m.EmployeeId == aliceEmp.Id && !m.IsExplicit);
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/chat/channels/{channel.Id}/messages")).StatusCode);
        (await ceo.DeleteAsync($"/api/projects/{project}/members/{bobEmp.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/chat/channels/{channel.Id}/messages")).StatusCode);

        // Company channels stay open to everyone.
        Assert.Contains(await bob.GetAsync<List<ChannelDto>>("/api/chat/channels"), c => c.Name == ChatService.GeneralChannel);
    }

    [Fact]
    public async Task Mentions_notify_only_the_mentioned_people_and_only_members_can_be_mentioned()
    {
        var ceo = await fx.CeoAsync();
        var (alice, aliceEmp) = await PersonAsync(ceo, "Alice");
        var (bob, bobEmp) = await PersonAsync(ceo, "Bob");
        var (carol, _) = await PersonAsync(ceo, "Carol");
        var general = (await alice.GetAsync<List<ChannelDto>>("/api/chat/channels")).Single(c => c.Name == ChatService.GeneralChannel);
        var bobBefore = (await bob.GetAsync<NotificationListDto>("/api/notifications")).Unread;
        var carolBefore = (await carol.GetAsync<NotificationListDto>("/api/notifications")).Unread;

        var msg = await alice.PostAsync<MessageDto>($"/api/chat/channels/{general.Id}/messages",
            new { body = $"@{bobEmp.DisplayName} please review", mentionedEmployeeIds = new[] { bobEmp.Id } });
        Assert.Equal([bobEmp.Id], msg.Mentions);

        var bobs = await bob.GetAsync<NotificationListDto>("/api/notifications");
        Assert.Equal(bobBefore + 1, bobs.Unread);
        var n = bobs.Items.First();
        Assert.Equal(NotificationKinds.Mention, n.Kind);
        Assert.Equal($"/chat?channel={general.Id}", n.Link);
        Assert.Equal(carolBefore, (await carol.GetAsync<NotificationListDto>("/api/notifications")).Unread);

        // Someone else cannot clear Bob's notification; Bob can.
        (await carol.PostAsync($"/api/notifications/read?id={n.Id}", null)).EnsureSuccessStatusCode();
        Assert.False((await bob.GetAsync<NotificationListDto>("/api/notifications")).Items.First(x => x.Id == n.Id).IsRead);
        (await bob.PostAsync($"/api/notifications/read?id={n.Id}", null)).EnsureSuccessStatusCode();
        Assert.True((await bob.GetAsync<NotificationListDto>("/api/notifications")).Items.First(x => x.Id == n.Id).IsRead);

        // In a project channel only members can be mentioned.
        var project = await NewProjectAsync(ceo);
        var task = await ceo.PostAsync<TaskDto>($"/api/projects/{project}/tasks", new { title = "t" });
        await ceo.PostAsync<TaskDto>($"/api/tasks/{task.Id}/assign", new { employeeId = aliceEmp.Id });
        var channel = await alice.PostAsync<ChannelDto>("/api/chat/channels/open", new { projectId = project });
        var res = await alice.PostAsJsonAsync($"/api/chat/channels/{channel.Id}/messages", new { body = "hey", mentionedEmployeeIds = new[] { bobEmp.Id } });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.DoesNotContain(await alice.GetAsync<List<ChannelMemberDto>>($"/api/chat/channels/{channel.Id}/members"), m => m.EmployeeId == bobEmp.Id);
    }

    [Fact]
    public async Task Meeting_invites_and_action_items_notify_people()
    {
        var ceo = await fx.CeoAsync();
        var (alice, aliceEmp) = await PersonAsync(ceo, "Alice");
        var (bob, bobEmp) = await PersonAsync(ceo, "Bob");
        var before = (await alice.GetAsync<NotificationListDto>("/api/notifications")).Unread;

        var meeting = await ceo.PostAsync<MeetingDto>("/api/meetings", new { title = "Kickoff", scheduledAt = DateTimeOffset.UtcNow.AddDays(1), participantIds = new[] { aliceEmp.Id } });
        var list = await alice.GetAsync<NotificationListDto>("/api/notifications");
        Assert.Equal(before + 1, list.Unread);
        Assert.Equal(NotificationKinds.MeetingInvite, list.Items.First().Kind);
        Assert.Equal($"/meetings/{meeting.Id}", list.Items.First().Link);

        // Re-saving the same participants does not invite again; adding Bob invites only Bob.
        var bobBefore = (await bob.GetAsync<NotificationListDto>("/api/notifications")).Unread;
        await ceo.PutAsync<MeetingDto>($"/api/meetings/{meeting.Id}", new
        {
            title = "Kickoff", scheduledAt = meeting.ScheduledAt, participantIds = new[] { aliceEmp.Id, bobEmp.Id }, version = meeting.Version,
        });
        Assert.Equal(before + 1, (await alice.GetAsync<NotificationListDto>("/api/notifications")).Unread);
        Assert.Equal(bobBefore + 1, (await bob.GetAsync<NotificationListDto>("/api/notifications")).Unread);

        // Editing an existing meeting's agenda and participants (regression: new rows were treated as existing → 409).
        var edited = await ceo.GetAsync<MeetingDto>($"/api/meetings/{meeting.Id}");
        edited = await ceo.PutAsync<MeetingDto>($"/api/meetings/{meeting.Id}", new
        {
            title = "Kickoff", scheduledAt = edited.ScheduledAt, participantIds = new[] { bobEmp.Id },
            agenda = new[] { new { title = "Goals" }, new { title = "Timeline" } }, version = edited.Version,
        });
        Assert.Equal(["Goals", "Timeline"], edited.Agenda.Select(a => a.Title));
        Assert.Equal(bobEmp.Id, Assert.Single(edited.Participants).EmployeeId);
        edited = await ceo.PutAsync<MeetingDto>($"/api/meetings/{meeting.Id}", new
        {
            title = "Kickoff", scheduledAt = edited.ScheduledAt, participantIds = new[] { bobEmp.Id, aliceEmp.Id },
            agenda = new[] { new { title = "Goals only" } }, version = edited.Version,
        });
        Assert.Equal("Goals only", Assert.Single(edited.Agenda).Title);
        Assert.Equal(2, edited.Participants.Count);
        before += 1; // Alice was invited again after being removed

        await ceo.PostAsync<MeetingDto>($"/api/meetings/{meeting.Id}/action-items", new { title = "Send the slides", ownerEmployeeId = aliceEmp.Id });
        var after = await alice.GetAsync<NotificationListDto>("/api/notifications");
        Assert.Equal(before + 2, after.Unread);
        Assert.Equal(NotificationKinds.ActionItem, after.Items.First().Kind);

        (await alice.PostAsync("/api/notifications/read", null)).EnsureSuccessStatusCode();
        Assert.Equal(0, (await alice.GetAsync<NotificationListDto>("/api/notifications")).Unread);
    }
}
