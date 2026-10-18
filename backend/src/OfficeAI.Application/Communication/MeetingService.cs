using System.Text;
using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Application.Knowledge;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Communication;
using OfficeAI.Domain.Knowledge;
using OfficeAI.Domain.Work;

namespace OfficeAI.Application.Communication;

public record MeetingSummaryDto(Guid Id, string Title, Guid? ProjectId, string? ProjectName, Guid? DepartmentId, DateTimeOffset ScheduledAt,
    int DurationMinutes, string? Location, MeetingStatus Status, MinutesStatus MinutesStatus, int ParticipantCount, int OpenActionItems, uint Version);
public record AgendaItemDto(Guid Id, int SortOrder, string Title, string? Notes, Guid? OwnerEmployeeId, string? OwnerName, int? DurationMinutes);
public record ParticipantDto(Guid EmployeeId, string DisplayName, Domain.Organization.EmployeeKind Kind, Attendance Attendance);
public record ActionItemDto(Guid Id, string Title, Guid? OwnerEmployeeId, string? OwnerName, DateOnly? DueDate, bool IsDone, Guid? TaskId, uint Version);
public record MeetingDto(Guid Id, string Title, string? Purpose, Guid? ProjectId, string? ProjectName, Guid? DepartmentId, string? DepartmentName,
    DateTimeOffset ScheduledAt, int DurationMinutes, string? Location, MeetingStatus Status, string? OrganizerName,
    List<AgendaItemDto> Agenda, List<ParticipantDto> Participants, List<ActionItemDto> ActionItems,
    string? Minutes, MinutesStatus MinutesStatus, string? MinutesAuthorName, bool MinutesIsSimulated, DateTimeOffset? MinutesApprovedAt,
    Guid? MinutesDocumentId, uint Version);
public record AgendaItemInput(string Title, string? Notes = null, Guid? OwnerEmployeeId = null, int? DurationMinutes = null);
public record SaveMeetingRequest(string Title, DateTimeOffset ScheduledAt, int DurationMinutes = 30, string? Purpose = null, Guid? ProjectId = null,
    Guid? DepartmentId = null, string? Location = null, List<Guid>? ParticipantIds = null, List<AgendaItemInput>? Agenda = null, uint? Version = null);
public record SetMeetingStatusRequest(MeetingStatus Status);
public record SetAttendanceRequest(Guid EmployeeId, Attendance Attendance);
public record SaveMinutesRequest(string Minutes, uint? Version = null);
public record AddActionItemRequest(string Title, Guid? OwnerEmployeeId = null, DateOnly? DueDate = null);
public record UpdateActionItemRequest(bool IsDone, uint? Version = null);

/// <summary>Meetings: agenda, participants, minutes (approved by a person) and action items that can become tasks.</summary>
public class MeetingService(IAppDbContext db, ICurrentUser current, IRealtimeNotifier notifier, WorkService work, TimeProvider clock)
{
    public const int MaxAgendaItems = 30;
    public const int MaxParticipants = 50;

    public async Task<List<MeetingSummaryDto>> ListAsync(DateTimeOffset? from, DateTimeOffset? to, Guid? projectId, CancellationToken ct)
    {
        var q = db.Meetings.AsNoTracking();
        if (from is { } f) q = q.Where(m => m.ScheduledAt >= f);
        if (to is { } t) q = q.Where(m => m.ScheduledAt < t);
        if (projectId is { } p) q = q.Where(m => m.ProjectId == p);
        return await q.OrderBy(m => m.ScheduledAt).Take(200)
            .Select(m => new MeetingSummaryDto(m.Id, m.Title, m.ProjectId, db.Projects.Where(x => x.Id == m.ProjectId).Select(x => x.Name).FirstOrDefault(),
                m.DepartmentId, m.ScheduledAt, m.DurationMinutes, m.Location, m.Status, m.MinutesStatus, m.Participants.Count,
                m.ActionItems.Count(a => !a.IsDone), m.Version))
            .ToListAsync(ct);
    }

