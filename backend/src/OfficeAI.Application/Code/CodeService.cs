using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Domain.Code;
using OfficeAI.Domain.Common;

namespace OfficeAI.Application.Code;

public record RepositoryDto(Guid Id, Guid ProjectId, string Name, string DefaultBranch, string SandboxImage, string TestCommand, bool IsDefault, DateTimeOffset CreatedAt, uint Version);
public record CreateRepositoryRequest(string Name, string SandboxImage, string TestCommand, string DefaultBranch = "main");
public record UpdateRepositoryRequest(string SandboxImage, string TestCommand, uint? Version = null);
public record TestRunDto(Guid Id, Guid RepositoryId, Guid? TaskId, Guid? RunId, string Branch, string? CommitSha, string Image, string Command,
    TestRunStatus Status, int? ExitCode, string? Output, int DurationMs, DateTimeOffset StartedAt);
public record MergeRequestDto(Guid Id, Guid ProjectId, Guid RepositoryId, Guid? TaskId, int Number, string Title, string? Description, string SourceBranch,
    string TargetBranch, string HeadSha, MergeRequestStatus Status, Guid? LatestTestRunId, TestRunStatus? LatestTestStatus, string? AuthorName,
    string? MergedSha, DateTimeOffset? MergedAt, DateTimeOffset CreatedAt);
public record MergeRequestDetailDto(MergeRequestDto MergeRequest, string Diff);
public record SandboxInfoDto(bool IsAvailable, IReadOnlyCollection<string> AllowedImages);

