using System.Text;
using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Domain.Code;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Delivery;
using OfficeAI.Domain.Knowledge;
using OfficeAI.Domain.Work;

namespace OfficeAI.Application.Delivery;

public record PackageDocumentDto(Guid Id, DocumentKind Kind, string Title, int Revision, DocumentStatus Status);
public record DeliveryPackageDto(Guid Id, string Code, string Title, Guid ReleaseId, string ReleaseTag, ReleaseStatus ReleaseStatus, string? CommitSha,
    Guid? TestRunId, TestRunStatus? TestRunStatus, List<PackageDocumentDto> Documents, string? HandoverNotes, DeliveryPackageStatus Status,
    List<ReadinessCheckDto> Checks, bool Ready, DateTimeOffset CreatedAt, DateTimeOffset? DeliveredAt, string? DeliveryNote, string? CustomerFeedback,
    DateTimeOffset? RespondedAt, Guid? ChangeTaskId, uint Version);
public record SavePackageRequest(Guid ReleaseId, string? Title = null, List<Guid>? DocumentIds = null, string? HandoverNotes = null, uint? Version = null);
public record PackageDeliveredRequest(string? Note = null, uint? Version = null);
public record PackageResponseRequest(bool Accepted, string? Feedback = null, uint? Version = null);

/// <summary>
/// Customer delivery (Master Prompt §7 stage 8): a package of the deployed release, the exact document versions and the test
/// evidence, a Markdown export to hand over, and the customer's acceptance or change request. The system never sends anything:
/// people record that it was delivered and what the customer answered. A change request becomes a task.
/// </summary>
public class DeliveryPackageService(IAppDbContext db, ICurrentUser current, TimeProvider clock, IRealtimeNotifier notifier)
{
    public const int MaxDocuments = 50;

