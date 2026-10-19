using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Application.Agents;
using OfficeAI.Application.Code;
using OfficeAI.Application.Workflows;
using OfficeAI.Domain.Code;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Delivery;
using OfficeAI.Domain.Work;
using OfficeAI.Domain.Workflows;

namespace OfficeAI.Application.Delivery;

/// <param name="Blocking">A blocking check must be met before the release can go to approval; the others are shown as warnings.</param>
public record ReadinessCheckDto(string Code, bool Met, bool Blocking, string? Detail = null);
public record ReleaseReadinessDto(bool Ready, string? RepositoryName, string? Branch, string? CommitSha, Guid? TestRunId, List<ReadinessCheckDto> Checks, List<string> UnresolvedIssues);
public record ReleaseDto(Guid Id, string Tag, string? Title, string? Notes, string? DeploymentPlan, string? RollbackPlan, string? KnownIssues, ReleaseStatus Status,
    string? CommitSha, Guid? TestRunId, List<string> UnresolvedIssues, Guid? ApprovalRequestId, string? AuthorName, DateTimeOffset CreatedAt,
    DateTimeOffset? ApprovedAt, DateTimeOffset? DeployedAt, DateTimeOffset? RolledBackAt, string? OutcomeNote, uint Version);
public record SaveReleaseRequest(string Tag, string? Title = null, string? Notes = null, string? DeploymentPlan = null, string? RollbackPlan = null,
    string? KnownIssues = null, uint? Version = null);
public record ReleaseOutcomeRequest(string? Note = null);

/// <summary>
/// Release preparation (Master Prompt §7 stage 7): notes, deployment instructions and a rollback plan; a deterministic readiness check
/// (passing tests on the exact commit, open defects, blocked tasks, open merge requests); human approval before production; people
/// record the deployment and any rollback. Nothing here deploys anything.
/// </summary>
public class ReleaseService(IAppDbContext db, ICurrentUser current, ICodeRepositoryStore code, TimeProvider clock, IRealtimeNotifier notifier)
{
    /// <summary>Tip of the repository's default branch, or null when it has none yet.</summary>
    public static async Task<string?> DefaultHeadAsync(ICodeRepositoryStore code, CodeRepository repo, CancellationToken ct)
    {
        var layout = (IRepositoryLayout)code;
        var r = await code.GitAsync(Path.GetFullPath("."), ["--git-dir", layout.OriginPath(repo), "rev-parse", "--verify", "--quiet", $"refs/heads/{repo.DefaultBranch}"], ct);
        var sha = r.Output.Trim();
        return r.Ok && sha.Length >= 40 ? sha : null;
    }

