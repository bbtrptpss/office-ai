using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Domain.Code;
using OfficeAI.Domain.Common;

namespace OfficeAI.Application.Code;

public record RepositoryDto(Guid Id, Guid ProjectId, string Name, string DefaultBranch, string SandboxImage, string TestCommand, bool IsDefault, DateTimeOffset CreatedAt, uint Version,
    string? RemoteUrl = null, string? RemoteTokenEnvVar = null, bool RemoteTokenPresent = false, DateTimeOffset? RemotePushedAt = null, string? RemotePushedSha = null,
    string? RemoteError = null);
/// <param name="ImportUrl">Start from a copy of this remote (GitHub/GitLab) instead of an empty repository; it also becomes the push remote.</param>
public record CreateRepositoryRequest(string Name, string SandboxImage, string TestCommand, string DefaultBranch = "main", string? ImportUrl = null,
    string? RemoteTokenEnvVar = null);
/// <summary>Null RemoteUrl removes the remote.</summary>
public record SetRemoteRequest(string? RemoteUrl, string? RemoteTokenEnvVar, uint? Version = null);
public record UpdateRepositoryRequest(string SandboxImage, string TestCommand, uint? Version = null);
public record TestRunDto(Guid Id, Guid RepositoryId, Guid? TaskId, Guid? RunId, string Branch, string? CommitSha, string Image, string Command,
    TestRunStatus Status, int? ExitCode, string? Output, int DurationMs, DateTimeOffset StartedAt);
public record MergeRequestDto(Guid Id, Guid ProjectId, Guid RepositoryId, Guid? TaskId, int Number, string Title, string? Description, string SourceBranch,
    string TargetBranch, string HeadSha, MergeRequestStatus Status, Guid? LatestTestRunId, TestRunStatus? LatestTestStatus, string? AuthorName,
    string? MergedSha, DateTimeOffset? MergedAt, DateTimeOffset CreatedAt);
public record MergeRequestDetailDto(MergeRequestDto MergeRequest, string Diff);
public record RunBranchTestsRequest(string? Branch);
public record SandboxInfoDto(bool IsAvailable, IReadOnlyCollection<string> AllowedImages);

