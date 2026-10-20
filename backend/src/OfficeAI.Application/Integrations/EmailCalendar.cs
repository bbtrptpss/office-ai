using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OfficeAI.Application.Abstractions;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Communication;
using OfficeAI.Domain.Integrations;
using OfficeAI.Domain.Organization;
using OfficeAI.Domain.Work;

namespace OfficeAI.Application.Integrations;

public record EmailDeliveryDto(long Id, string Subject, EmailStatus Status, int Attempts, string? Error, DateTimeOffset CreatedAt, DateTimeOffset? SentAt);
/// <param name="EmailConfigured">The server has an SMTP account; without one nothing is sent.</param>
public record DeliverySettingsDto(bool EmailConfigured, string? FromAddress, string? MyEmail, bool EmailNotifications, DateTimeOffset? EmailNotificationsSince,
    bool CalendarFeedActive, DateTimeOffset? CalendarFeedCreatedAt, List<EmailDeliveryDto> RecentEmails);
public record SetEmailNotificationsRequest(bool Enabled);
/// <param name="Url">Shown once. Anyone with the link can read the calendar, so it is never stored.</param>
public record CalendarFeedDto(string Url, DateTimeOffset CreatedAt);
public record IcsFile(string FileName, byte[] Content);

/// <summary>One event of an iCalendar (RFC 5545) file.</summary>
public record IcsEvent(string Uid, string Summary, string? Description, string? Location, DateTimeOffset? Start, DateTimeOffset? End, DateOnly? AllDay,
    bool Cancelled, string? Url, IReadOnlyList<(string Email, string Name)> Attendees);

/// <summary>Writes iCalendar text: CRLF lines, escaped values, folded at 75 octets without splitting a UTF-8 character.</summary>
public static class Ics
{
    public const string ContentType = "text/calendar; charset=utf-8";

    public static string Write(IEnumerable<IcsEvent> events, string method, string calendarName, DateTimeOffset now, string? organizerEmail = null)
    {
        var sb = new StringBuilder();
        void Line(string s) => Fold(sb, s);
        Line("BEGIN:VCALENDAR");
        Line("VERSION:2.0");
        Line("PRODID:-//AI Software House//Office AI//EN");
        Line("CALSCALE:GREGORIAN");
        Line($"METHOD:{method}");
        Line($"X-WR-CALNAME:{Escape(calendarName)}");
        foreach (var e in events)
        {
            Line("BEGIN:VEVENT");
            Line($"UID:{e.Uid}");
            Line($"DTSTAMP:{Utc(now)}");
            if (e.AllDay is { } day)
            {
                Line($"DTSTART;VALUE=DATE:{Date(day)}");
                Line($"DTEND;VALUE=DATE:{Date(day.AddDays(1))}");
            }
            else
            {
                Line($"DTSTART:{Utc(e.Start!.Value)}");
                Line($"DTEND:{Utc(e.End!.Value)}");
            }
            Line($"SUMMARY:{Escape(e.Summary)}");
            if (!string.IsNullOrWhiteSpace(e.Description)) Line($"DESCRIPTION:{Escape(e.Description)}");
            if (!string.IsNullOrWhiteSpace(e.Location)) Line($"LOCATION:{Escape(e.Location)}");
            if (e.Url is not null) Line($"URL:{e.Url}");
            Line($"STATUS:{(e.Cancelled ? "CANCELLED" : "CONFIRMED")}");
            if (organizerEmail is not null) Line($"ORGANIZER;CN=AI Software House:mailto:{organizerEmail}");
            foreach (var (email, name) in e.Attendees)
                Line($"ATTENDEE;CN={QuoteParam(name)};ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION:mailto:{email}");
            Line("END:VEVENT");
        }
        Line("END:VCALENDAR");
        return sb.ToString();
    }

    // Invariant culture: a Thai-culture server would otherwise write Buddhist-era years.
    private static string Date(DateOnly d) => d.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private static string Utc(DateTimeOffset t) => t.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    public static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "");

    private static string QuoteParam(string s) => $"\"{s.Replace("\"", "'")}\"";

    private static void Fold(StringBuilder sb, string line)
    {
        var bytes = 0;
        var limit = 75;
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (bytes + size > limit)
            {
                sb.Append("\r\n ");
                bytes = 0;
                limit = 74; // continuation lines start with a space
            }
            sb.Append(rune.ToString());
            bytes += size;
        }
        sb.Append("\r\n");
    }
}