    public async Task<List<ReleaseDto>> ListAsync(Guid projectId, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) throw new NotFoundException("Project", projectId);
        var rows = await db.Releases.AsNoTracking().Where(r => r.ProjectId == projectId).OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
        var authors = rows.Select(r => r.AuthorEmployeeId).OfType<Guid>().Distinct().ToList();
        var names = await db.Employees.AsNoTracking().Where(e => authors.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.DisplayName, ct);
        return rows.Select(r => ToDto(r, r.AuthorEmployeeId is { } a ? names.GetValueOrDefault(a) : null)).ToList();
    }

    private static ReleaseDto ToDto(Release r, string? author) => new(r.Id, r.Tag, r.Title, r.Notes, r.DeploymentPlan, r.RollbackPlan, r.KnownIssues, r.Status,
        r.CommitSha, r.TestRunId, r.UnresolvedIssues, r.ApprovalRequestId, author, r.CreatedAt, r.ApprovedAt, r.DeployedAt, r.RolledBackAt, r.OutcomeNote, r.Version);

    private async Task<ReleaseDto> GetAsync(Guid id, CancellationToken ct) =>
        (await ListAsync((await db.Releases.AsNoTracking().FirstAsync(r => r.Id == id, ct)).ProjectId, ct)).First(r => r.Id == id);

    internal static void ValidateRequest(SaveReleaseRequest req)
    {
        OfficeAI.Application.Validate.Required(req.Tag, "tag");
        if (req.Tag.Trim().Length > 40) throw new DomainException("validation", "tag must be at most 40 characters.");
        if (req.Title is { Length: > 200 }) throw new DomainException("validation", "title must be at most 200 characters.");
    }

    internal static void Apply(Release r, SaveReleaseRequest req)
    {
        r.Tag = req.Tag.Trim();
        r.Title = req.Title?.Trim();
        r.Notes = req.Notes;
        r.DeploymentPlan = req.DeploymentPlan;
        r.RollbackPlan = req.RollbackPlan;
        r.KnownIssues = req.KnownIssues;
    }

    internal static async Task EnsureTagFreeAsync(IAppDbContext db, Guid projectId, string tag, Guid? except, CancellationToken ct)
    {
        if (await db.Releases.AnyAsync(r => r.ProjectId == projectId && r.Tag == tag.Trim() && r.Id != except, ct))
            throw new DomainException("release.version_taken", $"Release {tag.Trim()} already exists in this project.");
    }

    public async Task<ReleaseDto> CreateAsync(Guid projectId, SaveReleaseRequest req, CancellationToken ct)
    {
        ValidateRequest(req);
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) throw new NotFoundException("Project", projectId);
        await EnsureTagFreeAsync(db, projectId, req.Tag, null, ct);
        var r = new Release { ProjectId = projectId, Tag = "" };
        Apply(r, req);
        db.Releases.Add(r);
        await db.SaveChangesAsync(ct);
        return await GetAsync(r.Id, ct);
    }

    public async Task<ReleaseDto> UpdateAsync(Guid id, SaveReleaseRequest req, CancellationToken ct)
    {
        ValidateRequest(req);
        var r = await LoadAsync(id, req.Version, ct);
        if (r.Status != ReleaseStatus.Draft) throw new DomainException("release.locked", $"Release {r.Tag} is {r.Status}; only drafts can be edited.");
        await EnsureTagFreeAsync(db, r.ProjectId, req.Tag, r.Id, ct);
        Apply(r, req);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>The readiness check, worked out from recorded data and the repository as it is now.</summary>
    public async Task<ReleaseReadinessDto> ReadinessAsync(Guid id, CancellationToken ct)
    {
        var r = await db.Releases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Release", id);
        return await EvaluateAsync(r, ct);
    }

    private async Task<ReleaseReadinessDto> EvaluateAsync(Release r, CancellationToken ct)
    {
        var checks = new List<ReadinessCheckDto>
        {
            new("releaseNotes", !string.IsNullOrWhiteSpace(r.Notes), true),
            new("deploymentPlan", !string.IsNullOrWhiteSpace(r.DeploymentPlan), true),
            new("rollbackPlan", !string.IsNullOrWhiteSpace(r.RollbackPlan), true),
        };
        var issues = new List<string>();

        var repo = await db.CodeRepositories.AsNoTracking().Where(x => x.ProjectId == r.ProjectId).OrderByDescending(x => x.IsDefault).ThenBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        string? head = null;
        Guid? testRunId = null;
        if (repo is not null)
        {
            head = await DefaultHeadAsync(code, repo, ct);
            var test = head is null ? null : await db.TestRuns.AsNoTracking().Where(t => t.RepositoryId == repo.Id && t.CommitSha == head)
                .OrderByDescending(t => t.StartedAt).Select(t => new { t.Id, t.Status }).FirstOrDefaultAsync(ct);
            testRunId = test?.Status == TestRunStatus.Passed ? test.Id : null;
            checks.Add(new("testsPassOnCommit", testRunId is not null, true, test is null ? "none" : test.Status.ToString()));
            var openMrs = await db.MergeRequests.AsNoTracking().Where(m => m.ProjectId == r.ProjectId && (m.Status == MergeRequestStatus.Open || m.Status == MergeRequestStatus.Conflict))
                .OrderBy(m => m.Number).Select(m => new { m.Number, m.Title }).ToListAsync(ct);
            checks.Add(new("noOpenMergeRequests", openMrs.Count == 0, false, $"{openMrs.Count}"));
            issues.AddRange(openMrs.Select(m => $"!{m.Number} {m.Title}"));
        }
        else
        {
            checks.Add(new("repositoryLinked", false, false));
        }

        var defects = await db.Defects.AsNoTracking().Where(d => d.ProjectId == r.ProjectId && d.Status == DefectStatus.Open)
            .OrderByDescending(d => d.Severity).ThenBy(d => d.Code).Select(d => new { d.Code, d.Title, d.Severity }).ToListAsync(ct);
        var critical = defects.Count(d => d.Severity == DefectSeverity.Critical);
        checks.Add(new("noCriticalDefects", critical == 0, true, $"{critical}"));
        checks.Add(new("noOpenDefects", defects.Count == 0, false, $"{defects.Count}"));
        issues.AddRange(defects.Select(d => $"{d.Code} [{d.Severity}] {d.Title}"));

        var stuck = await db.Tasks.AsNoTracking().Where(t => t.ProjectId == r.ProjectId && (t.Status == WorkTaskStatus.Blocked || t.Status == WorkTaskStatus.Failed))
            .OrderBy(t => t.CreatedAt).Select(t => new { t.Title, t.Status }).ToListAsync(ct);
        checks.Add(new("noBlockedTasks", stuck.Count == 0, false, $"{stuck.Count}"));
        issues.AddRange(stuck.Select(t => $"{t.Status}: {t.Title}"));

        return new ReleaseReadinessDto(checks.Where(c => c.Blocking).All(c => c.Met), repo?.Name, repo?.DefaultBranch, head, testRunId, checks, issues);
    }

    /// <summary>Freezes the candidate (commit, test run, unresolved issues) and asks a person to approve production deployment.</summary>
    public async Task<ReleaseDto> SubmitAsync(Guid id, CancellationToken ct)
    {
        var r = await LoadAsync(id, null, ct);
        if (r.Status != ReleaseStatus.Draft) throw new DomainException("release.locked", $"Release {r.Tag} is {r.Status}.");
        var readiness = await EvaluateAsync(r, ct);
        if (!readiness.Ready)
            throw new DomainException("release.not_ready", "Not ready: " + string.Join(", ", readiness.Checks.Where(c => c.Blocking && !c.Met).Select(c => c.Code)) + ".");

        var project = await db.Projects.AsNoTracking().FirstAsync(p => p.Id == r.ProjectId, ct);
        r.RepositoryId = readiness.RepositoryName is null ? null : await db.CodeRepositories.Where(x => x.ProjectId == r.ProjectId && x.Name == readiness.RepositoryName).Select(x => (Guid?)x.Id).FirstAsync(ct);
        r.CommitSha = readiness.CommitSha;
        r.TestRunId = readiness.TestRunId;
        r.UnresolvedIssues = readiness.UnresolvedIssues;
        r.Status = ReleaseStatus.PendingApproval;
        var warnings = readiness.Checks.Where(c => !c.Blocking && !c.Met).Select(c => c.Code).ToList();
        var approval = Approvals.Create(ApprovalKind.ProductionDeployment, $"Deploy {project.Name} {r.Tag} to production",
            warnings.Count > 0 ? "Warnings: " + string.Join(", ", warnings) : "All readiness checks passed.",
            new
            {
                releaseId = r.Id, r.Tag, r.Title, repository = readiness.RepositoryName, branch = readiness.Branch, commitSha = r.CommitSha, testRunId = r.TestRunId,
                r.Notes, r.DeploymentPlan, r.RollbackPlan, r.KnownIssues, unresolvedIssues = r.UnresolvedIssues, warnings,
            },
            clock.GetUtcNow(), projectId: r.ProjectId, requestedByUser: current.UserId);
        db.ApprovalRequests.Add(approval);
        r.ApprovalRequestId = approval.Id;
        await db.SaveChangesAsync(ct);
        await notifier.ApprovalsChangedAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Called from the approval inbox. Approval holds only if the default branch still points at the reviewed commit.</summary>
    internal static async Task ApplyDecisionAsync(IAppDbContext db, ICodeRepositoryStore code, ApprovalRequest approval, bool approve, DateTimeOffset now, CancellationToken ct)
    {
        var releaseId = JsonDocument.Parse(approval.PayloadJson).RootElement.GetProperty("releaseId").GetGuid();
        var r = await db.Releases.FirstAsync(x => x.Id == releaseId, ct);
        if (r.Status != ReleaseStatus.PendingApproval) throw new DomainException("approval.closed", $"Release {r.Tag} is {r.Status}.");
        if (!approve)
        {
            r.Status = ReleaseStatus.Draft;
            r.ApprovalRequestId = null;
            return;
        }
        if (r.RepositoryId is { } repoId)
        {
            var repo = await db.CodeRepositories.AsNoTracking().FirstAsync(x => x.Id == repoId, ct);
            if (await DefaultHeadAsync(code, repo, ct) != r.CommitSha)
                throw new DomainException("approval.changed", $"{repo.DefaultBranch} moved after release {r.Tag} was submitted. Cancel it and submit again.");
        }
        r.Status = ReleaseStatus.Approved;
        r.ApprovedAt = now;
    }

    public async Task<ReleaseDto> CancelAsync(Guid id, ReleaseOutcomeRequest req, CancellationToken ct)
    {
        var r = await LoadAsync(id, null, ct);
        if (r.Status is not (ReleaseStatus.Draft or ReleaseStatus.PendingApproval or ReleaseStatus.Approved))
            throw new DomainException("release.locked", $"Release {r.Tag} is {r.Status}.");
        if (r.ApprovalRequestId is { } a && await db.ApprovalRequests.FirstOrDefaultAsync(x => x.Id == a && x.Status == ApprovalStatus.Pending, ct) is { } pending)
            pending.Status = ApprovalStatus.Cancelled;
        r.Status = ReleaseStatus.Cancelled;
        r.OutcomeNote = Note(req);
        await db.SaveChangesAsync(ct);
        await notifier.ApprovalsChangedAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>People record that the approved release went to production (outside this system).</summary>
    public async Task<ReleaseDto> RecordDeployedAsync(Guid id, ReleaseOutcomeRequest req, CancellationToken ct)
    {
        EnsurePerson();
        var r = await LoadAsync(id, null, ct);
        if (r.Status != ReleaseStatus.Approved) throw new DomainException("release.not_approved", $"Release {r.Tag} is {r.Status}; only an approved release can be deployed.");
        r.Status = ReleaseStatus.Deployed;
        r.DeployedAt = clock.GetUtcNow();
        r.OutcomeNote = Note(req);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ReleaseDto> RecordRolledBackAsync(Guid id, ReleaseOutcomeRequest req, CancellationToken ct)
    {
        EnsurePerson();
        OfficeAI.Application.Validate.Required(req.Note, "note");
        var r = await LoadAsync(id, null, ct);
        if (r.Status != ReleaseStatus.Deployed) throw new DomainException("release.not_deployed", $"Release {r.Tag} is {r.Status}; only a deployed release can be rolled back.");
        r.Status = ReleaseStatus.RolledBack;
        r.RolledBackAt = clock.GetUtcNow();
        r.OutcomeNote = Note(req);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private void EnsurePerson()
    {
        if (current.IsAgent || current.UserId is null) throw new ForbiddenException("Only a person can record a production deployment.");
    }

    private static string? Note(ReleaseOutcomeRequest req) => string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();

    private async Task<Release> LoadAsync(Guid id, uint? version, CancellationToken ct)
    {
        var r = await db.Releases.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Release", id);
        if (version is { } v) db.ExpectVersion(r, v);
        return r;
    }
}

/// <summary>Release manager / DevOps write the release candidate: notes, deployment instructions, rollback plan, known issues.</summary>
public sealed class PrepareReleaseTool : IAgentTool
{
    public const string ToolId = "release.prepare";

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0",
        "Create or update a draft release of this project by its version tag: release notes, step-by-step deployment instructions, a rollback plan and known issues. " +
        "A person checks readiness (passing tests on the exact commit, open defects) and submits it; production deployment always needs a person's approval.",
        Schemas.Of(new
        {
            type = "object",
            properties = new
            {
                tag = new { type = "string", maxLength = 40, description = "Version label, e.g. 1.2.0" },
                title = new { type = "string", maxLength = 200 },
                notes = new { type = "string", maxLength = 20000 },
                deploymentPlan = new { type = "string", maxLength = 20000 },
                rollbackPlan = new { type = "string", maxLength = 20000 },
                knownIssues = new { type = "string", maxLength = 8000 },
            },
            required = new[] { "tag", "notes", "deploymentPlan", "rollbackPlan" },
            additionalProperties = false,
        }),
        TimeSpan.FromSeconds(10), 1_000, RequiresApproval: false, HasSideEffects: true);

    public async Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct)
    {
        var req = new SaveReleaseRequest(Schemas.Str(input, "tag") ?? "", Schemas.Str(input, "title"), Schemas.Str(input, "notes"),
            Schemas.Str(input, "deploymentPlan"), Schemas.Str(input, "rollbackPlan"), Schemas.Str(input, "knownIssues"));
        try
        {
            ReleaseService.ValidateRequest(req);
            var tag = req.Tag.Trim();
            // Read the status fresh: a person may have submitted the release while this run was going.
            var status = await ctx.Db.Releases.AsNoTracking().Where(x => x.ProjectId == ctx.ProjectId && x.Tag == tag).Select(x => (ReleaseStatus?)x.Status).FirstOrDefaultAsync(ct);
            if (status is { } s && s != ReleaseStatus.Draft) return ToolResult.Fail($"Release {tag} is {s} and can no longer be changed.");
            var r = await ctx.Db.Releases.FirstOrDefaultAsync(x => x.ProjectId == ctx.ProjectId && x.Tag == tag, ct);
            if (r is null)
            {
                if (await ctx.Db.Releases.CountAsync(x => x.ProjectId == ctx.ProjectId, ct) >= 200) return ToolResult.Fail("A project can hold at most 200 releases.");
                ctx.Db.Releases.Add(r = new Release { ProjectId = ctx.ProjectId, Tag = tag });
            }
            ReleaseService.Apply(r, req);
            r.AuthorEmployeeId = ctx.EmployeeId;
            r.RunId = ctx.RunId;
            await ctx.Db.SaveChangesAsync(ct);
            return ToolResult.Ok(new { tag, status = "Draft", next = "A person checks readiness and submits it for production approval." });
        }
        catch (DomainException e)
        {
            return ToolResult.Fail(e.Message);
        }
    }
}
