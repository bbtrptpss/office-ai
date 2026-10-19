using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Work;
using OfficeAI.Domain.Workflows;

namespace OfficeAI.Application.Organization;

public record OverviewTaskDto(Guid Id, Guid ProjectId, string ProjectCode, string Title, WorkTaskStatus Status, TaskPriority Priority, DateOnly? DueDate);
public record OverviewDeliverableDto(Guid Id, Guid TaskId, Guid ProjectId, string Title, DeliverableStatus Status, bool IsSimulated, DateTimeOffset CreatedAt);
public record EmployeeQualityDto(int Runs, int Succeeded, int Failed, decimal SuccessRate, decimal CostUsd, long Tokens, int TasksDone,
    int ReviewsApproved, int ChangesRequested, decimal FirstPassApprovalRate, int DeliverablesAccepted, int DeliverablesRejected);
public record EmployeeOverviewDto(Guid EmployeeId, Guid? ManagerId, string? ManagerName, string? RoleDescription, List<string> Responsibilities,
    List<string> Skills, List<OverviewTaskDto> Assignments, List<OverviewDeliverableDto> Deliverables, EmployeeQualityDto Quality);

/// <summary>
/// The profile view of an employee (Master Prompt §11): role, manager, skills, current assignments, deliverables and
/// all-time quality figures. Projects the caller can't see are left out by the project scope, so totals cover visible work.
/// </summary>
public class EmployeeOverviewService(IAppDbContext db)
{
    public async Task<EmployeeOverviewDto> GetAsync(Guid employeeId, CancellationToken ct)
    {
        var e = await db.Employees.AsNoTracking().Where(x => x.Id == employeeId)
            .Select(x => new
            {
                x.ManagerId, ManagerName = x.Manager != null ? x.Manager.DisplayName : null,
                x.RoleDefinition!.Description, x.RoleDefinition.Responsibilities, x.RoleDefinition.Skills,
            })
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Employee", employeeId);

        var myTaskIds = db.TaskAssignments.Where(a => a.EmployeeId == employeeId && a.UnassignedAt == null).Select(a => a.TaskId);
        // Priority is stored as text, so order in memory to get Critical → Low rather than alphabetical.
        var assignments = (await db.Tasks.AsNoTracking()
            .Where(t => myTaskIds.Contains(t.Id) && t.Status != WorkTaskStatus.Done && t.Status != WorkTaskStatus.Cancelled)
            .Select(t => new OverviewTaskDto(t.Id, t.ProjectId, t.Project!.Code, t.Title, t.Status, t.Priority, t.DueDate))
            .ToListAsync(ct))
            .OrderByDescending(t => t.Priority).ThenBy(t => t.DueDate ?? DateOnly.MaxValue).Take(50).ToList();

        var deliverables = await db.Deliverables.AsNoTracking().Where(d => d.EmployeeId == employeeId)
            .Join(db.Tasks, d => d.TaskId, t => t.Id, (d, t) => new { d, t.ProjectId })
            .OrderByDescending(x => x.d.CreatedAt).Take(10)
            .Select(x => new OverviewDeliverableDto(x.d.Id, x.d.TaskId, x.ProjectId, x.d.Title, x.d.Status, x.d.IsSimulated, x.d.CreatedAt))
            .ToListAsync(ct);
        var deliverableStatuses = await db.Deliverables.AsNoTracking().Where(d => d.EmployeeId == employeeId).Select(d => d.Status).ToListAsync(ct);

        var runs = await db.AgentRuns.AsNoTracking().Where(r => r.EmployeeId == employeeId)
            .Select(r => new { r.Status, r.CostUsd, Tokens = r.InputTokens + r.OutputTokens }).ToListAsync(ct);
        // Same definitions as the monthly performance report, over all time.
        var reviews = await db.Reviews.AsNoTracking().Where(r => db.TaskAssignments.Any(a => a.TaskId == r.TaskId && a.EmployeeId == employeeId))
            .Select(r => new { r.TaskId, r.Outcome }).ToListAsync(ct);
        var reviewedTasks = reviews.GroupBy(r => r.TaskId).ToList();
        var tasksDone = await db.Tasks.CountAsync(t => t.Status == WorkTaskStatus.Done && myTaskIds.Contains(t.Id), ct);
        var succeeded = runs.Count(r => r.Status == AgentRunStatus.Succeeded);

        var quality = new EmployeeQualityDto(runs.Count, succeeded, runs.Count(r => r.Status is AgentRunStatus.Failed or AgentRunStatus.LimitReached),
            runs.Count == 0 ? 0 : Math.Round(100m * succeeded / runs.Count, 1), runs.Sum(r => r.CostUsd), runs.Sum(r => r.Tokens), tasksDone,
            reviews.Count(r => r.Outcome == ReviewOutcome.Approved), reviews.Count(r => r.Outcome == ReviewOutcome.ChangesRequested),
            reviewedTasks.Count == 0 ? 0 : Math.Round(100m * reviewedTasks.Count(g => g.All(r => r.Outcome == ReviewOutcome.Approved)) / reviewedTasks.Count, 1),
            deliverableStatuses.Count(s => s == DeliverableStatus.Accepted), deliverableStatuses.Count(s => s == DeliverableStatus.Rejected));

        return new EmployeeOverviewDto(employeeId, e.ManagerId, e.ManagerName, e.Description, e.Responsibilities, e.Skills, assignments, deliverables, quality);
    }
}