public partial class CodeService(IAppDbContext db, ICodeRepositoryStore store, ISandbox sandbox)
{
    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,62}$")]
    private static partial Regex RepoName();

    public SandboxInfoDto SandboxInfo() => new(sandbox.IsAvailable, sandbox.AllowedImages);

    public async Task<List<RepositoryDto>> ListAsync(Guid projectId, CancellationToken ct) =>
        await db.CodeRepositories.AsNoTracking().Where(r => r.ProjectId == projectId).OrderByDescending(r => r.IsDefault).ThenBy(r => r.Name)
            .Select(r => new RepositoryDto(r.Id, r.ProjectId, r.Name, r.DefaultBranch, r.SandboxImage, r.TestCommand, r.IsDefault, r.CreatedAt, r.Version))
            .ToListAsync(ct);

    private void ValidateSandbox(string image, string command)
    {
        if (!sandbox.AllowedImages.Contains(image)) throw new DomainException("validation", $"Image '{image}' is not on the sandbox allow-list.");
        if (string.IsNullOrWhiteSpace(command) || command.Length > 1000) throw new DomainException("validation", "testCommand is required (max 1000 characters).");
    }

    public async Task<RepositoryDto> CreateAsync(Guid projectId, CreateRepositoryRequest req, CancellationToken ct)
    {
        var name = (req.Name ?? "").Trim().ToLowerInvariant();
        if (!RepoName().IsMatch(name)) throw new DomainException("validation", "name must be lowercase letters, digits, '.', '-' or '_'.");
        if (!RepoName().IsMatch(req.DefaultBranch)) throw new DomainException("validation", "defaultBranch is invalid.");
        ValidateSandbox(req.SandboxImage, req.TestCommand);
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) throw new NotFoundException("Project", projectId);
        if (await db.CodeRepositories.AnyAsync(r => r.ProjectId == projectId && r.Name == name, ct))
            throw new DomainException("code.duplicate", $"The project already has a repository named '{name}'.");

        var repo = new CodeRepository
        {
            ProjectId = projectId, Name = name, DefaultBranch = req.DefaultBranch, SandboxImage = req.SandboxImage, TestCommand = req.TestCommand.Trim(),
            IsDefault = !await db.CodeRepositories.AnyAsync(r => r.ProjectId == projectId, ct),
        };
        await store.InitAsync(repo, ct);
        db.CodeRepositories.Add(repo);
        await db.SaveChangesAsync(ct);
        return (await ListAsync(projectId, ct)).First(r => r.Id == repo.Id);
    }

    public async Task<RepositoryDto> UpdateAsync(Guid id, UpdateRepositoryRequest req, CancellationToken ct)
    {
        var repo = await db.CodeRepositories.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Repository", id);
        if (req.Version is { } v) db.ExpectVersion(repo, v);
        ValidateSandbox(req.SandboxImage, req.TestCommand);
        repo.SandboxImage = req.SandboxImage;
        repo.TestCommand = req.TestCommand.Trim();
        await db.SaveChangesAsync(ct);
        return (await ListAsync(repo.ProjectId, ct)).First(r => r.Id == id);
    }

    private async Task<CodeRepository> RepoAsync(Guid id, CancellationToken ct) =>
        await db.CodeRepositories.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Repository", id);

    public async Task<IReadOnlyList<string>> BranchesAsync(Guid id, CancellationToken ct) => await store.BranchesAsync(await RepoAsync(id, ct), ct);

    public async Task<IReadOnlyList<string>> FilesAsync(Guid id, string? branch, CancellationToken ct)
    {
        var repo = await RepoAsync(id, ct);
        return await store.ListFilesAsync(repo, string.IsNullOrWhiteSpace(branch) ? repo.DefaultBranch : branch, ct);
    }

    public async Task<string> FileAsync(Guid id, string? branch, string path, CancellationToken ct)
    {
        var repo = await RepoAsync(id, ct);
        return await store.ReadFileAsync(repo, string.IsNullOrWhiteSpace(branch) ? repo.DefaultBranch : branch, path, ct)
            ?? throw new DomainException("code.not_found", "File not found on that branch.");
    }

    public async Task<List<TestRunDto>> TestRunsAsync(Guid repositoryId, Guid? taskId, CancellationToken ct)
    {
        var q = db.TestRuns.AsNoTracking().Where(t => t.RepositoryId == repositoryId);
        if (taskId is { } tid) q = q.Where(t => t.TaskId == tid);
        return await q.OrderByDescending(t => t.StartedAt).Take(100)
            .Select(t => new TestRunDto(t.Id, t.RepositoryId, t.TaskId, t.RunId, t.Branch, t.CommitSha, t.Image, t.Command, t.Status, t.ExitCode, t.Output, t.DurationMs, t.StartedAt))
            .ToListAsync(ct);
    }

    private IQueryable<MergeRequestDto> MrQuery(IQueryable<MergeRequest> q) =>
        q.Select(m => new MergeRequestDto(m.Id, m.ProjectId, m.RepositoryId, m.TaskId, m.Number, m.Title, m.Description, m.SourceBranch, m.TargetBranch,
            m.HeadSha, m.Status, m.LatestTestRunId, db.TestRuns.Where(t => t.Id == m.LatestTestRunId).Select(t => (TestRunStatus?)t.Status).FirstOrDefault(),
            db.Employees.Where(e => e.Id == m.AuthorEmployeeId).Select(e => e.DisplayName).FirstOrDefault(), m.MergedSha, m.MergedAt, m.CreatedAt));

    public async Task<List<MergeRequestDto>> MergeRequestsAsync(Guid projectId, CancellationToken ct) =>
        await MrQuery(db.MergeRequests.AsNoTracking().Where(m => m.ProjectId == projectId).OrderByDescending(m => m.CreatedAt)).ToListAsync(ct);

    public async Task<MergeRequestDetailDto> MergeRequestAsync(Guid id, CancellationToken ct)
    {
        var dto = await MrQuery(db.MergeRequests.AsNoTracking().Where(m => m.Id == id)).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("MergeRequest", id);
        var repo = await RepoAsync(dto.RepositoryId, ct);
        var diffBase = dto.Status == MergeRequestStatus.Merged ? $"{dto.MergedSha}^1" : dto.TargetBranch;
        var diff = await store.GitAsync(Path.GetFullPath("."), ["--git-dir", OriginPath(repo), "diff", $"{diffBase}...{dto.HeadSha}"], ct);
        return new MergeRequestDetailDto(dto, diff.Ok ? diff.Output : diff.Error);
    }

    /// <summary>Resolved through the store's layout; kept here only for read-only diffs.</summary>
    private string OriginPath(CodeRepository repo) => store is IRepositoryLayout layout ? layout.OriginPath(repo) : throw new InvalidOperationException("Unknown repository layout.");
}

/// <summary>Where a repository lives on disk (implemented by the Git store).</summary>
public interface IRepositoryLayout
{
    string OriginPath(CodeRepository repo);
}