    public async Task<MeetingDto> GetAsync(Guid id, CancellationToken ct)
    {
        var m = await db.Meetings.AsNoTracking().Include(x => x.AgendaItems).Include(x => x.Participants).Include(x => x.ActionItems)
            .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Meeting", id);
        var people = m.AgendaItems.Select(i => i.OwnerEmployeeId).Concat(m.ActionItems.Select(a => a.OwnerEmployeeId))
            .Concat(m.Participants.Select(p => (Guid?)p.EmployeeId)).Append(m.OrganizerEmployeeId).Append(m.MinutesAuthorEmployeeId)
            .OfType<Guid>().Distinct().ToList();
        var emp = await db.Employees.AsNoTracking().Where(e => people.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        string? Name(Guid? e) => e is { } x && emp.TryGetValue(x, out var p) ? p.DisplayName : null;
        var project = m.ProjectId is { } pid ? await db.Projects.AsNoTracking().Where(p => p.Id == pid).Select(p => p.Name).FirstOrDefaultAsync(ct) : null;
        var dept = m.DepartmentId is { } did ? await db.Departments.AsNoTracking().Where(d => d.Id == did).Select(d => d.Name).FirstOrDefaultAsync(ct) : null;
        return new MeetingDto(m.Id, m.Title, m.Purpose, m.ProjectId, project, m.DepartmentId, dept, m.ScheduledAt, m.DurationMinutes, m.Location, m.Status,
            Name(m.OrganizerEmployeeId),
            m.AgendaItems.OrderBy(i => i.SortOrder).Select(i => new AgendaItemDto(i.Id, i.SortOrder, i.Title, i.Notes, i.OwnerEmployeeId, Name(i.OwnerEmployeeId), i.DurationMinutes)).ToList(),
            m.Participants.Where(p => emp.ContainsKey(p.EmployeeId)).OrderBy(p => emp[p.EmployeeId].DisplayName)
                .Select(p => new ParticipantDto(p.EmployeeId, emp[p.EmployeeId].DisplayName, emp[p.EmployeeId].Kind, p.Attendance)).ToList(),
            m.ActionItems.OrderBy(a => a.IsDone).ThenBy(a => a.DueDate).ThenBy(a => a.CreatedAt)
                .Select(a => new ActionItemDto(a.Id, a.Title, a.OwnerEmployeeId, Name(a.OwnerEmployeeId), a.DueDate, a.IsDone, a.TaskId, a.Version)).ToList(),
            m.Minutes, m.MinutesStatus, Name(m.MinutesAuthorEmployeeId), m.MinutesIsSimulated, m.MinutesApprovedAt, m.MinutesDocumentId, m.Version);
    }

    private async Task ApplyAsync(Meeting m, SaveMeetingRequest req, CancellationToken ct)
    {
        Validate.Required(req.Title, "title");
        if (req.Title.Trim().Length > 200) throw new DomainException("validation", "title must be at most 200 characters.");
        if (req.DurationMinutes is < 5 or > 600) throw new DomainException("validation", "durationMinutes must be between 5 and 600.");
        if (req.ScheduledAt == default) throw new DomainException("validation", "scheduledAt is required.");
        if (req.ProjectId is { } p && !await db.Projects.AnyAsync(x => x.Id == p, ct)) throw new NotFoundException("Project", p);
        if (req.DepartmentId is { } d && !await db.Departments.AnyAsync(x => x.Id == d, ct)) throw new NotFoundException("Department", d);

        var agenda = req.Agenda ?? [];
        if (agenda.Count > MaxAgendaItems) throw new DomainException("validation", $"An agenda has at most {MaxAgendaItems} items.");
        if (agenda.Any(i => string.IsNullOrWhiteSpace(i.Title))) throw new DomainException("validation", "Every agenda item needs a title.");
        var participants = (req.ParticipantIds ?? []).Distinct().ToList();
        if (participants.Count > MaxParticipants) throw new DomainException("validation", $"A meeting has at most {MaxParticipants} participants.");
        var referenced = participants.Concat(agenda.Select(i => i.OwnerEmployeeId).OfType<Guid>()).Distinct().ToList();
        var known = await db.Employees.Where(e => referenced.Contains(e.Id)).Select(e => e.Id).ToListAsync(ct);
        if (referenced.Except(known).FirstOrDefault() is var missing && missing != Guid.Empty) throw new NotFoundException("Employee", missing);

        m.Title = req.Title.Trim();
        m.Purpose = req.Purpose?.Trim();
        m.ProjectId = req.ProjectId;
        m.DepartmentId = req.DepartmentId;
        m.ScheduledAt = req.ScheduledAt;
        m.DurationMinutes = req.DurationMinutes;
        m.Location = req.Location?.Trim();

        // The agenda is replaced as a whole; participants are merged so recorded attendance survives an edit.
        db.MeetingAgendaItems.RemoveRange(m.AgendaItems);
        m.AgendaItems = agenda.Select((i, n) => new MeetingAgendaItem
        {
            MeetingId = m.Id, SortOrder = n + 1, Title = i.Title.Trim(), Notes = i.Notes?.Trim(), OwnerEmployeeId = i.OwnerEmployeeId, DurationMinutes = i.DurationMinutes,
        }).ToList();
        foreach (var gone in m.Participants.Where(p => !participants.Contains(p.EmployeeId)).ToList())
        {
            db.MeetingParticipants.Remove(gone);
            m.Participants.Remove(gone);
        }
        foreach (var e in participants.Where(e => m.Participants.All(p => p.EmployeeId != e)))
            m.Participants.Add(new MeetingParticipant { MeetingId = m.Id, EmployeeId = e });
    }

    private Task<Guid?> MyEmployeeIdAsync(CancellationToken ct) =>
        db.Employees.Where(e => e.UserId == current.UserId).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);

