using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Domain.Communication;
using OfficeAI.Domain.Organization;

namespace OfficeAI.Application.Communication;

public record NotificationDto(long Id, string Kind, string Title, string? Body, string? Link, DateTimeOffset CreatedAt, bool IsRead);
public record NotificationListDto(int Unread, List<NotificationDto> Items);

/// <summary>Personal notifications (mentions, meeting invites, action items). Each user only ever sees their own.</summary>
public class NotificationService(IAppDbContext db, ICurrentUser current, TimeProvider clock)
{
    /// <summary>
    /// Queues a notification for each employee who is a person with a login, except the caller. Returns the user ids to push to after saving.
    /// </summary>
    public static async Task<List<Guid>> AddForEmployeesAsync(IAppDbContext db, IEnumerable<Guid> employeeIds, Guid? exceptUserId,
        string kind, string title, string? body, string? link, DateTimeOffset now, CancellationToken ct)
    {
        var ids = employeeIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        var users = await db.Employees.AsNoTracking()
            .Where(e => ids.Contains(e.Id) && e.Kind == EmployeeKind.Human && e.UserId != null && e.UserId != exceptUserId)
            .Select(e => e.UserId!.Value).Distinct().ToListAsync(ct);
        foreach (var u in users)
            db.Notifications.Add(new Notification
            {
                UserId = u, Kind = kind, Title = Trim(title, 300)!, Body = Trim(body, 500), Link = link, CreatedAt = now,
            });
        return users;
    }

    private static string? Trim(string? s, int max) => s is null || s.Length <= max ? s : s[..(max - 1)] + "…";

    public async Task<NotificationListDto> ListAsync(CancellationToken ct)
    {
        var me = current.UserId ?? Guid.Empty;
        var unread = await db.Notifications.CountAsync(n => n.UserId == me && n.ReadAt == null, ct);
        var items = await db.Notifications.AsNoTracking().Where(n => n.UserId == me).OrderByDescending(n => n.Id).Take(50)
            .Select(n => new NotificationDto(n.Id, n.Kind, n.Title, n.Body, n.Link, n.CreatedAt, n.ReadAt != null)).ToListAsync(ct);
        return new NotificationListDto(unread, items);
    }

    /// <summary>Marks one notification (or all, when <paramref name="id"/> is null) as read. Other users' rows are never touched.</summary>
    public async Task MarkReadAsync(long? id, CancellationToken ct)
    {
        var me = current.UserId ?? Guid.Empty;
        var rows = await db.Notifications.Where(n => n.UserId == me && n.ReadAt == null && (id == null || n.Id == id)).ToListAsync(ct);
        var now = clock.GetUtcNow();
        foreach (var n in rows) n.ReadAt = now;
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Who belongs to a project: the owner, everyone ever assigned a task in it, and people added by hand.</summary>
public static class ProjectMembership
{
    public static IQueryable<Guid> MemberIds(IAppDbContext db, Guid projectId) =>
        db.ProjectMembers.Where(m => m.ProjectId == projectId).Select(m => m.EmployeeId)
            .Union(db.Projects.Where(p => p.Id == projectId && p.OwnerEmployeeId != null).Select(p => p.OwnerEmployeeId!.Value))
            .Union(db.TaskAssignments.Where(a => db.Tasks.Any(t => t.Id == a.TaskId && t.ProjectId == projectId)).Select(a => a.EmployeeId));

    public static Task<bool> IsMemberAsync(IAppDbContext db, Guid projectId, Guid? employeeId, CancellationToken ct) =>
        employeeId is { } e ? MemberIds(db, projectId).AnyAsync(x => x == e, ct) : Task.FromResult(false);

    /// <summary>Projects the employee belongs to (owner, assignee or added by hand).</summary>
    public static IQueryable<Guid> ProjectsOf(IAppDbContext db, Guid employeeId) =>
        db.ProjectMembers.Where(m => m.EmployeeId == employeeId).Select(m => m.ProjectId)
            .Union(db.Projects.Where(p => p.OwnerEmployeeId == employeeId).Select(p => p.Id))
            .Union(db.TaskAssignments.Where(a => a.EmployeeId == employeeId).Join(db.Tasks, a => a.TaskId, t => t.Id, (a, t) => t.ProjectId));
}