/// <summary>Personal email notifications and calendar feeds. Each person only ever changes their own settings.</summary>
public partial class EmailCalendarService(IAppDbContext db, ICurrentUser current, IEmailSender email, IUserDirectory users, TimeProvider clock,
    IOptions<IntegrationSettings> settings)
{
    private Guid Me => current.UserId is { } u && !current.IsAgent ? u : throw new ForbiddenException("Only a signed-in person has delivery settings.");

    public async Task<DeliverySettingsDto> GetAsync(CancellationToken ct)
    {
        var me = Me;
        var pref = await db.UserPreferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == me, ct);
        var contact = (await users.ContactsAsync([me], ct)).GetValueOrDefault(me);
        var recent = await db.EmailDeliveries.AsNoTracking().Where(d => d.UserId == me).OrderByDescending(d => d.Id).Take(10)
            .Select(d => new EmailDeliveryDto(d.Id, d.Subject, d.Status, d.Attempts, d.Error, d.CreatedAt, d.SentAt)).ToListAsync(ct);
        return new DeliverySettingsDto(email.IsConfigured, email.FromAddress, contact.Email, pref?.EmailNotificationsSince is not null,
            pref?.EmailNotificationsSince, pref?.CalendarTokenHash is not null, pref?.CalendarTokenCreatedAt, recent);
    }

    private async Task<UserPreference> MineAsync(CancellationToken ct)
    {
        var me = Me;
        var pref = await db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == me, ct);
        if (pref is null)
        {
            pref = new UserPreference { UserId = me };
            db.UserPreferences.Add(pref);
        }
        return pref;
    }

    public async Task<DeliverySettingsDto> SetEmailNotificationsAsync(SetEmailNotificationsRequest req, CancellationToken ct)
    {
        var pref = await MineAsync(ct);
        // Turning it on starts from now: nothing that happened before is mailed.
        if (req.Enabled && pref.EmailNotificationsSince is null) pref.EmailNotificationsSince = clock.GetUtcNow();
        if (!req.Enabled) pref.EmailNotificationsSince = null;
        await db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }

    /// <summary>Sends a test message to the caller's own address.</summary>
    public async Task<DeliverySettingsDto> SendTestAsync(CancellationToken ct)
    {
        var me = Me;
        if (!email.IsConfigured) throw new DomainException("email.not_configured", "Email is not configured on the server (Email__Host and Email__FromAddress).");
        if (!(await users.ContactsAsync([me], ct)).TryGetValue(me, out var contact))
            throw new DomainException("email.no_address", "Your account has no email address.");
        try
        {
            await email.SendAsync(new OutgoingEmail(contact.Email, contact.Name, "[AI Software House] Test email",
                $"Hello {contact.Name},\n\nThis is a test from AI Software House. Email notifications can reach you.\n"), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new DomainException("email.send_failed", $"The test email could not be sent: {ex.Message}");
        }
        return await GetAsync(ct);
    }

    /// <summary>Creates (or replaces) the caller's private calendar link. Any earlier link stops working.</summary>
    public async Task<CalendarFeedDto> CreateFeedAsync(string apiBaseUrl, CancellationToken ct)
    {
        var pref = await MineAsync(ct);
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        pref.CalendarTokenHash = Hash(token);
        pref.CalendarTokenCreatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        var baseUrl = (settings.Value.ApiBaseUrl ?? apiBaseUrl).TrimEnd('/');
        return new CalendarFeedDto($"{baseUrl}/api/calendar/feed/{token}.ics", pref.CalendarTokenCreatedAt.Value);
    }

    public async Task<DeliverySettingsDto> RevokeFeedAsync(CancellationToken ct)
    {
        var pref = await MineAsync(ct);
        pref.CalendarTokenHash = null;
        pref.CalendarTokenCreatedAt = null;
        await db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }

    private static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    [GeneratedRegex("^[A-Za-z0-9_-]{43}$")]
    private static partial Regex TokenShape();

    /// <summary>
    /// The calendar behind a private link: the person's meetings (organizer or participant, from 60 days ago to a year ahead)
    /// and the due dates of open tasks assigned to them. Null when the link is unknown, revoked or the account is inactive.
    /// </summary>
    public async Task<string?> FeedAsync(string token, CancellationToken ct)
    {
        if (!TokenShape().IsMatch(token)) return null;
        var hash = Hash(token);
        var pref = await db.UserPreferences.AsNoTracking().FirstOrDefaultAsync(p => p.CalendarTokenHash == hash, ct);
        if (pref is null) return null;
        var contact = await users.ContactsAsync([pref.UserId], ct);
        if (!contact.ContainsKey(pref.UserId)) return null;
        var employeeId = await db.Employees.AsNoTracking().Where(e => e.UserId == pref.UserId && e.Kind == EmployeeKind.Human).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);

        var now = clock.GetUtcNow();
        var events = new List<IcsEvent>();
        if (employeeId is { } me)
        {
            var from = now.AddDays(-60);
            var to = now.AddDays(365);
            var meetings = await db.Meetings.AsNoTracking()
                .Where(m => m.ScheduledAt >= from && m.ScheduledAt <= to && (m.OrganizerEmployeeId == me || m.Participants.Any(p => p.EmployeeId == me)))
                .OrderBy(m => m.ScheduledAt).Take(500).ToListAsync(ct);
            events.AddRange(meetings.Select(m => MeetingEvent(m, [])));

            var tasks = await db.Tasks.AsNoTracking()
                .Where(t => t.DueDate != null && t.Status != WorkTaskStatus.Done && t.Status != WorkTaskStatus.Cancelled
                    && t.Assignments.Any(a => a.EmployeeId == me && a.UnassignedAt == null))
                .Select(t => new { t.Id, t.Title, t.DueDate, t.ProjectId, t.Status }).Take(500).ToListAsync(ct);
            var link = settings.Value.AppBaseUrl?.TrimEnd('/');
            events.AddRange(tasks.Select(t => new IcsEvent($"task-{t.Id}@officeai", $"Due: {t.Title}", $"Status: {t.Status}", null, null, null, t.DueDate,
                false, link is null ? null : $"{link}/projects/{t.ProjectId}", [])));
        }
        return Ics.Write(events, "PUBLISH", "AI Software House", now);
    }

    public IcsEvent MeetingEvent(Meeting m, IReadOnlyList<(string Email, string Name)> attendees)
    {
        var link = settings.Value.AppBaseUrl?.TrimEnd('/');
        var description = string.Join("\n", new[] { m.Purpose }.Concat(m.AgendaItems.OrderBy(a => a.SortOrder).Select(a => $"• {a.Title}"))
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        return new IcsEvent($"meeting-{m.Id}@officeai", m.Title, description, m.Location, m.ScheduledAt, m.ScheduledAt.AddMinutes(m.DurationMinutes), null,
            m.Status == MeetingStatus.Cancelled, link is null ? null : $"{link}/meetings/{m.Id}", attendees);
    }

    /// <summary>One meeting as an .ics file (people who are participants are listed as attendees).</summary>
    public async Task<IcsFile> MeetingIcsAsync(Guid meetingId, CancellationToken ct)
    {
        var m = await db.Meetings.AsNoTracking().Include(x => x.AgendaItems).Include(x => x.Participants).FirstOrDefaultAsync(x => x.Id == meetingId, ct)
            ?? throw new NotFoundException("Meeting", meetingId);
        var attendees = await AttendeesAsync(db, users, m, ct);
        var text = Ics.Write([MeetingEvent(m, attendees)], "PUBLISH", m.Title, clock.GetUtcNow());
        return new IcsFile($"meeting-{m.ScheduledAt.UtcDateTime.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}.ics", Encoding.UTF8.GetBytes(text));
    }

    public static async Task<List<(string Email, string Name)>> AttendeesAsync(IAppDbContext db, IUserDirectory users, Meeting m, CancellationToken ct)
    {
        var ids = m.Participants.Select(p => p.EmployeeId).ToList();
        var userIds = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id) && e.Kind == EmployeeKind.Human && e.UserId != null)
            .Select(e => e.UserId!.Value).ToListAsync(ct);
        return [.. (await users.ContactsAsync(userIds, ct)).Values.OrderBy(c => c.Name)];
    }
}