    public async Task<MeetingDto> CreateAsync(SaveMeetingRequest req, CancellationToken ct)
    {
        var m = new Meeting { Title = "" };
        await ApplyAsync(m, req, ct);
        m.OrganizerEmployeeId = await MyEmployeeIdAsync(ct);
        db.Meetings.Add(m);
        await db.SaveChangesAsync(ct);
        await notifier.MeetingChangedAsync(m.Id, ct);
        return await GetAsync(m.Id, ct);
    }

    private async Task<Meeting> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Meetings.Include(x => x.AgendaItems).Include(x => x.Participants).Include(x => x.ActionItems)
            .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Meeting", id);

    public async Task<MeetingDto> UpdateAsync(Guid id, SaveMeetingRequest req, CancellationToken ct)
    {
        var m = await LoadAsync(id, ct);
        if (req.Version is { } v) db.ExpectVersion(m, v);
        if (m.Status == MeetingStatus.Cancelled) throw new DomainException("validation", "A cancelled meeting cannot be edited.");
        await ApplyAsync(m, req, ct);
        await db.SaveChangesAsync(ct);
        await notifier.MeetingChangedAsync(m.Id, ct);
        return await GetAsync(m.Id, ct);
    }

    public async Task<MeetingDto> SetStatusAsync(Guid id, SetMeetingStatusRequest req, CancellationToken ct)
    {
        if (!Enum.IsDefined(req.Status)) throw new DomainException("validation", "status is invalid.");
        var m = await db.Meetings.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Meeting", id);
        if (m.MinutesStatus == MinutesStatus.Approved && req.Status != MeetingStatus.Held)
            throw new DomainException("validation", "A meeting with approved minutes stays held.");
        m.Status = req.Status;
        await db.SaveChangesAsync(ct);
        await notifier.MeetingChangedAsync(m.Id, ct);
        return await GetAsync(m.Id, ct);
    }

