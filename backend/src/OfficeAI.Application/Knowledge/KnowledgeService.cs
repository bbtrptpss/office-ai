using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Knowledge;

namespace OfficeAI.Application.Knowledge;

public record KnowledgeDto(Guid Id, KnowledgeKind Kind, string Title, string Body, List<string> Tags, Guid? ProjectId, string? ProjectName,
    KnowledgeStatus Status, string? Source, string? AuthorName, DateTimeOffset? ValidatedAt, DateTimeOffset CreatedAt, uint Version);
public record SaveKnowledgeRequest(KnowledgeKind Kind, string Title, string Body, List<string>? Tags, Guid? ProjectId, string? Source, uint? Version = null);
public record SetKnowledgeStatusRequest(KnowledgeStatus Status);
public record DocumentDto(Guid Id, Guid ProjectId, DocumentKind Kind, string Title, int Version, DocumentStatus Status, string? AuthorName,
    bool IsSimulated, DateTimeOffset CreatedAt, string? Content);

/// <summary>Knowledge base and project documents. Knowledge is trusted only after a person validates it.</summary>
public class KnowledgeService(IAppDbContext db, ICurrentUser current, TimeProvider clock)
{
    /// <summary>Case-insensitive match on title, body and tags, limited to company-wide entries and the given project.</summary>
    public static IQueryable<KnowledgeEntry> SearchQuery(IAppDbContext db, string q, Guid? projectId)
    {
        var lower = q.ToLowerInvariant();
        return db.KnowledgeEntries.AsNoTracking()
            .Where(k => k.ProjectId == null || k.ProjectId == projectId)
            .Where(k => k.Title.ToLower().Contains(lower) || k.Body.ToLower().Contains(lower) || k.Tags.Contains(lower))
            .OrderByDescending(k => k.Title.ToLower().Contains(lower)).ThenByDescending(k => k.ValidatedAt);
    }

    internal static async Task<ProjectDocument> AddVersionAsync(IAppDbContext db, Guid projectId, DocumentKind kind, string title, string content, CancellationToken ct)
    {
        var last = await db.Documents.Where(d => d.ProjectId == projectId && d.Kind == kind && d.Title == title).MaxAsync(d => (int?)d.Revision, ct) ?? 0;
        var doc = new ProjectDocument { ProjectId = projectId, Kind = kind, Title = title, Content = content, Revision = last + 1 };
        db.Documents.Add(doc);
        return doc;
    }

    // ---------------- knowledge ----------------

    public async Task<List<KnowledgeDto>> ListAsync(string? query, KnowledgeStatus? status, Guid? projectId, CancellationToken ct)
    {
        var q = string.IsNullOrWhiteSpace(query)
            ? db.KnowledgeEntries.AsNoTracking().Where(k => projectId == null || k.ProjectId == null || k.ProjectId == projectId).OrderByDescending(k => k.CreatedAt)
            : SearchQuery(db, query.Trim(), projectId);
        if (status is { } s) q = q.Where(k => k.Status == s);
        var rows = await q.Take(200).ToListAsync(ct);
        return await ToDtosAsync(rows, ct);
    }

    private async Task<List<KnowledgeDto>> ToDtosAsync(List<KnowledgeEntry> rows, CancellationToken ct)
    {
        var authors = rows.Select(r => r.AuthorEmployeeId).OfType<Guid>().Distinct().ToList();
        var names = await db.Employees.AsNoTracking().Where(e => authors.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.DisplayName, ct);
        var projectIds = rows.Select(r => r.ProjectId).OfType<Guid>().Distinct().ToList();
        var projects = await db.Projects.AsNoTracking().Where(p => projectIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        return rows.Select(k => new KnowledgeDto(k.Id, k.Kind, k.Title, k.Body, k.Tags, k.ProjectId, k.ProjectId is { } p ? projects.GetValueOrDefault(p) : null,
            k.Status, k.Source, k.AuthorEmployeeId is { } a ? names.GetValueOrDefault(a) : null, k.ValidatedAt, k.CreatedAt, k.Version)).ToList();
    }

    /// <summary>People write knowledge directly as validated: they are accountable for it.</summary>
    public async Task<KnowledgeDto> CreateAsync(SaveKnowledgeRequest req, CancellationToken ct)
    {
        Validate.Required(req.Title, "title");
        Validate.Required(req.Body, "body");
        if (!Enum.IsDefined(req.Kind)) throw new DomainException("validation", "kind is invalid.");
        if (req.ProjectId is { } p && !await db.Projects.AnyAsync(x => x.Id == p, ct)) throw new NotFoundException("Project", p);
        var k = new KnowledgeEntry
        {
            Kind = req.Kind, Title = req.Title.Trim(), Body = req.Body, ProjectId = req.ProjectId, Source = req.Source,
            Tags = (req.Tags ?? []).Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).Distinct().Take(10).ToList(),
            Status = KnowledgeStatus.Validated, ValidatedByUserId = current.UserId, ValidatedAt = clock.GetUtcNow(),
        };
        db.KnowledgeEntries.Add(k);
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([k], ct))[0];
    }

    public async Task<KnowledgeDto> SetStatusAsync(Guid id, SetKnowledgeStatusRequest req, CancellationToken ct)
    {
        if (current.IsAgent) throw new ForbiddenException("Only a person can validate knowledge.");
        var k = await db.KnowledgeEntries.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Knowledge", id);
        if (!Enum.IsDefined(req.Status)) throw new DomainException("validation", "status is invalid.");
        k.Status = req.Status;
        if (req.Status == KnowledgeStatus.Validated)
        {
            k.ValidatedByUserId = current.UserId;
            k.ValidatedAt = clock.GetUtcNow();
        }
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([k], ct))[0];
    }

    // ---------------- documents ----------------

    public async Task<List<DocumentDto>> ListDocumentsAsync(Guid projectId, CancellationToken ct) =>
        await db.Documents.AsNoTracking().Where(d => d.ProjectId == projectId).OrderBy(d => d.Kind).ThenBy(d => d.Title).ThenByDescending(d => d.Revision)
            .Select(d => new DocumentDto(d.Id, d.ProjectId, d.Kind, d.Title, d.Revision, d.Status,
                db.Employees.Where(e => e.Id == d.AuthorEmployeeId).Select(e => e.DisplayName).FirstOrDefault(), d.IsSimulated, d.CreatedAt, null))
            .ToListAsync(ct);

    public async Task<DocumentDto> GetDocumentAsync(Guid id, CancellationToken ct) =>
        await db.Documents.AsNoTracking().Where(d => d.Id == id)
            .Select(d => new DocumentDto(d.Id, d.ProjectId, d.Kind, d.Title, d.Revision, d.Status,
                db.Employees.Where(e => e.Id == d.AuthorEmployeeId).Select(e => e.DisplayName).FirstOrDefault(), d.IsSimulated, d.CreatedAt, d.Content))
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Document", id);

    public async Task<DocumentDto> FinalizeDocumentAsync(Guid id, CancellationToken ct)
    {
        var d = await db.Documents.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Document", id);
        if (d.IsSimulated) throw new DomainException("validation", "A document produced by the simulated provider cannot be made final.");
        d.Status = DocumentStatus.Final;
        await db.SaveChangesAsync(ct);
        return await GetDocumentAsync(id, ct);
    }
}