/// <summary>
/// Emails each opted-in person a copy of their new notifications. Meeting invites carry the meeting as a calendar invitation.
/// Without an SMTP account nothing happens; failures are retried up to <see cref="MaxAttempts"/> times.
/// </summary>
public sealed partial class EmailDispatcher(IAppDbContext db, IEmailSender email, IUserDirectory users, TimeProvider clock,
    IOptions<IntegrationSettings> settings, EmailCalendarService calendar, ILogger<EmailDispatcher> log)
{
    public const int MaxAttempts = 3;

    [GeneratedRegex("^/meetings/([0-9a-fA-F-]{36})$")]
    private static partial Regex MeetingLink();

    public async Task<int> TickAsync(CancellationToken ct)
    {
        if (!email.IsConfigured) return 0;
        var now = clock.GetUtcNow();
        // Only recent notifications: switching email on (or fixing the server) never mails a backlog.
        var cutoff = now.AddDays(-1);
        var due = await db.Notifications.AsNoTracking()
            .Join(db.UserPreferences.Where(p => p.EmailNotificationsSince != null), n => n.UserId, p => p.UserId, (n, p) => new { n, p.EmailNotificationsSince })
            .Where(x => x.n.CreatedAt >= x.EmailNotificationsSince && x.n.CreatedAt >= cutoff
                && !db.EmailDeliveries.Any(d => d.NotificationId == x.n.Id && (d.Status == EmailStatus.Sent || d.Attempts >= MaxAttempts)))
            .OrderBy(x => x.n.Id).Select(x => x.n).Take(50).ToListAsync(ct);
        if (due.Count == 0) return 0;

        var contacts = await users.ContactsAsync(due.Select(n => n.UserId).Distinct().ToList(), ct);
        var link = settings.Value.AppBaseUrl?.TrimEnd('/');
        var sent = 0;
        foreach (var n in due)
        {
            var delivery = await db.EmailDeliveries.FirstOrDefaultAsync(d => d.NotificationId == n.Id, ct);
            if (delivery is null)
            {
                delivery = new EmailDelivery { NotificationId = n.Id, UserId = n.UserId, Subject = Subject(n.Title), CreatedAt = now };
                db.EmailDeliveries.Add(delivery);
            }
            delivery.Attempts++;
            if (!contacts.TryGetValue(n.UserId, out var to))
            {
                (delivery.Status, delivery.Error, delivery.Attempts) = (EmailStatus.Failed, "The account has no email address or is inactive.", MaxAttempts);
                continue;
            }
            try
            {
                var body = new StringBuilder($"{n.Title}\n");
                if (!string.IsNullOrWhiteSpace(n.Body)) body.Append($"{n.Body}\n");
                if (link is not null && n.Link is not null) body.Append($"\n{link}{n.Link}\n");
                body.Append("\n— AI Software House. Turn these emails off under “My settings”.\n");
                await email.SendAsync(new OutgoingEmail(to.Email, to.Name, delivery.Subject, body.ToString(), await InviteAsync(n, ct)), ct);
                (delivery.Status, delivery.Error, delivery.SentAt) = (EmailStatus.Sent, null, clock.GetUtcNow());
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Email for notification {NotificationId} failed", n.Id);
                (delivery.Status, delivery.Error) = (EmailStatus.Failed, ex.Message.Length > 500 ? ex.Message[..500] : ex.Message);
            }
        }
        await db.SaveChangesAsync(ct);
        return sent;
    }

    private static string Subject(string title)
    {
        var s = $"[AI Software House] {title}";
        return s.Length <= 300 ? s : s[..299] + "…";
    }

    /// <summary>A meeting invite carries the meeting as an iCalendar REQUEST so mail clients offer “add to calendar”.</summary>
    private async Task<EmailAttachment?> InviteAsync(Notification n, CancellationToken ct)
    {
        if (n.Kind != NotificationKinds.MeetingInvite || n.Link is null || MeetingLink().Match(n.Link) is not { Success: true } match) return null;
        var id = Guid.Parse(match.Groups[1].Value);
        var m = await db.Meetings.AsNoTracking().Include(x => x.AgendaItems).Include(x => x.Participants).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null || m.Status != MeetingStatus.Scheduled) return null;
        var attendees = await EmailCalendarService.AttendeesAsync(db, users, m, ct);
        var text = Ics.Write([calendar.MeetingEvent(m, attendees)], "REQUEST", m.Title, clock.GetUtcNow(), email.FromAddress);
        return new EmailAttachment("invite.ics", "text/calendar; charset=utf-8; method=REQUEST", Encoding.UTF8.GetBytes(text));
    }
}
