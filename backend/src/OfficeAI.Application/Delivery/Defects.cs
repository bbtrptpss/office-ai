using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Application.Agents;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Delivery;
using OfficeAI.Domain.Organization;
using OfficeAI.Domain.Work;

namespace OfficeAI.Application.Delivery;

public record DefectDto(Guid Id, string Code, string Title, DefectSeverity Severity, DefectStatus Status, string State,
    string StepsToReproduce, string? ExpectedResult, string? ActualResult, string? Environment,
    Guid? TaskId, string? TaskTitle, Guid? TestRunId, Guid? FixTaskId, string? FixTaskTitle, WorkTaskStatus? FixTaskStatus, string? AssigneeName,
    string? ReporterName, bool ReportedByAi, int ReopenCount, string? ResolutionNote, DateTimeOffset CreatedAt, DateTimeOffset? ResolvedAt, uint Version);
public record ReportDefectRequest(string Title, string StepsToReproduce, DefectSeverity Severity = DefectSeverity.Medium, string? ExpectedResult = null,
    string? ActualResult = null, string? Environment = null, Guid? TaskId = null, Guid? TestRunId = null);
public record ResolveDefectRequest(string? Note = null, uint? Version = null);

/// <summary>Input shared by the people endpoint and the agent tool.</summary>
internal record DefectInput(string Title, string Steps, DefectSeverity Severity, string? Expected, string? Actual, string? Environment, Guid? TaskId, Guid? TestRunId);

/// <summary>
/// Defects found in QA (Master Prompt §7 stage 6): recorded with reproduction steps, sent back to the developer as a fix task,
/// verified or rejected by a person. A task with an open defect cannot be marked Done.
/// </summary>
public class DefectService(IAppDbContext db, ICurrentUser current, IRealtimeNotifier notifier, TimeProvider clock)
{
    public const int MaxPerProject = 1000;

    /// <summary>Open = still being fixed; fixReady = its fix task is Done and a person should verify it.</summary>
    public static string StateOf(DefectStatus status, WorkTaskStatus? fixStatus) => status switch
    {
        DefectStatus.Verified => "verified",
        DefectStatus.Rejected => "rejected",
        _ => fixStatus == WorkTaskStatus.Done ? "fixReady" : "open",
    };

    /// <summary>The quality gate: the affected task stays out of Done while one of its defects is open.</summary>
    public static async Task EnsureNoOpenDefectsAsync(IAppDbContext db, Guid taskId, CancellationToken ct)
    {
        var open = await db.Defects.Where(d => d.TaskId == taskId && d.Status == DefectStatus.Open).OrderBy(d => d.Code).Select(d => d.Code).ToListAsync(ct);
        if (open.Count > 0)
            throw new DomainException("task.open_defects", $"Open defects must be verified or rejected first: {string.Join(", ", open)}.");
    }

    internal static async Task<Defect> RecordAsync(IAppDbContext db, Guid projectId, DefectInput input, Guid? reporterEmployeeId, Guid? reporterUserId,
        Guid? runId, DateTimeOffset now, CancellationToken ct)
    {
        Validate.Required(input.Title, "title");
        Validate.Required(input.Steps, "stepsToReproduce");
        if (!Enum.IsDefined(input.Severity)) throw new DomainException("validation", "severity is invalid.");
        if (input.Title.Trim().Length > 200) throw new DomainException("validation", "title must be at most 200 characters.");
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) throw new NotFoundException("Project", projectId);
        if (await db.Defects.CountAsync(d => d.ProjectId == projectId, ct) >= MaxPerProject)
            throw new DomainException("defect.limit", $"A project can hold at most {MaxPerProject} defects.");

