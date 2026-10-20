using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Application.Delivery;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Organization;
using OfficeAI.Domain.Work;
using OfficeAI.Domain.Workflows;

namespace OfficeAI.Application.Work;

public record ProjectDto(
    Guid Id, string Code, string Name, string? Description, ProjectStatus Status, Guid? OwnerEmployeeId,
    decimal? EstimatedBudget, DateOnly? StartDate, DateOnly? DueDate, int TaskCount, int DoneCount, uint Version);
public record SaveProjectRequest(
    string Code, string Name, string? Description, ProjectStatus Status = ProjectStatus.Draft, Guid? OwnerEmployeeId = null,
    decimal? EstimatedBudget = null, DateOnly? StartDate = null, DateOnly? DueDate = null, uint? Version = null);

public record MilestoneDto(Guid Id, Guid ProjectId, string Name, DateOnly? DueDate, int SortOrder, uint Version);
public record SaveMilestoneRequest(string Name, DateOnly? DueDate, int SortOrder = 0, uint? Version = null);

public record AssigneeDto(Guid EmployeeId, string DisplayName, EmployeeKind Kind);
public record TaskDto(
    Guid Id, Guid ProjectId, Guid? MilestoneId, string Title, string? Description, string? AcceptanceCriteria,
    WorkTaskStatus Status, TaskPriority Priority, TaskComplexity Complexity, DateOnly? DueDate, List<AssigneeDto> Assignees,
    List<Guid> DependsOn, List<WorkTaskStatus> NextStates, uint Version);
public record SaveTaskRequest(
    string Title, string? Description = null, string? AcceptanceCriteria = null, Guid? MilestoneId = null,
    TaskPriority Priority = TaskPriority.Medium, DateOnly? DueDate = null, uint? Version = null, TaskComplexity Complexity = TaskComplexity.Standard);
public record ChangeStatusRequest(WorkTaskStatus Status, uint? Version = null);
public record AssignRequest(Guid EmployeeId, uint? Version = null);
public record AddDependencyRequest(Guid DependsOnTaskId);

public record DashboardDto(
    int ActiveProjects, int TotalTasks, int CompletedTasks, int BlockedTasks, int OverdueTasks, int WaitingApproval,
    int HumanEmployees, int AiEmployees, int AiAvailable, int AiWorking, List<WorkloadDto> Workload);
public record WorkloadDto(Guid EmployeeId, string DisplayName, EmployeeKind Kind, int OpenTasks);
public record RecentTaskDto(Guid Id, Guid ProjectId, string ProjectName, string Title, WorkTaskStatus Status, TaskPriority Priority,
    string? AssigneeName, DateOnly? DueDate, DateTimeOffset ChangedAt);
public record BoardTaskDto(TaskDto Task, string ProjectCode, string ProjectName, bool IsOverdue);
public record TaskBoardQuery(Guid? ProjectId = null, Guid? EmployeeId = null, TaskPriority? Priority = null, bool OverdueOnly = false);

public class WorkService(IAppDbContext db, IRealtimeNotifier notifier, TimeProvider clock, IAgentRunQueue queue)
{
    // ---------- Projects ----------

    public async Task<List<ProjectDto>> ListProjectsAsync(CancellationToken ct) =>
        await db.Projects.AsNoTracking().OrderByDescending(p => p.CreatedAt)
            .Select(p => new ProjectDto(p.Id, p.Code, p.Name, p.Description, p.Status, p.OwnerEmployeeId, p.EstimatedBudget,
                p.StartDate, p.DueDate,
                db.Tasks.Count(t => t.ProjectId == p.Id && t.Status != WorkTaskStatus.Cancelled),
                db.Tasks.Count(t => t.ProjectId == p.Id && t.Status == WorkTaskStatus.Done),
                p.Version))
            .ToListAsync(ct);

    public async Task<ProjectDto> GetProjectAsync(Guid id, CancellationToken ct) =>
        (await ListQuery(id).FirstOrDefaultAsync(ct)) ?? throw new NotFoundException("Project", id);

