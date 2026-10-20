using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Organization;
using OfficeAI.Domain.Work;

namespace OfficeAI.Application.Organization;

/// <summary>
/// An AI employee shows Failed after a run fails, and only their next run used to clear it. When the failed work goes
/// away instead — the task is cancelled or done, its project cancelled or completed — nothing is left to retry, so the
/// employee goes back to Available.
/// </summary>
public class StaleFailureService(IAppDbContext db, IRealtimeNotifier notifier)
{
    /// <summary>Returns how many employees were set back to Available.</summary>
    public async Task<int> ClearAsync(CancellationToken ct)
    {
        var failed = await db.Employees.Where(e => e.Status == EmployeeStatus.Failed).ToListAsync(ct);
        if (failed.Count == 0) return 0;
        var cleared = 0;
        foreach (var employee in failed)
        {
            var last = await db.AgentRuns.AsNoTracking().Where(r => r.EmployeeId == employee.Id)
                .OrderByDescending(r => r.CreatedAt).Select(r => new { r.Status, r.TaskId, r.ProjectId }).FirstOrDefaultAsync(ct);
            if (last is not null && last.Status == AgentRunStatus.Failed && await StillOpenAsync(last.TaskId, last.ProjectId, ct)) continue;
            employee.Status = EmployeeStatus.Available;
            cleared++;
        }
        if (cleared == 0) return 0;
        await db.SaveChangesAsync(ct);
        await notifier.OrganizationChangedAsync(ct);
        return cleared;
    }

    private async Task<bool> StillOpenAsync(Guid taskId, Guid projectId, CancellationToken ct)
    {
        var task = await db.Tasks.AsNoTracking().Where(t => t.Id == taskId).Select(t => (WorkTaskStatus?)t.Status).FirstOrDefaultAsync(ct);
        if (task is null or WorkTaskStatus.Done or WorkTaskStatus.Cancelled) return false;
        var project = await db.Projects.AsNoTracking().Where(p => p.Id == projectId).Select(p => (ProjectStatus?)p.Status).FirstOrDefaultAsync(ct);
        return project is not (null or ProjectStatus.Completed or ProjectStatus.Cancelled);
    }
}
