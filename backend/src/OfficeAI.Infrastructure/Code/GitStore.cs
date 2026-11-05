using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OfficeAI.Application.Code;
using OfficeAI.Domain.Code;

namespace OfficeAI.Infrastructure.Code;

public class RepositoryOptions
{
    /// <summary>Directory holding bare repositories and task workspaces.</summary>
    public string Root { get; set; } = "data/repos";
    public int GitTimeoutSeconds { get; set; } = 60;

    /// <summary>Allow file:// and local paths as remotes. Tests only; production remotes must be HTTPS.</summary>
    public bool AllowLocalRemotes { get; set; }
}

/// <summary>
/// Layout under Root: {repoId}/origin.git (bare), {repoId}/work/{taskId} (task clones), {repoId}/merge (scratch clone).
/// Git runs with prompts, hooks, external tools and symlinks disabled.
/// </summary>
public sealed class GitStore(IOptions<RepositoryOptions> options, ILogger<GitStore> log) : ICodeRepositoryStore, IRepositoryLayout
{
    private const int MaxOutput = 200_000;
    private static readonly (string Name, string Email) System = ("AI Software House", "system@officeai.local");

    private string RepoDir(CodeRepository repo) => Path.GetFullPath(Path.Combine(options.Value.Root, repo.Id.ToString("N")));
    private string Origin(CodeRepository repo) => Path.Combine(RepoDir(repo), "origin.git");
    public string OriginPath(CodeRepository repo) => Origin(repo);

    public Task<GitResult> GitAsync(string workdir, IReadOnlyList<string> args, CancellationToken ct, (string Name, string Email)? author = null) =>
        RunGitAsync(workdir, args, ct, author, null, null);