    private IQueryable<ProjectDto> ListQuery(Guid id) =>
        db.Projects.AsNoTracking().Where(p => p.Id == id)
            .Select(p => new ProjectDto(p.Id, p.Code, p.Name, p.Description, p.Status, p.OwnerEmployeeId, p.EstimatedBudget,
                p.StartDate, p.DueDate,
                db.Tasks.Count(t => t.ProjectId == p.Id && t.Status != WorkTaskStatus.Cancelled),
                db.Tasks.Count(t => t.ProjectId == p.Id && t.Status == WorkTaskStatus.Done),
                p.Version));

    public async Task<ProjectDto> CreateProjectAsync(SaveProjectRequest req, CancellationToken ct)
    {
        var project = new Project { Code = "", Name = "" };
        await ApplyAsync(project, req, ct);
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        return await GetProjectAsync(project.Id, ct);
    }

    public async Task<ProjectDto> UpdateProjectAsync(Guid id, SaveProjectRequest req, CancellationToken ct)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Project", id);
        if (req.Version is { } v) db.ExpectVersion(project, v);
        var wasCancelled = project.Status == ProjectStatus.Cancelled;
        await ApplyAsync(project, req, ct);
        var stop = !wasCancelled && project.Status == ProjectStatus.Cancelled ? await CancelOpenWorkAsync(id, ct) : null;
        await db.SaveChangesAsync(ct);
        if (stop is not null) await AfterCancelAsync(id, stop, ct);
        return await GetProjectAsync(id, ct);
    }

    private sealed record CancelledWork(List<WorkTask> Tasks, List<AgentRun> Runs, bool Approvals);

    /// <summary>
    /// A cancelled project leaves nothing running: its open tasks are cancelled, AI runs on them are asked to stop and
    /// pending approvals are withdrawn. Done tasks and finished runs stay as they are. Saved with the project change.
    /// </summary>
    private async Task<CancelledWork> CancelOpenWorkAsync(Guid projectId, CancellationToken ct)
    {
        var tasks = await db.Tasks.Where(t => t.ProjectId == projectId && t.Status != WorkTaskStatus.Done && t.Status != WorkTaskStatus.Cancelled).ToListAsync(ct);
        foreach (var t in tasks) t.Status = WorkTaskStatus.Cancelled;
        var runs = await db.AgentRuns.Where(r => r.ProjectId == projectId && (r.Status == AgentRunStatus.Queued || r.Status == AgentRunStatus.Running)).ToListAsync(ct);
        foreach (var r in runs) r.CancelRequested = true;
        var now = clock.GetUtcNow();
        var approvals = await db.ApprovalRequests.Where(a => a.ProjectId == projectId && a.Status == ApprovalStatus.Pending).ToListAsync(ct);
        foreach (var a in approvals)
        {
            a.Status = ApprovalStatus.Cancelled;
            a.DecidedAt = now;
            a.DecisionNote = "Project cancelled";
        }
        return new CancelledWork(tasks, runs, approvals.Count > 0);
    }

    private async Task AfterCancelAsync(Guid projectId, CancelledWork stop, CancellationToken ct)
    {
        foreach (var r in stop.Runs)
        {
            queue.Cancel(r.Id);
            await notifier.AgentRunChangedAsync(r.Id, r.TaskId, projectId, r.Status.ToString(), ct);
        }
        foreach (var t in stop.Tasks) await notifier.TaskChangedAsync(projectId, t.Id, ct);
        if (stop.Approvals) await notifier.ApprovalsChangedAsync(ct);
    }

    private async Task ApplyAsync(Project p, SaveProjectRequest r, CancellationToken ct)
    {
        Validate.Required(r.Code, "code");
        Validate.Required(r.Name, "name");
        if (!Enum.IsDefined(r.Status)) throw new DomainException("validation", "status is invalid.");
        if (r.EstimatedBudget is < 0) throw new DomainException("validation", "estimatedBudget cannot be negative.");
        if (r.StartDate is { } s && r.DueDate is { } d && d < s) throw new DomainException("validation", "dueDate is before startDate.");
        if (r.OwnerEmployeeId is { } owner && !await db.Employees.AnyAsync(e => e.Id == owner, ct))
            throw new NotFoundException("Employee", owner);

        p.Code = r.Code.Trim();
        p.Name = r.Name.Trim();
        p.Description = r.Description;
        p.Status = r.Status;
        p.OwnerEmployeeId = r.OwnerEmployeeId;
        p.EstimatedBudget = r.EstimatedBudget;
        p.StartDate = r.StartDate;
        p.DueDate = r.DueDate;
    }

    // ---------- Milestones ----------

    public async Task<List<MilestoneDto>> ListMilestonesAsync(Guid projectId, CancellationToken ct) =>
        await db.Milestones.AsNoTracking().Where(m => m.ProjectId == projectId).OrderBy(m => m.SortOrder).ThenBy(m => m.DueDate)
            .Select(m => new MilestoneDto(m.Id, m.ProjectId, m.Name, m.DueDate, m.SortOrder, m.Version))
            .ToListAsync(ct);

    public async Task<MilestoneDto> CreateMilestoneAsync(Guid projectId, SaveMilestoneRequest req, CancellationToken ct)
    {
        Validate.Required(req.Name, "name");
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) throw new NotFoundException("Project", projectId);
        var m = new Milestone { ProjectId = projectId, Name = req.Name.Trim(), DueDate = req.DueDate, SortOrder = req.SortOrder };
        db.Milestones.Add(m);
        await db.SaveChangesAsync(ct);
        return new MilestoneDto(m.Id, m.ProjectId, m.Name, m.DueDate, m.SortOrder, m.Version);
    }

    // ---------- Tasks ----------

    public async Task<List<TaskDto>> ListTasksAsync(Guid projectId, CancellationToken ct)
    {
        var tasks = await db.Tasks.AsNoTracking().Where(t => t.ProjectId == projectId).OrderBy(t => t.CreatedAt).ToListAsync(ct);
        return await ToDtosAsync(tasks, ct);
    }

    public async Task<TaskDto> GetTaskAsync(Guid id, CancellationToken ct)
    {
        var task = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Task", id);
        return (await ToDtosAsync([task], ct))[0];
    }

    private async Task<List<TaskDto>> ToDtosAsync(List<WorkTask> tasks, CancellationToken ct)
    {
        var ids = tasks.Select(t => t.Id).ToList();
        var assignees = await db.TaskAssignments.AsNoTracking()
            .Where(a => ids.Contains(a.TaskId) && a.UnassignedAt == null)
            .Join(db.Employees, a => a.EmployeeId, e => e.Id, (a, e) => new { a.TaskId, e.Id, e.DisplayName, e.Kind })
            .ToListAsync(ct);
        var deps = await db.TaskDependencies.AsNoTracking().Where(d => ids.Contains(d.TaskId)).ToListAsync(ct);

        return tasks.Select(t => new TaskDto(
            t.Id, t.ProjectId, t.MilestoneId, t.Title, t.Description, t.AcceptanceCriteria, t.Status, t.Priority, t.Complexity, t.DueDate,
            assignees.Where(a => a.TaskId == t.Id).Select(a => new AssigneeDto(a.Id, a.DisplayName, a.Kind)).ToList(),
            deps.Where(d => d.TaskId == t.Id).Select(d => d.DependsOnTaskId).ToList(),
            [.. TaskStateMachine.NextStates(t.Status)],
            t.Version)).ToList();
    }

    public async Task<TaskDto> CreateTaskAsync(Guid projectId, SaveTaskRequest req, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) throw new NotFoundException("Project", projectId);
        var task = new WorkTask { ProjectId = projectId, Title = "" };
        await ApplyAsync(task, req, ct);
        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);
        await notifier.TaskChangedAsync(projectId, task.Id, ct);
        return await GetTaskAsync(task.Id, ct);
    }

    public async Task<TaskDto> UpdateTaskAsync(Guid id, SaveTaskRequest req, CancellationToken ct)
    {
        var task = await LoadTaskAsync(id, req.Version, ct);
        await ApplyAsync(task, req, ct);
        await db.SaveChangesAsync(ct);
        await notifier.TaskChangedAsync(task.ProjectId, task.Id, ct);
        return await GetTaskAsync(id, ct);
    }

    private async Task ApplyAsync(WorkTask t, SaveTaskRequest r, CancellationToken ct)
    {
        Validate.Required(r.Title, "title");
        if (!Enum.IsDefined(r.Priority)) throw new DomainException("validation", "priority is invalid.");
        if (!Enum.IsDefined(r.Complexity)) throw new DomainException("validation", "complexity is invalid.");
        if (r.MilestoneId is { } m && !await db.Milestones.AnyAsync(x => x.Id == m && x.ProjectId == t.ProjectId, ct))
            throw new DomainException("validation", "milestoneId does not belong to this project.");
        t.Title = r.Title.Trim();
        t.Description = r.Description;
        t.AcceptanceCriteria = r.AcceptanceCriteria;
        t.MilestoneId = r.MilestoneId;
        t.Priority = r.Priority;
        t.Complexity = r.Complexity;
        t.DueDate = r.DueDate;
    }

    /// <param name="canApprove">Caller holds the approver permission; required to move a task to Done.</param>
    public async Task<TaskDto> ChangeStatusAsync(Guid id, ChangeStatusRequest req, bool canApprove, CancellationToken ct)
    {
        var task = await LoadTaskAsync(id, req.Version, ct);
        var to = req.Status;
        if (!Enum.IsDefined(to)) throw new DomainException("validation", "status is invalid.");
        if (!TaskStateMachine.CanTransition(task.Status, to))
            throw new DomainException("task.invalid_transition", $"A task cannot move from {task.Status} to {to}.");
        if (TaskStateMachine.RequiresApprover(task.Status, to) && !canApprove)
            throw new ForbiddenException("Only an approver can mark a task as Done.");
        if (to == WorkTaskStatus.Done) await DefectService.EnsureNoOpenDefectsAsync(db, id, ct);

        if (to == WorkTaskStatus.InProgress)
        {
            var open = await db.TaskDependencies.Where(d => d.TaskId == id)
                .Join(db.Tasks, d => d.DependsOnTaskId, t => t.Id, (d, t) => t)
                .Where(t => t.Status != WorkTaskStatus.Done)
                .Select(t => t.Title).ToListAsync(ct);
            if (open.Count > 0)
                throw new DomainException("task.dependencies_open", $"Waiting on unfinished tasks: {string.Join(", ", open)}.");
            if (!await db.TaskAssignments.AnyAsync(a => a.TaskId == id && a.UnassignedAt == null, ct))
                throw new DomainException("task.unassigned", "Assign the task before starting it.");
        }

        if (to == WorkTaskStatus.Todo)
        {
            // Back to the pool: close current assignments.
            var now = clock.GetUtcNow();
            foreach (var a in await db.TaskAssignments.Where(a => a.TaskId == id && a.UnassignedAt == null).ToListAsync(ct))
                a.UnassignedAt = now;
        }

        task.Status = to;
        await db.SaveChangesAsync(ct);
        await notifier.TaskChangedAsync(task.ProjectId, task.Id, ct);
        return await GetTaskAsync(id, ct);
    }

    public async Task<TaskDto> AssignAsync(Guid id, AssignRequest req, CancellationToken ct)
    {
        var task = await LoadTaskAsync(id, req.Version, ct);
        if (task.Status is WorkTaskStatus.Done or WorkTaskStatus.Cancelled)
            throw new DomainException("task.closed", "A closed task cannot be reassigned.");
        var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == req.EmployeeId, ct)
            ?? throw new NotFoundException("Employee", req.EmployeeId);
        if (employee.Status == EmployeeStatus.Inactive)
            throw new DomainException("employee.inactive", $"{employee.DisplayName} is inactive.");

        var now = clock.GetUtcNow();
        var current = await db.TaskAssignments.Where(a => a.TaskId == id && a.UnassignedAt == null).ToListAsync(ct);
        if (current.Any(a => a.EmployeeId == req.EmployeeId)) return await GetTaskAsync(id, ct);
        foreach (var a in current) a.UnassignedAt = now;

        db.TaskAssignments.Add(new TaskAssignment { TaskId = id, EmployeeId = req.EmployeeId, AssignedAt = now });
        if (task.Status == WorkTaskStatus.Todo) task.Status = WorkTaskStatus.Assigned;
        else task.UpdatedAt = now; // touch the task so concurrent edits conflict

        await db.SaveChangesAsync(ct);
        await notifier.TaskChangedAsync(task.ProjectId, task.Id, ct);
        return await GetTaskAsync(id, ct);
    }

    public async Task<TaskDto> AddDependencyAsync(Guid id, AddDependencyRequest req, CancellationToken ct)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Task", id);
        var other = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == req.DependsOnTaskId, ct)
            ?? throw new NotFoundException("Task", req.DependsOnTaskId);
        if (other.ProjectId != task.ProjectId)
            throw new DomainException("task.cross_project", "Dependencies must be within the same project.");
        if (id == req.DependsOnTaskId)
            throw new DomainException("task.dependency_cycle", "A task cannot depend on itself.");
        if (await db.TaskDependencies.AnyAsync(d => d.TaskId == id && d.DependsOnTaskId == req.DependsOnTaskId, ct))
            return await GetTaskAsync(id, ct);

        var edges = await db.TaskDependencies.AsNoTracking()
            .Where(d => db.Tasks.Any(t => t.Id == d.TaskId && t.ProjectId == task.ProjectId)).ToListAsync(ct);
        if (Graph.WouldCreateDependencyCycle(id, req.DependsOnTaskId, edges.ToLookup(e => e.TaskId, e => e.DependsOnTaskId)))
            throw new DomainException("task.dependency_cycle", "This dependency would create a loop.");

        db.TaskDependencies.Add(new TaskDependency { TaskId = id, DependsOnTaskId = req.DependsOnTaskId, CreatedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct);
        await notifier.TaskChangedAsync(task.ProjectId, id, ct);
        return await GetTaskAsync(id, ct);
    }

    public async Task<TaskDto> RemoveDependencyAsync(Guid id, Guid dependsOnTaskId, CancellationToken ct)
    {
        var dep = await db.TaskDependencies.FirstOrDefaultAsync(d => d.TaskId == id && d.DependsOnTaskId == dependsOnTaskId, ct);
        if (dep is not null)
        {
            db.TaskDependencies.Remove(dep);
            await db.SaveChangesAsync(ct);
            var projectId = await db.Tasks.Where(t => t.Id == id).Select(t => t.ProjectId).FirstAsync(ct);
            await notifier.TaskChangedAsync(projectId, id, ct);
        }
        return await GetTaskAsync(id, ct);
    }

    private async Task<WorkTask> LoadTaskAsync(Guid id, uint? version, CancellationToken ct)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Task", id);
        if (version is { } v) db.ExpectVersion(task, v);
        return task;
    }

    // ---------- Company task board ----------

    public const int BoardLimit = 500;
    public const int BoardDoneDays = 14;

    /// <summary>
    /// Tasks across all open projects for the company-wide board: cancelled tasks are left out and done tasks only
    /// show for the last <see cref="BoardDoneDays"/> days. Overdue = past the due date and not done.
    /// </summary>
    public async Task<List<BoardTaskDto>> BoardAsync(TaskBoardQuery q, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var doneSince = now.AddDays(-BoardDoneDays);
        var tasks = db.Tasks.AsNoTracking()
            .Where(t => db.Projects.Any(p => p.Id == t.ProjectId && p.Status != ProjectStatus.Cancelled && p.Status != ProjectStatus.Completed))
            .Where(t => t.Status != WorkTaskStatus.Cancelled && (t.Status != WorkTaskStatus.Done || (t.UpdatedAt ?? t.CreatedAt) >= doneSince));
        if (q.ProjectId is { } pid) tasks = tasks.Where(t => t.ProjectId == pid);
        if (q.EmployeeId is { } eid) tasks = tasks.Where(t => db.TaskAssignments.Any(a => a.TaskId == t.Id && a.EmployeeId == eid && a.UnassignedAt == null));
        if (q.Priority is { } pr) tasks = tasks.Where(t => t.Priority == pr);
        if (q.OverdueOnly) tasks = tasks.Where(t => t.DueDate != null && t.DueDate < today && t.Status != WorkTaskStatus.Done);

        var list = await tasks.OrderByDescending(TaskOrdering.PriorityRank).ThenBy(t => t.DueDate == null).ThenBy(t => t.DueDate).ThenBy(t => t.CreatedAt)
            .Take(BoardLimit).ToListAsync(ct);
        var projectIds = list.Select(t => t.ProjectId).Distinct().ToList();
        var projects = await db.Projects.AsNoTracking().Where(p => projectIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => new { p.Code, p.Name }, ct);
        return (await ToDtosAsync(list, ct)).Select(t => new BoardTaskDto(t, projects[t.ProjectId].Code, projects[t.ProjectId].Name,
            t.DueDate is { } d && d < today && t.Status != WorkTaskStatus.Done)).ToList();
    }

    // ---------- Dashboard ----------

    /// <summary>The most recently changed tasks across open projects, for the dashboard's task list.</summary>
    public async Task<List<RecentTaskDto>> RecentTasksAsync(int take, CancellationToken ct) =>
        await db.Tasks.AsNoTracking()
            .Where(t => db.Projects.Any(p => p.Id == t.ProjectId && p.Status != ProjectStatus.Cancelled && p.Status != ProjectStatus.Completed))
            .OrderByDescending(t => t.UpdatedAt ?? t.CreatedAt).Take(Math.Clamp(take, 1, 50))
            .Select(t => new RecentTaskDto(t.Id, t.ProjectId, db.Projects.Where(p => p.Id == t.ProjectId).Select(p => p.Name).First(), t.Title, t.Status, t.Priority,
                db.TaskAssignments.Where(a => a.TaskId == t.Id && a.UnassignedAt == null)
                    .Join(db.Employees, a => a.EmployeeId, e => e.Id, (a, e) => e.DisplayName).FirstOrDefault(),
                t.DueDate, t.UpdatedAt ?? t.CreatedAt))
            .ToListAsync(ct);

    public async Task<DashboardDto> GetDashboardAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var closedStatuses = new[] { WorkTaskStatus.Done, WorkTaskStatus.Cancelled };
        var tasks = db.Tasks.AsNoTracking().Where(t => t.Status != WorkTaskStatus.Cancelled);

        var workload = (await db.TaskAssignments.AsNoTracking()
                .Where(a => a.UnassignedAt == null)
                .Join(db.Tasks.Where(t => !closedStatuses.Contains(t.Status)), a => a.TaskId, t => t.Id, (a, t) => a.EmployeeId)
                .GroupBy(employeeId => employeeId)
                .Select(g => new { EmployeeId = g.Key, OpenTasks = g.Count() })
                .OrderByDescending(x => x.OpenTasks).Take(10)
                .Join(db.Employees, x => x.EmployeeId, e => e.Id, (x, e) => new { x.OpenTasks, e.Id, e.DisplayName, e.Kind })
                .ToListAsync(ct))
            .OrderByDescending(x => x.OpenTasks)
            .Select(x => new WorkloadDto(x.Id, x.DisplayName, x.Kind, x.OpenTasks))
            .ToList();

        return new DashboardDto(
            await db.Projects.CountAsync(p => p.Status == ProjectStatus.Active, ct),
            await tasks.CountAsync(ct),
            await tasks.CountAsync(t => t.Status == WorkTaskStatus.Done, ct),
            await tasks.CountAsync(t => t.Status == WorkTaskStatus.Blocked, ct),
            await tasks.CountAsync(t => t.DueDate < today && !closedStatuses.Contains(t.Status), ct),
            await tasks.CountAsync(t => t.Status == WorkTaskStatus.WaitingApproval, ct),
            await db.Employees.CountAsync(e => e.Kind == EmployeeKind.Human, ct),
            await db.Employees.CountAsync(e => e.Kind == EmployeeKind.AI, ct),
            await db.Employees.CountAsync(e => e.Kind == EmployeeKind.AI && e.Status == EmployeeStatus.Available, ct),
            await db.Employees.CountAsync(e => e.Kind == EmployeeKind.AI && e.Status == EmployeeStatus.Working, ct),
            workload);
    }
}