    public async Task<List<DeliveryPackageDto>> ListAsync(Guid projectId, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) throw new NotFoundException("Project", projectId);
        var rows = await db.DeliveryPackages.AsNoTracking().Where(p => p.ProjectId == projectId).OrderByDescending(p => p.CreatedAt).ToListAsync(ct);
        return await ToDtosAsync(projectId, rows, ct);
    }

    public async Task<DeliveryPackageDto> GetAsync(Guid id, CancellationToken ct)
    {
        var p = await db.DeliveryPackages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("DeliveryPackage", id);
        return (await ToDtosAsync(p.ProjectId, [p], ct))[0];
    }

    private async Task<List<DeliveryPackageDto>> ToDtosAsync(Guid projectId, List<DeliveryPackage> rows, CancellationToken ct)
    {
        var releaseIds = rows.Select(r => r.ReleaseId).Distinct().ToList();
        var releases = await db.Releases.AsNoTracking().Where(r => releaseIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        var runIds = releases.Values.Select(r => r.TestRunId).OfType<Guid>().ToList();
        var runs = await db.TestRuns.AsNoTracking().Where(t => runIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Status, ct);
        var docIds = rows.SelectMany(r => r.DocumentIds).Distinct().ToList();
        var docs = await db.Documents.AsNoTracking().Where(d => docIds.Contains(d.Id))
            .Select(d => new PackageDocumentDto(d.Id, d.Kind, d.Title, d.Revision, d.Status)).ToDictionaryAsync(d => d.Id, ct);
        var openDefects = await db.Defects.CountAsync(d => d.ProjectId == projectId && d.Status == DefectStatus.Open, ct);

        return rows.Select(p =>
        {
            var r = releases[p.ReleaseId];
            TestRunStatus? run = r.TestRunId is { } rid && runs.TryGetValue(rid, out var s) ? s : null;
            var included = p.DocumentIds.Where(docs.ContainsKey).Select(id => docs[id]).ToList();
            List<ReadinessCheckDto> checks =
            [
                new("releaseDeployed", r.Status == ReleaseStatus.Deployed, true, r.Tag),
                new("testEvidence", run == TestRunStatus.Passed, true, r.CommitSha is { Length: >= 7 } sha ? sha[..7] : null),
                new("completionReportIncluded", included.Any(d => d.Kind == DocumentKind.CompletionReport), true),
                new("handoverNotes", !string.IsNullOrWhiteSpace(p.HandoverNotes), true),
                new("documentsFinal", included.All(d => d.Status == DocumentStatus.Final), false, $"{included.Count(d => d.Status == DocumentStatus.Final)}/{included.Count}"),
                new("noOpenDefects", openDefects == 0, false, $"{openDefects}"),
            ];
            return new DeliveryPackageDto(p.Id, p.Code, p.Title, p.ReleaseId, r.Tag, r.Status, r.CommitSha, r.TestRunId, run, included, p.HandoverNotes, p.Status,
                checks, checks.All(c => c.Met || !c.Blocking), p.CreatedAt, p.DeliveredAt, p.DeliveryNote, p.CustomerFeedback, p.RespondedAt,
                p.ChangeTaskId, p.Version);
        }).ToList();
    }

    public async Task<DeliveryPackageDto> CreateAsync(Guid projectId, SavePackageRequest req, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) throw new NotFoundException("Project", projectId);
        var p = new DeliveryPackage { ProjectId = projectId, Code = "", Title = "" };
        await ApplyAsync(p, req, ct);
        p.Code = $"PKG-{await db.DeliveryPackages.CountAsync(x => x.ProjectId == projectId, ct) + 1:000}";
        db.DeliveryPackages.Add(p);
        await db.SaveChangesAsync(ct);
        return await GetAsync(p.Id, ct);
    }

    public async Task<DeliveryPackageDto> UpdateAsync(Guid id, SavePackageRequest req, CancellationToken ct)
    {
        var p = await LoadAsync(id, req.Version, ct);
        if (p.Status != DeliveryPackageStatus.Draft) throw new DomainException("package.not_draft", $"{p.Code} was already delivered; make a new package instead.");
        await ApplyAsync(p, req, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private async Task ApplyAsync(DeliveryPackage p, SavePackageRequest req, CancellationToken ct)
    {
        var release = await db.Releases.AsNoTracking().FirstOrDefaultAsync(r => r.Id == req.ReleaseId && r.ProjectId == p.ProjectId, ct)
            ?? throw new DomainException("validation", "releaseId does not belong to this project.");
        var ids = (req.DocumentIds ?? []).Distinct().ToList();
        if (ids.Count > MaxDocuments) throw new DomainException("validation", $"A package can include at most {MaxDocuments} documents.");
        if (await db.Documents.CountAsync(d => ids.Contains(d.Id) && d.ProjectId == p.ProjectId, ct) != ids.Count)
            throw new DomainException("validation", "documentIds must be documents of this project.");
        var title = string.IsNullOrWhiteSpace(req.Title) ? $"Delivery of {release.Tag}" : req.Title.Trim();
        if (title.Length > 200) throw new DomainException("validation", "title must be at most 200 characters.");
        p.Title = title;
        p.ReleaseId = release.Id;
        p.DocumentIds = ids;
        p.HandoverNotes = string.IsNullOrWhiteSpace(req.HandoverNotes) ? null : req.HandoverNotes.Trim();
    }

    /// <summary>A person sent the package to the customer outside the system. Needs every blocking check met.</summary>
    public async Task<DeliveryPackageDto> MarkDeliveredAsync(Guid id, PackageDeliveredRequest req, CancellationToken ct)
    {
        EnsurePerson();
        var p = await LoadAsync(id, req.Version, ct);
        if (p.Status != DeliveryPackageStatus.Draft) throw new DomainException("package.not_draft", $"{p.Code} is already {p.Status}.");
        var missing = (await GetAsync(id, ct)).Checks.Where(c => c.Blocking && !c.Met).Select(c => c.Code).ToList();
        if (missing.Count > 0) throw new DomainException("package.not_ready", $"Not ready to deliver: {string.Join(", ", missing)}.");
        p.Status = DeliveryPackageStatus.Delivered;
        p.DeliveredAt = clock.GetUtcNow();
        p.DeliveryNote = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>The customer's response as relayed by a person. A change request becomes a High-priority task with the feedback.</summary>
    public async Task<DeliveryPackageDto> RecordResponseAsync(Guid id, PackageResponseRequest req, CancellationToken ct)
    {
        EnsurePerson();
        if (!req.Accepted) Validate.Required(req.Feedback, "feedback");
        var p = await LoadAsync(id, req.Version, ct);
        if (p.Status != DeliveryPackageStatus.Delivered) throw new DomainException("package.not_delivered", $"{p.Code} is {p.Status}; record the delivery first.");
        p.Status = req.Accepted ? DeliveryPackageStatus.Accepted : DeliveryPackageStatus.ChangesRequested;
        p.CustomerFeedback = string.IsNullOrWhiteSpace(req.Feedback) ? null : req.Feedback.Trim();
        p.RespondedAt = clock.GetUtcNow();
        WorkTask? task = null;
        if (!req.Accepted)
        {
            task = new WorkTask
            {
                ProjectId = p.ProjectId, Title = $"Customer change request ({p.Code}): {p.Title}", Priority = TaskPriority.High,
                Description = $"The customer asked for changes after receiving {p.Code}:\n\n{p.CustomerFeedback}",
                AcceptanceCriteria = "Each requested change is done or agreed with the customer as out of scope, and a new delivery package is accepted.",
            };
            if (task.Title.Length > 300) task.Title = task.Title[..300];
            db.Tasks.Add(task);
            p.ChangeTaskId = task.Id;
        }
        await db.SaveChangesAsync(ct);
        if (task is not null) await notifier.TaskChangedAsync(p.ProjectId, task.Id, ct);
        return await GetAsync(id, ct);
    }

    /// <summary>The package as one Markdown file: manifest, handover notes, release plans, test evidence and every included document.</summary>
    public async Task<(string FileName, string Content)> ExportAsync(Guid id, CancellationToken ct)
    {
        var dto = await GetAsync(id, ct);
        var projectId = await db.DeliveryPackages.Where(p => p.Id == id).Select(p => p.ProjectId).FirstAsync(ct);
        var project = await db.Projects.AsNoTracking().FirstAsync(x => x.Id == projectId, ct);
        var release = await db.Releases.AsNoTracking().FirstAsync(r => r.Id == dto.ReleaseId, ct);
        var run = release.TestRunId is { } rid ? await db.TestRuns.AsNoTracking().FirstOrDefaultAsync(t => t.Id == rid, ct) : null;
        var ids = dto.Documents.Select(d => d.Id).ToList();
        var contents = await db.Documents.AsNoTracking().Where(d => ids.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Content, ct);

        var sb = new StringBuilder();
        sb.Append($"# {dto.Title}\n\n");
        sb.Append($"- **Project:** {project.Name} ({project.Code})\n- **Package:** {dto.Code}\n- **Release:** {release.Tag}");
        if (release.CommitSha is { } sha) sb.Append($" (commit `{sha}`)");
        sb.Append($"\n- **Prepared:** {dto.CreatedAt:yyyy-MM-dd}\n");
        if (dto.DeliveredAt is { } at) sb.Append($"- **Delivered:** {at:yyyy-MM-dd}\n");
        sb.Append("\n## Contents\n\n");
        foreach (var d in dto.Documents) sb.Append($"- {d.Title} (revision {d.Revision}, {d.Status})\n");
        if (dto.Documents.Count == 0) sb.Append("- (no documents)\n");
        if (dto.HandoverNotes is { } h) sb.Append($"\n## Handover\n\n{h}\n");
        if (release.Notes is { } notes) sb.Append($"\n## Release notes\n\n{notes}\n");
        if (release.DeploymentPlan is { } dep) sb.Append($"\n## Deployment\n\n{dep}\n");
        if (release.RollbackPlan is { } rb) sb.Append($"\n## Rollback\n\n{rb}\n");
        if (release.KnownIssues is { } ki) sb.Append($"\n## Known issues\n\n{ki}\n");
        sb.Append("\n## Test evidence\n\n");
        sb.Append(run is null ? "No test run is linked to this release.\n"
            : $"- **Result:** {run.Status}\n- **Commit:** `{run.CommitSha}`\n- **Command:** `{run.Command}`\n- **Run at:** {run.StartedAt:yyyy-MM-dd HH:mm} UTC\n");
        foreach (var d in dto.Documents)
            sb.Append($"\n---\n\n<!-- {d.Title}, revision {d.Revision} -->\n\n{contents.GetValueOrDefault(d.Id)}\n");

        var name = new string($"{project.Code}-{dto.Code}-{release.Tag}".Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-').ToArray());
        return ($"{name}.md", sb.ToString());
    }

    private void EnsurePerson()
    {
        if (current.IsAgent) throw new DomainException("package.people_only", "Only a person can record what was delivered to the customer and their answer.");
    }

    private async Task<DeliveryPackage> LoadAsync(Guid id, uint? version, CancellationToken ct)
    {
        var p = await db.DeliveryPackages.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("DeliveryPackage", id);
        if (version is { } v) db.ExpectVersion(p, v);
        return p;
    }
}