    private async Task<GitResult> RunGitAsync(string workdir, IReadOnlyList<string> args, CancellationToken ct, (string Name, string Email)? author,
        IReadOnlyDictionary<string, string>? env, IReadOnlyList<string>? extraConfig)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workdir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // Hardened defaults: no hooks, no symlinks, no credential prompts, no pager, no external diff/merge tools.
        foreach (var c in new[] { "core.hooksPath=/dev/null", "core.symlinks=false", "core.autocrlf=false", "core.quotepath=false",
                     "credential.helper=", "protocol.file.allow=always", "advice.detachedHead=false", "init.defaultBranch=main" })
        {
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(c);
        }
        foreach (var c in extraConfig ?? [])
        {
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(c);
        }
        if (author is { } a)
        {
            psi.ArgumentList.Add("-c"); psi.ArgumentList.Add($"user.name={a.Name}");
            psi.ArgumentList.Add("-c"); psi.ArgumentList.Add($"user.email={a.Email}");
        }
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["LC_ALL"] = "C.UTF-8";
        foreach (var (k, v) in env ?? new Dictionary<string, string>()) psi.Environment[k] = v;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.GitTimeoutSeconds));
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start git.");
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw;
        }
        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0) log.LogDebug("git {Args} failed: {Error}", string.Join(' ', args), error);
        return new GitResult(process.ExitCode, Cap(output), Cap(error));
    }

    private static string Cap(string s) => s.Length <= MaxOutput ? s : s[..MaxOutput] + "\n…[truncated]";

    private async Task Must(string dir, CancellationToken ct, (string, string)? author, params string[] args)
    {
        var r = await GitAsync(dir, args, ct, author);
        if (!r.Ok) throw new InvalidOperationException($"git {args[0]} failed: {r.Error.Trim()}");
    }

    public async Task InitAsync(CodeRepository repo, CancellationToken ct)
    {
        var dir = RepoDir(repo);
        if (Directory.Exists(Origin(repo))) return;
        Directory.CreateDirectory(dir);
        await Must(dir, ct, null, "init", "--bare", "--initial-branch", repo.DefaultBranch, Origin(repo));

        var seed = Path.Combine(dir, "seed");
        if (Directory.Exists(seed)) Directory.Delete(seed, recursive: true);
        await Must(dir, ct, null, "clone", Origin(repo), seed);
        await File.WriteAllTextAsync(Path.Combine(seed, "README.md"), $"# {repo.Name}\n\nCreated by AI Software House.\n", ct);
        await Must(seed, ct, System, "checkout", "-B", repo.DefaultBranch);
        await Must(seed, ct, System, "add", "-A");
        await Must(seed, ct, System, "commit", "-m", "Initial commit");
        await Must(seed, ct, System, "push", "origin", repo.DefaultBranch);
        DeleteDirectory(seed);
    }

    public async Task<string> EnsureWorkspaceAsync(CodeRepository repo, Guid taskId, string branch, CancellationToken ct)
    {
        var work = Path.Combine(RepoDir(repo), "work", taskId.ToString("N"));
        if (!Directory.Exists(Path.Combine(work, ".git")))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(work)!);
            await Must(RepoDir(repo), ct, null, "clone", "--no-hardlinks", Origin(repo), work);
            var remoteBranch = await GitAsync(work, ["ls-remote", "--exit-code", "--heads", "origin", branch], ct);
            if (remoteBranch.Ok) await Must(work, ct, null, "checkout", "-B", branch, $"origin/{branch}");
            else await Must(work, ct, null, "checkout", "-B", branch, $"origin/{repo.DefaultBranch}");
        }
        else if ((await GitAsync(work, ["branch", "--show-current"], ct)).Output.Trim() != branch)
        {
            // A workspace from before branch names used the whole task id. Moving keeps its uncommitted work.
            await Must(work, ct, null, "checkout", "-B", branch);
        }
        return work;
    }

    public async Task<MergeOutcome> MergeAsync(CodeRepository repo, string source, string target, string message, (string Name, string Email) author, CancellationToken ct)
    {
        var scratch = Path.Combine(RepoDir(repo), "merge", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(scratch)!);
        try
        {
            await Must(RepoDir(repo), ct, null, "clone", "--no-hardlinks", Origin(repo), scratch);
            await Must(scratch, ct, null, "checkout", "-B", target, $"origin/{target}");
            var merge = await GitAsync(scratch, ["merge", "--no-ff", "-m", message, $"origin/{source}"], ct, author);
            if (!merge.Ok)
            {
                await GitAsync(scratch, ["merge", "--abort"], ct);
                return new MergeOutcome(false, null, (merge.Output + merge.Error).Trim());
            }
            await Must(scratch, ct, null, "push", "origin", target);
            var sha = (await GitAsync(scratch, ["rev-parse", "HEAD"], ct)).Output.Trim();
            return new MergeOutcome(true, sha, null);
        }
        finally
        {
            DeleteDirectory(scratch);
        }
    }

    public async Task<string> CheckoutBranchAsync(CodeRepository repo, string branch, CancellationToken ct)
    {
        var safe = string.Concat(branch.Select(c => char.IsLetterOrDigit(c) ? c : '-'));
        var dir = Path.Combine(RepoDir(repo), "work", "branch-" + safe);
        if (!Directory.Exists(Path.Combine(dir, ".git")))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dir)!);
            await Must(RepoDir(repo), ct, null, "clone", "--no-hardlinks", Origin(repo), dir);
        }
        await Must(dir, ct, null, "fetch", "--quiet", "origin");
        await Must(dir, ct, null, "checkout", "-B", branch, $"origin/{branch}");
        await Must(dir, ct, null, "reset", "--hard", $"origin/{branch}");
        await Must(dir, ct, null, "clean", "-fdx");
        return dir;
    }

    public async Task<IReadOnlyList<string>> BranchesAsync(CodeRepository repo, CancellationToken ct)
    {
        var r = await GitAsync(Origin(repo), ["for-each-ref", "--format=%(refname:short)", "refs/heads"], ct);
        return r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public async Task<IReadOnlyList<string>> ListFilesAsync(CodeRepository repo, string branch, CancellationToken ct)
    {
        var r = await GitAsync(Origin(repo), ["ls-tree", "-r", "--name-only", branch], ct);
        return r.Ok ? r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries) : [];
    }

    public async Task<string?> ReadFileAsync(CodeRepository repo, string branch, string path, CancellationToken ct)
    {
        if (path.Contains("..")) return null;
        var r = await GitAsync(Origin(repo), ["show", $"{branch}:{path}"], ct);
        return r.Ok ? r.Output : null;
    }

    // ---------------- remotes (GitHub / GitLab / any HTTPS host) ----------------

    public string? CheckRemoteUrl(string url)
    {
        if (options.Value.AllowLocalRemotes && (url.StartsWith("file://", StringComparison.Ordinal) || Path.IsPathRooted(url))) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return "The remote must be an https:// URL.";
        if (!string.IsNullOrEmpty(uri.UserInfo)) return "Put the token in a GIT_REMOTE_* environment variable, not in the URL.";
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return "The remote URL must not have a query or fragment.";
        return null;
    }

    /// <summary>
    /// The token goes in an HTTP header passed through environment variables (GIT_CONFIG_*), so it never shows up in the process
    /// list, the URL or git's own config. Only HTTPS (or local paths in tests) may be contacted.
    /// </summary>
    private (Dictionary<string, string> Env, string[] Config) RemoteAccess(string? token)
    {
        var env = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(token))
        {
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"x-access-token:{token}"));
            env["GIT_CONFIG_COUNT"] = "1";
            env["GIT_CONFIG_KEY_0"] = "http.extraHeader";
            env["GIT_CONFIG_VALUE_0"] = $"Authorization: Basic {basic}";
        }
        string[] config = options.Value.AllowLocalRemotes
            ? ["protocol.allow=never", "protocol.https.allow=always", "protocol.file.allow=always"]
            : ["protocol.allow=never", "protocol.https.allow=always", "protocol.file.allow=never"];
        return (env, config);
    }

    private static string Scrub(string text, string? token)
    {
        if (string.IsNullOrEmpty(token)) return text;
        return text.Replace(token, "***").Replace(Convert.ToBase64String(Encoding.UTF8.GetBytes($"x-access-token:{token}")), "***");
    }

    public async Task ImportAsync(CodeRepository repo, string url, string? token, CancellationToken ct)
    {
        if (CheckRemoteUrl(url) is { } problem) throw new InvalidOperationException(problem);
        var dir = RepoDir(repo);
        if (Directory.Exists(Origin(repo))) throw new InvalidOperationException("The repository already exists on disk.");
        Directory.CreateDirectory(dir);
        var (env, config) = RemoteAccess(token);
        var clone = await RunGitAsync(dir, ["clone", "--bare", "--no-hardlinks", url, Origin(repo)], ct, null, env, config);
        if (!clone.Ok)
        {
            DeleteDirectory(Origin(repo));
            throw new InvalidOperationException($"Import failed: {Scrub(clone.Error.Trim(), token)}");
        }
        // A bare clone keeps the URL as "origin"; the local repository is the source of truth, so drop it.
        await GitAsync(Origin(repo), ["remote", "remove", "origin"], ct);
        if (!(await BranchesAsync(repo, ct)).Contains(repo.DefaultBranch))
        {
            DeleteDirectory(Origin(repo));
            throw new InvalidOperationException($"The remote has no '{repo.DefaultBranch}' branch.");
        }
    }

    public async Task<(string? Sha, string? Error)> PushToRemoteAsync(CodeRepository repo, string url, string? token, string branch, CancellationToken ct)
    {
        if (CheckRemoteUrl(url) is { } problem) return (null, problem);
        var sha = (await GitAsync(Origin(repo), ["rev-parse", $"refs/heads/{branch}"], ct)).Output.Trim();
        var (env, config) = RemoteAccess(token);
        var push = await RunGitAsync(Origin(repo), ["push", "--porcelain", url, $"refs/heads/{branch}:refs/heads/{branch}"], ct, null, env, config);
        return push.Ok ? (sha, null) : (null, Scrub((push.Error + "\n" + push.Output).Trim(), token));
    }

    /// <summary>Git marks pack files read-only; clear that before deleting.</summary>
    private static void DeleteDirectory(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(dir, recursive: true);
    }
}
