using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Api.Endpoints;
using OfficeAI.Application.Communication;
using OfficeAI.Application.Integrations;
using OfficeAI.Domain.Integrations;
using OfficeAI.Domain.Security;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class EmailCalendarTests(ApiFixture fx)
{
    private async Task<int> DispatchAsync()
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EmailDispatcher>().TickAsync(CancellationToken.None);
    }

    private async Task<(HttpClient Client, Guid EmployeeId, string Email)> PersonAsync()
    {
        var (client, employeeId) = await fx.PersonAsync([SystemRoles.Member]);
        var me = await client.GetAsync<MeDto>("/api/auth/me");
        return (client, employeeId, me.Email);
    }

    private static Task<MeetingDto> InviteAsync(HttpClient ceo, Guid employeeId, string title = "Sprint review") =>
        ceo.PostAsync<MeetingDto>("/api/meetings", new { title, scheduledAt = DateTimeOffset.UtcNow.AddDays(2), durationMinutes = 45, location = "Room 1", participantIds = new[] { employeeId } });

    [Fact]
    public async Task Opted_in_people_get_their_new_notifications_by_email_with_a_calendar_invite()
    {
        var ceo = await fx.CeoAsync();
        var (person, employeeId, email) = await PersonAsync();

        // Before opting in: nothing is mailed, and switching on later does not mail the backlog.
        await InviteAsync(ceo, employeeId, "Before opt-in");
        await DispatchAsync();
        Assert.Empty(fx.Email.To(email));

        var settings = await person.PutAsync<DeliverySettingsDto>("/api/me/email-notifications", new { enabled = true });
        Assert.True(settings.EmailNotifications);
        Assert.True(settings.EmailConfigured);
        Assert.Equal(email, settings.MyEmail);
        await DispatchAsync();
        Assert.Empty(fx.Email.To(email));

        var meeting = await InviteAsync(ceo, employeeId, "Design review, part 2");
        Assert.True(await DispatchAsync() >= 1);
        var mail = Assert.Single(fx.Email.To(email));
        Assert.Contains("Design review, part 2", mail.Subject);
        Assert.Contains($"https://office.test/meetings/{meeting.Id}", mail.TextBody);
        var invite = Encoding.UTF8.GetString(mail.Attachment!.Content).Replace("\r\n ", "");
        Assert.Contains("METHOD:REQUEST", invite);
        Assert.Contains($"UID:meeting-{meeting.Id}@officeai", invite);
        Assert.Contains("SUMMARY:Design review\\, part 2", invite);
        Assert.Contains($"mailto:{email}", invite);

        // Each notification is mailed once.
        await DispatchAsync();
        Assert.Single(fx.Email.To(email));
        Assert.Equal(EmailStatus.Sent, Assert.Single((await person.GetAsync<DeliverySettingsDto>("/api/me/delivery-settings")).RecentEmails).Status);

        await person.PutAsync<DeliverySettingsDto>("/api/me/email-notifications", new { enabled = false });
        await InviteAsync(ceo, employeeId, "After opt-out");
        await DispatchAsync();
        Assert.Single(fx.Email.To(email));
    }

    [Fact]
    public async Task Failed_sends_are_retried_a_few_times_and_nothing_is_sent_without_a_server()
    {
        var ceo = await fx.CeoAsync();
        var (person, employeeId, email) = await PersonAsync();
        await person.PutAsync<DeliverySettingsDto>("/api/me/email-notifications", new { enabled = true });
        try
        {
            fx.Email.Configured = false;
            await InviteAsync(ceo, employeeId);
            Assert.Equal(0, await DispatchAsync());
            Assert.False((await person.GetAsync<DeliverySettingsDto>("/api/me/delivery-settings")).EmailConfigured);
            Assert.Equal(HttpStatusCode.Conflict, (await person.PostAsJsonAsync("/api/me/email-test", new { })).StatusCode);

            fx.Email.Configured = true;
            fx.Email.FailWith = new IOException("smtp down");
            for (var i = 0; i < EmailDispatcher.MaxAttempts + 2; i++) await DispatchAsync();
            var failed = Assert.Single((await person.GetAsync<DeliverySettingsDto>("/api/me/delivery-settings")).RecentEmails);
            Assert.Equal(EmailStatus.Failed, failed.Status);
            Assert.Equal(EmailDispatcher.MaxAttempts, failed.Attempts);
            Assert.Contains("smtp down", failed.Error);
        }
        finally
        {
            fx.Email.Configured = true;
            fx.Email.FailWith = null;
        }
        Assert.Empty(fx.Email.To(email));

        await person.PostAsync<DeliverySettingsDto>("/api/me/email-test", new { });
        Assert.Contains("Test", Assert.Single(fx.Email.To(email)).Subject);
    }

    [Fact]
    public async Task Private_calendar_link_lists_my_meetings_and_due_tasks_until_revoked()
    {
        var ceo = await fx.CeoAsync();
        var (person, employeeId, _) = await PersonAsync();
        var meeting = await InviteAsync(ceo, employeeId, "Planning; Q4");
        var project = await ceo.PostAsync<OfficeAI.Application.Work.ProjectDto>("/api/projects", new { code = $"CAL-{Guid.NewGuid():N}"[..14], name = "Calendar project", status = "Active" });
        var task = await ceo.PostAsync<OfficeAI.Application.Work.TaskDto>($"/api/projects/{project.Id}/tasks", new { title = "Ship the calendar", dueDate = "2026-12-01" });
        await ceo.PostAsync<OfficeAI.Application.Work.TaskDto>($"/api/tasks/{task.Id}/assign", new { employeeId });
        var (_, someoneElse, _) = await PersonAsync();
        await InviteAsync(ceo, someoneElse, "Someone else's meeting");

        var feed = await person.PostAsync<CalendarFeedDto>("/api/me/calendar-feed", new { });
        var path = new Uri(feed.Url).PathAndQuery;
        Assert.StartsWith("/api/calendar/feed/", path);
        Assert.EndsWith(".ics", path);

        var anonymous = fx.Anonymous();
        var res = await anonymous.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("text/calendar", res.Content.Headers.ContentType!.MediaType);
        var ics = (await res.Content.ReadAsStringAsync()).Replace("\r\n ", "");
        Assert.StartsWith("BEGIN:VCALENDAR\r\n", ics);
        Assert.Contains($"UID:meeting-{meeting.Id}@officeai", ics);
        Assert.Contains("SUMMARY:Planning\\; Q4", ics);
        Assert.True(ics.Contains("DTSTART;VALUE=DATE:20261201"), ics);
        Assert.Contains("Due: Ship the calendar", ics);
        Assert.DoesNotContain("Someone else", ics);
        Assert.True((await person.GetAsync<DeliverySettingsDto>("/api/me/delivery-settings")).CalendarFeedActive);

        // A new link replaces the old one; revoking ends both.
        var second = await person.PostAsync<CalendarFeedDto>("/api/me/calendar-feed", new { });
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(path)).StatusCode);
        var secondPath = new Uri(second.Url).PathAndQuery;
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(secondPath)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await person.DeleteAsync("/api/me/calendar-feed")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(secondPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/calendar/feed/not-a-token.ics")).StatusCode);
    }

    [Fact]
    public async Task A_meeting_downloads_as_an_ics_file()
    {
        var ceo = await fx.CeoAsync();
        var (_, employeeId, email) = await PersonAsync();
        var meeting = await InviteAsync(ceo, employeeId);
        var res = await ceo.GetAsync($"/api/meetings/{meeting.Id}/ics");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("text/calendar", res.Content.Headers.ContentType!.MediaType);
        Assert.EndsWith(".ics", res.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        var ics = (await res.Content.ReadAsStringAsync()).Replace("\r\n ", "");
        Assert.Contains("METHOD:PUBLISH", ics);
        Assert.Contains("LOCATION:Room 1", ics);
        Assert.Contains($"mailto:{email}", ics);
        Assert.Equal(HttpStatusCode.Unauthorized, (await fx.Anonymous().GetAsync($"/api/meetings/{meeting.Id}/ics")).StatusCode);
    }

    [Fact]
    public void Ics_lines_are_escaped_and_folded_without_splitting_characters()
    {
        var title = string.Concat(Enumerable.Repeat("ประชุมทีม, ", 20));
        var text = Ics.Write([new IcsEvent("x@officeai", title, "line 1\nline 2", null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1), null, false, null, [])],
            "PUBLISH", "Test", DateTimeOffset.UnixEpoch);
        foreach (var line in text.Split("\r\n")) Assert.True(Encoding.UTF8.GetByteCount(line) <= 75, line);
        Assert.Contains("DESCRIPTION:line 1\\nline 2", text);
        var unfolded = text.Replace("\r\n ", "");
        Assert.Contains($"SUMMARY:{title.Replace(",", "\\,")}", unfolded);
        Assert.Contains("DTSTART:19700101T000000Z", text);
    }
}