        WorkTask? source = null;
        if (input.TaskId is { } taskId)
            source = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId && t.ProjectId == projectId, ct)
                ?? throw new DomainException("validation", "taskId does not belong to this project.");
        if (input.TestRunId is { } testRunId && !await db.TestRuns.AnyAsync(t => t.Id == testRunId
                && db.CodeRepositories.Any(r => r.Id == t.RepositoryId && r.ProjectId == projectId), ct))
            throw new DomainException("validation", "testRunId does not belong to this project.");

        var defect = new Defect
        {
            ProjectId = projectId, Code = $"DEF-{await db.Defects.CountAsync(d => d.ProjectId == projectId, ct) + 1:000}",
            Title = input.Title.Trim(), Severity = input.Severity, StepsToReproduce = input.Steps.Trim(), ExpectedResult = input.Expected,
            ActualResult = input.Actual, Environment = input.Environment, TaskId = input.TaskId, TestRunId = input.TestRunId,
            ReportedByEmployeeId = reporterEmployeeId, ReportedByUserId = reporterUserId, RunId = runId,
        };
        db.Defects.Add(defect);
        await AddFixTaskAsync(db, defect, source, now, ct);
        return defect;
    }

    /// <summary>Sends the defect back to the developer: a new fix task, assigned to whoever last worked on the affected task.</summary>
    private static async Task AddFixTaskAsync(IAppDbContext db, Defect d, WorkTask? source, DateTimeOffset now, CancellationToken ct)
    {
        var title = $"Fix {d.Code}: {d.Title}";
        var fix = new WorkTask
        {
            ProjectId = d.ProjectId, Title = title.Length > 300 ? title[..300] : title, MilestoneId = source?.MilestoneId,
            Priority = Defect.PriorityFor(d.Severity), Complexity = source?.Complexity ?? TaskComplexity.Standard, RequiredSkills = source?.RequiredSkills ?? [],
            Description = string.Join("\n\n", new[]
            {
                source is null ? null : $"Found in: {source.Title}",
                $"Steps to reproduce:\n{d.StepsToReproduce}",
                d.ExpectedResult is { Length: > 0 } e ? $"Expected:\n{e}" : null,
                d.ActualResult is { Length: > 0 } a ? $"Actual:\n{a}" : null,
                d.Environment is { Length: > 0 } env ? $"Environment: {env}" : null,
            }.OfType<string>()),
            AcceptanceCriteria = $"Following the steps of {d.Code} no longer shows the defect" +
                (d.ExpectedResult is { Length: > 0 } x ? $"; the result is: {x}" : ".") + " Add or update a test that covers it where possible.",
        };
        db.Tasks.Add(fix);
        d.FixTaskId = fix.Id;

        if (source is null) return;
        var developer = await db.TaskAssignments.AsNoTracking().Where(a => a.TaskId == source.Id)
            .Join(db.Employees, a => a.EmployeeId, e => e.Id, (a, e) => new { a.AssignedAt, e.Id, e.Status })
            .Where(x => x.Status != EmployeeStatus.Inactive).OrderByDescending(x => x.AssignedAt).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        if (developer is not { } dev) return;
        db.TaskAssignments.Add(new TaskAssignment { TaskId = fix.Id, EmployeeId = dev, AssignedAt = now });
        fix.Status = WorkTaskStatus.Assigned;
    }

    public async Task<List<DefectDto>> ListAsync(Guid projectId, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) throw new NotFoundException("Project", projectId);
        var rows = await db.Defects.AsNoTracking().Where(d => d.ProjectId == projectId).OrderByDescending(d => d.CreatedAt).ToListAsync(ct);
        return await ToDtosAsync(rows, ct);
    }

    private async Task<DefectDto> GetAsync(Guid id, CancellationToken ct)
    {
        var d = await db.Defects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Defect", id);
        return (await ToDtosAsync([d], ct))[0];
    }

    private async Task<List<DefectDto>> ToDtosAsync(List<Defect> rows, CancellationToken ct)
    {
        var taskIds = rows.SelectMany(d => new[] { d.TaskId, d.FixTaskId }).OfType<Guid>().Distinct().ToList();
        var tasks = await db.Tasks.AsNoTracking().Where(t => taskIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => new { t.Title, t.Status }, ct);
        var activeAssignments = await db.TaskAssignments.AsNoTracking().Where(a => taskIds.Contains(a.TaskId) && a.UnassignedAt == null)
            .Select(a => new { a.TaskId, a.EmployeeId }).ToListAsync(ct);
        var employeeIds = activeAssignments.Select(a => a.EmployeeId).Concat(rows.Select(d => d.ReportedByEmployeeId).OfType<Guid>()).Distinct().ToList();
        var names = await db.Employees.AsNoTracking().Where(e => employeeIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.DisplayName, ct);
        var userIds = rows.Select(d => d.ReportedByUserId).OfType<Guid>().Distinct().ToList();
        var userNames = await db.Employees.AsNoTracking().Where(e => e.UserId != null && userIds.Contains(e.UserId.Value))
            .ToDictionaryAsync(e => e.UserId!.Value, e => e.DisplayName, ct);

        return rows.Select(d =>
        {
            var source = d.TaskId is { } s ? tasks.GetValueOrDefault(s) : null;
            var fix = d.FixTaskId is { } f ? tasks.GetValueOrDefault(f) : null;
            var assignee = activeAssignments.FirstOrDefault(a => a.TaskId == d.FixTaskId);
            var reporter = d.ReportedByEmployeeId is { } re ? names.GetValueOrDefault(re) : d.ReportedByUserId is { } ru ? userNames.GetValueOrDefault(ru) : null;
            return new DefectDto(d.Id, d.Code, d.Title, d.Severity, d.Status, StateOf(d.Status, fix?.Status), d.StepsToReproduce, d.ExpectedResult, d.ActualResult,
                d.Environment, d.TaskId, source?.Title, d.TestRunId, d.FixTaskId, fix?.Title, fix?.Status,
                assignee is null ? null : names.GetValueOrDefault(assignee.EmployeeId), reporter, d.ReportedByEmployeeId is not null,
                d.ReopenCount, d.ResolutionNote, d.CreatedAt, d.ResolvedAt, d.Version);
        }).ToList();
    }

    public async Task<DefectDto> ReportAsync(Guid projectId, ReportDefectRequest req, CancellationToken ct)
    {
        var d = await RecordAsync(db, projectId, new DefectInput(req.Title ?? "", req.StepsToReproduce ?? "", req.Severity, req.ExpectedResult, req.ActualResult,
            req.Environment, req.TaskId, req.TestRunId), null, current.UserId, null, clock.GetUtcNow(), ct);
        await db.SaveChangesAsync(ct);
        await NotifyAsync(d, ct);
        return await GetAsync(d.Id, ct);
    }

    /// <summary>A person confirms the fix works. Needs the fix task to be Done.</summary>
    public async Task<DefectDto> VerifyAsync(Guid id, ResolveDefectRequest req, CancellationToken ct)
    {
        var d = await LoadAsync(id, req.Version, ct);
        if (d.Status != DefectStatus.Open) throw new DomainException("defect.closed", $"{d.Code} is already {d.Status}.");
        if (d.FixTaskId is { } fixId && !await db.Tasks.AnyAsync(t => t.Id == fixId && t.Status == WorkTaskStatus.Done, ct))
            throw new DomainException("defect.fix_not_done", $"The fix task for {d.Code} is not Done yet.");
        return await CloseAsync(d, DefectStatus.Verified, req.Note, ct);
    }

    /// <summary>Not a defect, a duplicate or not reproducible. A fix task nobody started is cancelled with it.</summary>
    public async Task<DefectDto> RejectAsync(Guid id, ResolveDefectRequest req, CancellationToken ct)
    {
        Validate.Required(req.Note, "note");
        var d = await LoadAsync(id, req.Version, ct);
        if (d.Status != DefectStatus.Open) throw new DomainException("defect.closed", $"{d.Code} is already {d.Status}.");
        if (d.FixTaskId is { } fixId && await db.Tasks.FirstOrDefaultAsync(t => t.Id == fixId, ct) is { Status: WorkTaskStatus.Todo or WorkTaskStatus.Assigned } fix)
            fix.Status = WorkTaskStatus.Cancelled;
        return await CloseAsync(d, DefectStatus.Rejected, req.Note, ct);
    }

    /// <summary>The fix did not hold (after verification, or when checking a finished fix): open it again with a new fix task.</summary>
    public async Task<DefectDto> ReopenAsync(Guid id, ResolveDefectRequest req, CancellationToken ct)
    {
        Validate.Required(req.Note, "note");
        var d = await LoadAsync(id, req.Version, ct);
        var fixStatus = d.FixTaskId is { } fixId ? await db.Tasks.Where(t => t.Id == fixId).Select(t => (WorkTaskStatus?)t.Status).FirstOrDefaultAsync(ct) : null;
        if (StateOf(d.Status, fixStatus) is not ("verified" or "fixReady"))
            throw new DomainException("defect.not_reopenable", $"{d.Code} can be reopened only after its fix is done.");

        d.Status = DefectStatus.Open;
        d.ReopenCount++;
        d.ResolvedAt = null;
        d.ResolutionNote = req.Note!.Trim();
        var previousFix = d.FixTaskId is { } pf ? await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == pf, ct) : null;
        await AddFixTaskAsync(db, d, previousFix ?? (d.TaskId is { } s ? await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == s, ct) : null), clock.GetUtcNow(), ct);
        await db.SaveChangesAsync(ct);
        await NotifyAsync(d, ct);
        return await GetAsync(d.Id, ct);
    }

    private async Task<Defect> LoadAsync(Guid id, uint? version, CancellationToken ct)
    {
        var d = await db.Defects.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Defect", id);
        if (version is { } v) db.ExpectVersion(d, v);
        return d;
    }

    private async Task<DefectDto> CloseAsync(Defect d, DefectStatus status, string? note, CancellationToken ct)
    {
        d.Status = status;
        d.ResolutionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        d.ResolvedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await NotifyAsync(d, ct);
        return await GetAsync(d.Id, ct);
    }

    private async Task NotifyAsync(Defect d, CancellationToken ct)
    {
        if (d.FixTaskId is { } fix) await notifier.TaskChangedAsync(d.ProjectId, fix, ct);
        if (d.TaskId is { } task) await notifier.TaskChangedAsync(d.ProjectId, task, ct);
    }
}