public partial class CodeService(IAppDbContext db, ICodeRepositoryStore store, ISandbox sandbox, TimeProvider clock)
{
    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,62}$")]
    private static partial Regex RepoName();

    /// <summary>Only variables named GIT_REMOTE_* can be read as tokens, so a remote can never be pointed at other server secrets.</summary>
    [GeneratedRegex("^GIT_REMOTE_[A-Z0-9_]{2,60}$")]
    private static partial Regex TokenVar();

    internal static string? ReadToken(string? envVar) =>
        envVar is not null && TokenVar().IsMatch(envVar) ? Environment.GetEnvironmentVariable(envVar) : null;

    private void ValidateRemote(string url, string? tokenVar)
    {
        if (store.CheckRemoteUrl(url) is { } problem) throw new DomainException("validation", problem);
        if (tokenVar is not null && !TokenVar().IsMatch(tokenVar))
            throw new DomainException("validation", "The token variable must be named GIT_REMOTE_<NAME> (letters, digits, '_').");
    }

    /// <summary>
    /// Pushes the default branch of <paramref name="repo"/> (a tracked entity) to its remote and records the outcome on it.
    /// Never throws for remote problems: the local repository stays the source of truth.
    /// </summary>
    public static async Task PushDefaultBranchAsync(ICodeRepositoryStore store, CodeRepository repo, TimeProvider clock, CancellationToken ct)
    {
        if (repo.RemoteUrl is null) return;
        var (sha, error) = await store.PushToRemoteAsync(repo, repo.RemoteUrl, ReadToken(repo.RemoteTokenEnvVar), repo.DefaultBranch, ct);
        repo.RemoteError = error is null ? null : error.Length > 2000 ? error[..2000] : error;
        if (sha is not null)
        {
            repo.RemotePushedSha = sha;
            repo.RemotePushedAt = clock.GetUtcNow();
        }
    }

    public SandboxInfoDto SandboxInfo() => new(sandbox.IsAvailable, sandbox.AllowedImages);

    public async Task<List<RepositoryDto>> ListAsync(Guid projectId, CancellationToken ct) =>
        (await db.CodeRepositories.AsNoTracking().Where(r => r.ProjectId == projectId).OrderByDescending(r => r.IsDefault).ThenBy(r => r.Name).ToListAsync(ct))
            .Select(r => new RepositoryDto(r.Id, r.ProjectId, r.Name, r.DefaultBranch, r.SandboxImage, r.TestCommand, r.IsDefault, r.CreatedAt, r.Version,
                r.RemoteUrl, r.RemoteTokenEnvVar, !string.IsNullOrEmpty(ReadToken(r.RemoteTokenEnvVar)), r.RemotePushedAt, r.RemotePushedSha, r.RemoteError))
            .ToList();

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
        if (string.IsNullOrWhiteSpace(req.ImportUrl))
        {
            await store.InitAsync(repo, ct);
        }
        else
        {
            var url = req.ImportUrl.Trim();
            ValidateRemote(url, req.RemoteTokenEnvVar);
            try
            {
                await store.ImportAsync(repo, url, ReadToken(req.RemoteTokenEnvVar), ct);
            }
            catch (InvalidOperationException ex)
            {
                throw new DomainException("code.import_failed", ex.Message);
            }
            repo.RemoteUrl = url;
            repo.RemoteTokenEnvVar = req.RemoteTokenEnvVar;
        }
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

    public async Task<RepositoryDto> SetRemoteAsync(Guid id, SetRemoteRequest req, CancellationToken ct)
    {
        var repo = await db.CodeRepositories.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Repository", id);
        if (req.Version is { } v) db.ExpectVersion(repo, v);
        var url = string.IsNullOrWhiteSpace(req.RemoteUrl) ? null : req.RemoteUrl.Trim();
        var tokenVar = string.IsNullOrWhiteSpace(req.RemoteTokenEnvVar) ? null : req.RemoteTokenEnvVar.Trim();
        if (url is not null) ValidateRemote(url, tokenVar);
        if (url != repo.RemoteUrl) (repo.RemotePushedAt, repo.RemotePushedSha, repo.RemoteError) = (null, null, null);
        repo.RemoteUrl = url;
        repo.RemoteTokenEnvVar = url is null ? null : tokenVar;
        await db.SaveChangesAsync(ct);
        return (await ListAsync(repo.ProjectId, ct)).First(r => r.Id == id);
    }

    /// <summary>A person pushes the default branch to the remote now (it is also pushed after every approved merge).</summary>
    public async Task<RepositoryDto> PushAsync(Guid id, CancellationToken ct)
    {
        var repo = await db.CodeRepositories.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Repository", id);
        if (repo.RemoteUrl is null) throw new DomainException("validation", "This repository has no remote.");
        await PushDefaultBranchAsync(store, repo, clock, ct);
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

    /// <summary>A person runs the tests on a branch (e.g. before approving a merge). Kept like any other test run.</summary>
    public async Task<TestRunDto> RunTestsAsync(Guid repositoryId, string? branch, TimeProvider clock, CancellationToken ct)
    {
        var repo = await RepoAsync(repositoryId, ct);
        if (!sandbox.IsAvailable) throw new DomainException("code.sandbox_disabled", "The test sandbox is not enabled on this server.");
        branch = string.IsNullOrWhiteSpace(branch) ? repo.DefaultBranch : branch.Trim();
        if (!(await store.BranchesAsync(repo, ct)).Contains(branch)) throw new DomainException("code.not_found", $"No branch '{branch}'.");
        var path = await store.CheckoutBranchAsync(repo, branch, ct);
        var sha = (await store.GitAsync(path, ["rev-parse", "HEAD"], ct)).Output.Trim();
        var started = clock.GetUtcNow();
        var result = await sandbox.RunAsync(new SandboxRequest(repo.SandboxImage, path, repo.TestCommand, sandbox.TestTimeout, 1.0, 2048), ct);
        var run = new TestRun
        {
            RepositoryId = repo.Id, Branch = branch, CommitSha = sha, Image = repo.SandboxImage, Command = repo.TestCommand,
            Status = result.Error is not null ? TestRunStatus.Error : result.TimedOut ? TestRunStatus.TimedOut : result.ExitCode == 0 ? TestRunStatus.Passed : TestRunStatus.Failed,
            ExitCode = result.ExitCode, Output = result.Error ?? result.Output, DurationMs = result.DurationMs, StartedAt = started,
        };
        db.TestRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return (await TestRunsAsync(repositoryId, null, ct)).First(t => t.Id == run.Id);
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