    public async Task<MeetingDto> SetAttendanceAsync(Guid id, SetAttendanceRequest req, CancellationToken ct)
    {
        if (!Enum.IsDefined(req.Attendance)) throw new DomainException("validation", "attendance is invalid.");
        var p = await db.MeetingParticipants.FirstOrDefaultAsync(x => x.MeetingId == id && x.EmployeeId == req.EmployeeId, ct)
            ?? throw new NotFoundException("Participant", req.EmployeeId);
        p.Attendance = req.Attendance;
        await db.SaveChangesAsync(ct);
        await notifier.MeetingChangedAsync(id, ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Anyone who may post writes draft minutes. Changing approved minutes reopens them as a draft that needs approval again.</summary>
    public async Task<MeetingDto> SaveMinutesAsync(Guid id, SaveMinutesRequest req, CancellationToken ct)
    {
        Validate.Required(req.Minutes, "minutes");
        var m = await db.Meetings.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Meeting", id);
        if (req.Version is { } v) db.ExpectVersion(m, v);
        if (m.Status == MeetingStatus.Cancelled) throw new DomainException("validation", "A cancelled meeting has no minutes.");
        m.Minutes = req.Minutes;
        m.MinutesStatus = MinutesStatus.Draft;
        m.MinutesAuthorEmployeeId = await MyEmployeeIdAsync(ct);
        m.MinutesIsSimulated = false;
        m.MinutesApprovedAt = null;
        m.MinutesApprovedByUserId = null;
        await db.SaveChangesAsync(ct);
        await notifier.MeetingChangedAsync(m.Id, ct);
        return await GetAsync(m.Id, ct);
    }

    /// <summary>A person approves the minutes. For a project meeting they are also filed as a final project document (new revision each time).</summary>
    public async Task<MeetingDto> ApproveMinutesAsync(Guid id, CancellationToken ct)
    {
        if (current.IsAgent) throw new ForbiddenException("Only a person can approve minutes.");
        var m = await LoadAsync(id, ct);
        if (m.MinutesStatus != MinutesStatus.Draft || string.IsNullOrWhiteSpace(m.Minutes)) throw new DomainException("validation", "There are no draft minutes to approve.");
        if (m.MinutesIsSimulated) throw new DomainException("validation", "Minutes from the simulated provider cannot be approved. Edit them first.");
        m.MinutesStatus = MinutesStatus.Approved;
        m.MinutesApprovedAt = clock.GetUtcNow();
        m.MinutesApprovedByUserId = current.UserId;
        m.Status = MeetingStatus.Held;
        if (m.ProjectId is { } projectId)
        {
            var doc = await KnowledgeService.AddVersionAsync(db, projectId, DocumentKind.MeetingMinutes, $"{m.Title} — {m.ScheduledAt:yyyy-MM-dd}",
                await RenderMinutesAsync(m, ct), ct);
            doc.Status = DocumentStatus.Final;
            doc.AuthorEmployeeId = m.MinutesAuthorEmployeeId;
            m.MinutesDocumentId = doc.Id;
        }
        await db.SaveChangesAsync(ct);
        await notifier.MeetingChangedAsync(m.Id, ct);
        return await GetAsync(m.Id, ct);
    }

    private async Task<string> RenderMinutesAsync(Meeting m, CancellationToken ct)
    {
        var ids = m.Participants.Select(p => p.EmployeeId).Concat(m.ActionItems.Select(a => a.OwnerEmployeeId).OfType<Guid>()).Distinct().ToList();
        var names = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.DisplayName, ct);
        var sb = new StringBuilder();
        sb.AppendLine($"# {m.Title}").AppendLine();
        sb.AppendLine($"- Date: {m.ScheduledAt:yyyy-MM-dd HH:mm} UTC ({m.DurationMinutes} min)");
        if (!string.IsNullOrWhiteSpace(m.Location)) sb.AppendLine($"- Location: {m.Location}");
        var attended = m.Participants.Where(p => p.Attendance == Attendance.Attended).Select(p => names.GetValueOrDefault(p.EmployeeId)).OfType<string>().ToList();
        var absent = m.Participants.Where(p => p.Attendance == Attendance.Absent).Select(p => names.GetValueOrDefault(p.EmployeeId)).OfType<string>().ToList();
        if (attended.Count > 0) sb.AppendLine($"- Attended: {string.Join(", ", attended)}");
        if (absent.Count > 0) sb.AppendLine($"- Absent: {string.Join(", ", absent)}");
        if (m.AgendaItems.Count > 0)
        {
            sb.AppendLine().AppendLine("## Agenda");
            foreach (var i in m.AgendaItems.OrderBy(i => i.SortOrder)) sb.AppendLine($"{i.SortOrder}. {i.Title}");
        }
        sb.AppendLine().AppendLine("## Minutes").AppendLine().AppendLine(m.Minutes);
        if (m.ActionItems.Count > 0)
        {
            sb.AppendLine().AppendLine("## Action items");
            foreach (var a in m.ActionItems)
                sb.AppendLine($"- [{(a.IsDone ? "x" : " ")}] {a.Title}" + (a.OwnerEmployeeId is { } o ? $" — {names.GetValueOrDefault(o)}" : "") + (a.DueDate is { } d ? $" (due {d:yyyy-MM-dd})" : ""));
        }
        return sb.ToString();
    }

    public async Task<MeetingDto> AddActionItemAsync(Guid id, AddActionItemRequest req, CancellationToken ct)
    {
        Validate.Required(req.Title, "title");
        if (!await db.Meetings.AnyAsync(x => x.Id == id, ct)) throw new NotFoundException("Meeting", id);
        if (req.OwnerEmployeeId is { } o && !await db.Employees.AnyAsync(e => e.Id == o, ct)) throw new NotFoundException("Employee", o);
        db.MeetingActionItems.Add(new MeetingActionItem { MeetingId = id, Title = req.Title.Trim(), OwnerEmployeeId = req.OwnerEmployeeId, DueDate = req.DueDate });
        await db.SaveChangesAsync(ct);
        await notifier.MeetingChangedAsync(id, ct);
        return await GetAsync(id, ct);
    }

    public async Task<MeetingDto> UpdateActionItemAsync(Guid id, Guid itemId, UpdateActionItemRequest req, CancellationToken ct)
    {
        var a = await db.MeetingActionItems.FirstOrDefaultAsync(x => x.Id == itemId && x.MeetingId == id, ct) ?? throw new NotFoundException("ActionItem", itemId);
        if (req.Version is { } v) db.ExpectVersion(a, v);
        a.IsDone = req.IsDone;
        await db.SaveChangesAsync(ct);
        await notifier.MeetingChangedAsync(id, ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Turns an action item of a project meeting into a task (assigned to its owner). Once only.</summary>
    public async Task<MeetingDto> CreateTaskAsync(Guid id, Guid itemId, CancellationToken ct)
    {
        var m = await db.Meetings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Meeting", id);
        if (m.ProjectId is not { } projectId) throw new DomainException("validation", "Only action items of a project meeting can become tasks.");
        var a = await db.MeetingActionItems.FirstOrDefaultAsync(x => x.Id == itemId && x.MeetingId == id, ct) ?? throw new NotFoundException("ActionItem", itemId);
        if (a.TaskId is not null) throw new DomainException("task.exists", "This action item already has a task.");
        // Task and link are saved together; the item's row version makes a second concurrent click fail instead of duplicating the task.
        var task = new WorkTask { ProjectId = projectId, Title = a.Title, Description = $"From meeting \"{m.Title}\" ({m.ScheduledAt:yyyy-MM-dd}).", DueDate = a.DueDate };
        db.Tasks.Add(task);
        a.TaskId = task.Id;
        await db.SaveChangesAsync(ct);
        if (a.OwnerEmployeeId is { } owner) await work.AssignAsync(task.Id, new AssignRequest(owner), ct);
        else await notifier.TaskChangedAsync(projectId, task.Id, ct);
        await notifier.MeetingChangedAsync(id, ct);
        return await GetAsync(id, ct);
    }
}