/// <summary>QA records a reproducible defect; the system creates the fix task and assigns it back to the developer.</summary>
public sealed class ReportDefectTool : IAgentTool
{
    public const string ToolId = "defect.report";

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0",
        "Record a reproducible defect in this project: exact steps to reproduce, expected and actual result, severity, and the affected task id " +
        "(defaults to the task you are working on). The system creates a fix task for the developer; the affected task cannot be marked Done " +
        "until a person verifies the fix or rejects the defect. Only report what you observed in real tool results.",
        Schemas.Of(new
        {
            type = "object",
            properties = new
            {
                title = new { type = "string", maxLength = 200 },
                stepsToReproduce = new { type = "string", maxLength = 8000 },
                expectedResult = new { type = "string", maxLength = 4000 },
                actualResult = new { type = "string", maxLength = 4000 },
                environment = new { type = "string", maxLength = 500 },
                severity = new { type = "string", @enum = new[] { "Low", "Medium", "High", "Critical" } },
                taskId = new { type = "string", description = "Affected task id; omit to use the current task." },
                testRunId = new { type = "string", description = "Id of the failing test run that shows the defect, if any." },
            },
            required = new[] { "title", "stepsToReproduce", "actualResult" },
            additionalProperties = false,
        }),
        TimeSpan.FromSeconds(10), 1_000, RequiresApproval: false, HasSideEffects: true);

    public async Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct)
    {
        var severity = Enum.TryParse<DefectSeverity>(Schemas.Str(input, "severity"), true, out var s) && Enum.IsDefined(s) ? s : DefectSeverity.Medium;
        Guid? Id(string name) => Schemas.Str(input, name) is { } v ? Guid.TryParse(v, out var g) ? g : Guid.Empty : null;
        var taskId = Id("taskId") ?? ctx.TaskId;
        var testRunId = Id("testRunId");
        if (taskId == Guid.Empty || testRunId == Guid.Empty) return ToolResult.Fail("taskId and testRunId must be ids.");
        try
        {
            var d = await DefectService.RecordAsync(ctx.Db, ctx.ProjectId, new DefectInput(Schemas.Str(input, "title") ?? "", Schemas.Str(input, "stepsToReproduce") ?? "",
                severity, Schemas.Str(input, "expectedResult"), Schemas.Str(input, "actualResult"), Schemas.Str(input, "environment"), taskId, testRunId),
                ctx.EmployeeId, null, ctx.RunId, ctx.Clock.GetUtcNow(), ct);
            await ctx.Db.SaveChangesAsync(ct);
            return ToolResult.Ok(new { code = d.Code, severity = d.Severity.ToString(), fixTaskId = d.FixTaskId, status = "Open" });
        }
        catch (DomainException e)
        {
            return ToolResult.Fail(e.Message);
        }
    }
}
