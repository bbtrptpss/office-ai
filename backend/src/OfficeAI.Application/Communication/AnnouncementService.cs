using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Communication;

namespace OfficeAI.Application.Communication;

public record AnnouncementDto(Guid Id, string Title, string Body, AnnouncementPriority Priority, AnnouncementStatus Status, Guid? DepartmentId,
    string? DepartmentName, bool IsPinned, DateTimeOffset? PublishedAt, string? AuthorName, bool IsSimulated, bool IsRead, int ReadCount,
    DateTimeOffset CreatedAt, uint Version);
public record SaveAnnouncementRequest(string Title, string Body, AnnouncementPriority Priority = AnnouncementPriority.Normal, Guid? DepartmentId = null,
    bool IsPinned = false, bool Publish = false, uint? Version = null);

/// <summary>Internal announcements. People with comms.manage write and publish; everyone else sees what is published for them.</summary>
public class AnnouncementService(IAppDbContext db, ICurrentUser current, IRealtimeNotifier notifier, TimeProvider clock)
{
    /// <summary>Published announcements for the caller (company-wide + their department). Managers may ask for drafts and archived ones too.</summary>
    public async Task<List<AnnouncementDto>> ListAsync(AnnouncementStatus? status, bool canManage, CancellationToken ct)
    {
        var q = db.Announcements.AsNoTracking();
        if (!canManage || status is null or AnnouncementStatus.Published)
        {
            q = q.Where(a => a.Status == AnnouncementStatus.Published);
            if (!canManage)
            {
                var myDept = await db.Employees.Where(e => e.UserId == current.UserId).Select(e => (Guid?)e.DepartmentId).FirstOrDefaultAsync(ct);
                q = q.Where(a => a.DepartmentId == null || a.DepartmentId == myDept);
            }
        }
        else q = q.Where(a => a.Status == status);
        var rows = await q.OrderByDescending(a => a.IsPinned).ThenByDescending(a => a.PublishedAt ?? a.CreatedAt).Take(100).ToListAsync(ct);
        return await ToDtosAsync(rows, ct);
    }

    public async Task<AnnouncementDto> GetAsync(Guid id, bool canManage, CancellationToken ct)
    {
        var a = await db.Announcements.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Announcement", id);
        if (!canManage && a.Status != AnnouncementStatus.Published) throw new NotFoundException("Announcement", id);
        return (await ToDtosAsync([a], ct))[0];
    }

