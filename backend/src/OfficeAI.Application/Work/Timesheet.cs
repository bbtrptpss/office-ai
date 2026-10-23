using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Organization;
using OfficeAI.Domain.Work;

namespace OfficeAI.Application.Work;

public record TimeEntryDto(Guid Id, Guid EmployeeId, string EmployeeName, Guid ProjectId, string ProjectCode, string ProjectName, Guid? TaskId, string? TaskTitle,
    DateOnly Date, decimal Hours, string? Note, uint Version);
public record SaveTimeEntryRequest(Guid ProjectId, Guid? TaskId, DateOnly Date, decimal Hours, string? Note, uint? Version = null);
public record ProjectHoursDto(Guid ProjectId, decimal TotalHours, List<PersonHoursDto> People, List<TimeEntryDto> Recent);
public record PersonHoursDto(Guid EmployeeId, string Name, decimal Hours);

/// <summary>
/// People record the hours they worked, per project and day (optionally per task). Each person edits only their own entries;
/// the project scope decides which projects they can log against. AI employees never log time: their effort is in their runs.
/// </summary>
public class TimesheetService(IAppDbContext db, ICurrentUser current, TimeProvider clock)
{
    public const int MaxDaysBack = 90;
    public const decimal Step = 0.25m;

    private async Task<EmployeeProfile> MeAsync(CancellationToken ct)
    {
        if (current.IsAgent || current.UserId is null) throw new ForbiddenException("Only people record working hours.");
        return await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.UserId == current.UserId && e.Kind == EmployeeKind.Human, ct)
            ?? throw new DomainException("time.no_employee", "Your account has no employee profile. Ask an administrator to link one.");
    }

    private IQueryable<TimeEntryDto> Query(IQueryable<TimeEntry> q) =>
        q.Join(db.Employees, t => t.EmployeeId, e => e.Id, (t, e) => new { t, e.DisplayName })
            .Join(db.Projects, x => x.t.ProjectId, p => p.Id, (x, p) => new { x.t, x.DisplayName, p.Code, p.Name })
            .Select(x => new TimeEntryDto(x.t.Id, x.t.EmployeeId, x.DisplayName, x.t.ProjectId, x.Code, x.Name, x.t.TaskId,
                db.Tasks.Where(k => k.Id == x.t.TaskId).Select(k => k.Title).FirstOrDefault(), x.t.Date, x.t.Hours, x.t.Note, x.t.Version));

    public async Task<List<TimeEntryDto>> MineAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var me = await MeAsync(ct);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var (f, t) = (from ?? today.AddDays(-13), to ?? today);
        var rows = await Query(db.TimeEntries.AsNoTracking().Where(x => x.EmployeeId == me.Id && x.Date >= f && x.Date <= t)).ToListAsync(ct);
        return rows.OrderByDescending(x => x.Date).ThenBy(x => x.ProjectCode, StringComparer.Ordinal).ToList();
    }

    public async Task<TimeEntryDto> CreateAsync(SaveTimeEntryRequest req, CancellationToken ct)
    {
        var me = await MeAsync(ct);
        var entry = new TimeEntry { EmployeeId = me.Id };
        await ApplyAsync(entry, req, ct);
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return await Query(db.TimeEntries.AsNoTracking().Where(x => x.Id == entry.Id)).FirstAsync(ct);
    }

    public async Task<TimeEntryDto> UpdateAsync(Guid id, SaveTimeEntryRequest req, CancellationToken ct)
    {
        var entry = await OwnAsync(id, ct);
        if (req.Version is { } v) db.ExpectVersion(entry, v);
        await ApplyAsync(entry, req, ct);
        await db.SaveChangesAsync(ct);
        return await Query(db.TimeEntries.AsNoTracking().Where(x => x.Id == entry.Id)).FirstAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        db.TimeEntries.Remove(await OwnAsync(id, ct));
        await db.SaveChangesAsync(ct);
    }

    private async Task<TimeEntry> OwnAsync(Guid id, CancellationToken ct)
    {
        var me = await MeAsync(ct);
        var entry = await db.TimeEntries.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("TimeEntry", id);
        if (entry.EmployeeId != me.Id) throw new ForbiddenException("You can change only your own time entries.");
        return entry;
    }

    private async Task ApplyAsync(TimeEntry entry, SaveTimeEntryRequest req, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (req.Date > today.AddDays(1)) throw new DomainException("validation", "Hours cannot be recorded for a future day.");
        if (req.Date < today.AddDays(-MaxDaysBack)) throw new DomainException("validation", $"Hours can be recorded for the last {MaxDaysBack} days only.");
        if (req.Hours <= 0 || req.Hours > 24 || req.Hours % Step != 0) throw new DomainException("validation", "hours must be between 0.25 and 24, in steps of 0.25.");
        if (req.Note is { Length: > 500 }) throw new DomainException("validation", "note must be at most 500 characters.");
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == req.ProjectId, ct) ?? throw new NotFoundException("Project", req.ProjectId);
        if (project.Status == ProjectStatus.Cancelled) throw new DomainException("validation", "The project is cancelled.");
        if (req.TaskId is { } taskId && !await db.Tasks.AnyAsync(t => t.Id == taskId && t.ProjectId == project.Id, ct))
            throw new DomainException("validation", "The task is not part of this project.");
        var sameDay = await db.TimeEntries.Where(x => x.EmployeeId == entry.EmployeeId && x.Date == req.Date && x.Id != entry.Id).SumAsync(x => (decimal?)x.Hours, ct) ?? 0m;
        if (sameDay + req.Hours > 24) throw new DomainException("validation", $"That makes more than 24 hours on {req.Date:yyyy-MM-dd}.");
        (entry.ProjectId, entry.TaskId, entry.Date, entry.Hours) = (project.Id, req.TaskId, req.Date, req.Hours);
        entry.Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
    }

    /// <summary>Everyone's hours on a project (who worked how much), for people who can read the project.</summary>
    public async Task<ProjectHoursDto> ProjectAsync(Guid projectId, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) throw new NotFoundException("Project", projectId);
        var all = await Query(db.TimeEntries.AsNoTracking().Where(x => x.ProjectId == projectId)).ToListAsync(ct);
        var people = all.GroupBy(x => (x.EmployeeId, x.EmployeeName)).Select(g => new PersonHoursDto(g.Key.EmployeeId, g.Key.EmployeeName, g.Sum(x => x.Hours)))
            .OrderByDescending(p => p.Hours).ToList();
        return new ProjectHoursDto(projectId, all.Sum(x => x.Hours), people, all.OrderByDescending(x => x.Date).Take(50).ToList());
    }

    /// <summary>Recorded hours and their cost at each person's position rate. Cost is null when anyone's position has no rate (never guessed).</summary>
    public static async Task<(decimal Hours, decimal? LabourThb, int People)> ActualAsync(IAppDbContext db, Guid projectId, CancellationToken ct)
    {
        var rows = await db.TimeEntries.AsNoTracking().Where(x => x.ProjectId == projectId)
            .Join(db.Employees, t => t.EmployeeId, e => e.Id, (t, e) => new { t.EmployeeId, t.Hours, e.RoleDefinitionId })
            .ToListAsync(ct);
        if (rows.Count == 0) return (0m, null, 0);
        var roles = rows.Select(r => r.RoleDefinitionId).Distinct().ToList();
        var rates = await db.RoleRates.AsNoTracking().Where(r => roles.Contains(r.RoleDefinitionId) && r.HourlyRateThb > 0)
            .ToDictionaryAsync(r => r.RoleDefinitionId, r => r.HourlyRateThb, ct);
        decimal? cost = rows.All(r => rates.ContainsKey(r.RoleDefinitionId))
            ? Math.Round(rows.Sum(r => r.Hours * rates[r.RoleDefinitionId]), 2, MidpointRounding.AwayFromZero) : null;
        return (rows.Sum(r => r.Hours), cost, rows.Select(r => r.EmployeeId).Distinct().Count());
    }
}