    private async Task<List<AnnouncementDto>> ToDtosAsync(List<Announcement> rows, CancellationToken ct)
    {
        var ids = rows.Select(r => r.Id).ToList();
        var me = current.UserId;
        var counts = await db.AnnouncementReads.Where(r => ids.Contains(r.AnnouncementId)).GroupBy(r => r.AnnouncementId)
            .Select(g => new { g.Key, Count = g.Count(), Mine = g.Any(r => r.UserId == me) }).ToDictionaryAsync(x => x.Key, ct);
        var deptIds = rows.Select(r => r.DepartmentId).OfType<Guid>().Distinct().ToList();
        var depts = await db.Departments.AsNoTracking().Where(d => deptIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        var authors = rows.Select(r => r.AuthorEmployeeId).OfType<Guid>().Distinct().ToList();
        var names = await db.Employees.AsNoTracking().Where(e => authors.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.DisplayName, ct);
        return rows.Select(a =>
        {
            var c = counts.GetValueOrDefault(a.Id);
            return new AnnouncementDto(a.Id, a.Title, a.Body, a.Priority, a.Status, a.DepartmentId, a.DepartmentId is { } d ? depts.GetValueOrDefault(d) : null,
                a.IsPinned, a.PublishedAt, a.AuthorEmployeeId is { } e ? names.GetValueOrDefault(e) : null, a.IsSimulated, c?.Mine ?? false, c?.Count ?? 0,
                a.CreatedAt, a.Version);
        }).ToList();
    }

    private async Task ApplyAsync(Announcement a, SaveAnnouncementRequest req, CancellationToken ct)
    {
        Validate.Required(req.Title, "title");
        Validate.Required(req.Body, "body");
        if (req.Title.Trim().Length > 200) throw new DomainException("validation", "title must be at most 200 characters.");
        if (!Enum.IsDefined(req.Priority)) throw new DomainException("validation", "priority is invalid.");
        if (req.DepartmentId is { } d && !await db.Departments.AnyAsync(x => x.Id == d, ct)) throw new NotFoundException("Department", d);
        a.Title = req.Title.Trim();
        a.Body = req.Body;
        a.Priority = req.Priority;
        a.DepartmentId = req.DepartmentId;
        a.IsPinned = req.IsPinned;
    }

    public async Task<AnnouncementDto> CreateAsync(SaveAnnouncementRequest req, CancellationToken ct)
    {
        var a = new Announcement { Title = "", Body = "" };
        await ApplyAsync(a, req, ct);
        a.AuthorEmployeeId = await db.Employees.Where(e => e.UserId == current.UserId).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
        if (req.Publish) MarkPublished(a);
        db.Announcements.Add(a);
        await db.SaveChangesAsync(ct);
        await notifier.AnnouncementsChangedAsync(ct);
        return await GetAsync(a.Id, true, ct);
    }

    /// <summary>Editing clears the simulated flag: a person now stands behind the text.</summary>
    public async Task<AnnouncementDto> UpdateAsync(Guid id, SaveAnnouncementRequest req, CancellationToken ct)
    {
        var a = await db.Announcements.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Announcement", id);
        if (req.Version is { } v) db.ExpectVersion(a, v);
        if (a.Status == AnnouncementStatus.Archived) throw new DomainException("validation", "An archived announcement cannot be edited.");
        await ApplyAsync(a, req, ct);
        a.IsSimulated = false;
        if (req.Publish && a.Status == AnnouncementStatus.Draft) MarkPublished(a);
        await db.SaveChangesAsync(ct);
        await notifier.AnnouncementsChangedAsync(ct);
        return await GetAsync(a.Id, true, ct);
    }

    public async Task<AnnouncementDto> PublishAsync(Guid id, CancellationToken ct)
    {
        var a = await db.Announcements.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Announcement", id);
        if (a.Status != AnnouncementStatus.Draft) throw new DomainException("validation", "Only a draft can be published.");
        MarkPublished(a);
        await db.SaveChangesAsync(ct);
        await notifier.AnnouncementsChangedAsync(ct);
        return await GetAsync(a.Id, true, ct);
    }

    private void MarkPublished(Announcement a)
    {
        if (current.IsAgent) throw new ForbiddenException("Only a person can publish an announcement.");
        if (a.IsSimulated) throw new DomainException("validation", "A draft from the simulated provider cannot be published. Edit it first.");
        a.Status = AnnouncementStatus.Published;
        a.PublishedAt = clock.GetUtcNow();
        a.PublishedByUserId = current.UserId;
    }

    public async Task<AnnouncementDto> ArchiveAsync(Guid id, CancellationToken ct)
    {
        var a = await db.Announcements.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Announcement", id);
        a.Status = AnnouncementStatus.Archived;
        a.IsPinned = false;
        await db.SaveChangesAsync(ct);
        await notifier.AnnouncementsChangedAsync(ct);
        return await GetAsync(a.Id, true, ct);
    }

    /// <summary>Acknowledge reading. Idempotent, including under concurrent calls.</summary>
    public async Task MarkReadAsync(Guid id, CancellationToken ct)
    {
        if (current.UserId is not { } me) return;
        if (!await db.Announcements.AnyAsync(a => a.Id == id && a.Status == AnnouncementStatus.Published, ct)) throw new NotFoundException("Announcement", id);
        if (await db.AnnouncementReads.AnyAsync(r => r.AnnouncementId == id && r.UserId == me, ct)) return;
        db.AnnouncementReads.Add(new AnnouncementRead { AnnouncementId = id, UserId = me, ReadAt = clock.GetUtcNow() });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ResetTracking();
        }
    }
}
