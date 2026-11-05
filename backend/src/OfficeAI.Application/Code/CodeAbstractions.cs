using OfficeAI.Domain.Code;

namespace OfficeAI.Application.Code;

public sealed record GitResult(int ExitCode, string Output, string Error)
{
    public bool Ok => ExitCode == 0;
}

public sealed record MergeOutcome(bool Merged, string? Sha, string? Error);

/// <summary>Git operations on locally stored repositories. Every call runs the git CLI without a shell.</summary>
public interface ICodeRepositoryStore
{
    /// <summary>Creates the bare repository with an initial commit on the default branch.</summary>
    Task InitAsync(CodeRepository repo, CancellationToken ct);

    /// <summary>A private clone for one task, on its own branch. Created on first use; returns its directory.</summary>
    Task<string> EnsureWorkspaceAsync(CodeRepository repo, Guid taskId, string branch, CancellationToken ct);

    Task<GitResult> GitAsync(string workdir, IReadOnlyList<string> args, CancellationToken ct, (string Name, string Email)? author = null);

    /// <summary>Merges <paramref name="source"/> into <paramref name="target"/> in a scratch clone and pushes the result.</summary>
    Task<MergeOutcome> MergeAsync(CodeRepository repo, string source, string target, string message, (string Name, string Email) author, CancellationToken ct);

    Task<IReadOnlyList<string>> BranchesAsync(CodeRepository repo, CancellationToken ct);

    /// <summary>A clean, up-to-date checkout of any branch for people to test (reset on every call).</summary>
    Task<string> CheckoutBranchAsync(CodeRepository repo, string branch, CancellationToken ct);

    /// <summary>Read-only listing / file read straight from a branch of the bare repository.</summary>
    Task<IReadOnlyList<string>> ListFilesAsync(CodeRepository repo, string branch, CancellationToken ct);
    Task<string?> ReadFileAsync(CodeRepository repo, string branch, string path, CancellationToken ct);

    /// <summary>Null when the URL may be used as a remote; otherwise why not (HTTPS only, no credentials in the URL).</summary>
    string? CheckRemoteUrl(string url);

    /// <summary>Creates the local repository as a copy of a remote one (all branches). Fails if <see cref="CodeRepository.DefaultBranch"/> is missing.</summary>
    Task ImportAsync(CodeRepository repo, string url, string? token, CancellationToken ct);

    /// <summary>Pushes one branch to the remote (never forced). Returns the pushed commit, or the error with the token removed.</summary>
    Task<(string? Sha, string? Error)> PushToRemoteAsync(CodeRepository repo, string url, string? token, string branch, CancellationToken ct);
}

/// <param name="SetupCommand">Downloads dependencies first, with access to the package registries only. The command itself never has a network.</param>
public sealed record SandboxRequest(string Image, string WorkspacePath, string Command, TimeSpan Timeout, double Cpus, int MemoryMb, string? SetupCommand = null);
public sealed record SandboxResult(int? ExitCode, string Output, bool TimedOut, int DurationMs, string? Error);

/// <summary>Runs untrusted code in a throwaway, network-less, resource-limited container.</summary>
public interface ISandbox
{
    bool IsAvailable { get; }
    IReadOnlyCollection<string> AllowedImages { get; }
    TimeSpan TestTimeout { get; }
    Task<SandboxResult> RunAsync(SandboxRequest request, CancellationToken ct);
}

public static class Branches
{
    /// <summary>One branch per task. The whole id: ids start with their creation time, so a shortened one is shared by tasks created together.</summary>
    public static string ForTask(Guid taskId) => $"task/{taskId:N}";
}

public static class WorkspacePaths
{
    /// <summary>
    /// Resolves an agent-supplied relative path inside the workspace. Rejects absolute paths, '..', the .git directory
    /// and anything that resolves outside the workspace (including through symbolic links).
    /// </summary>
    public static string? Resolve(string workspace, string relative, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(relative)) { error = "path is required."; return null; }
        var normalized = relative.Replace('\\', '/').Trim();
        if (normalized.StartsWith('/') || Path.IsPathRooted(normalized) || normalized.Contains(':'))
        {
            error = "Use a path relative to the repository root.";
            return null;
        }
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(p => p == ".." || p == "."))
        {
            error = "'.' and '..' are not allowed in paths.";
            return null;
        }
        if (parts.Length > 0 && parts[0].Equals(".git", StringComparison.OrdinalIgnoreCase))
        {
            error = "The .git directory is off limits.";
            return null;
        }
        var root = Path.GetFullPath(workspace);
        var full = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal) && full != root)
        {
            error = "The path leaves the repository.";
            return null;
        }
        // No hop through a link anywhere along the way.
        var current = root;
        foreach (var p in parts)
        {
            current = Path.Combine(current, p);
            if ((File.Exists(current) || Directory.Exists(current)) && new FileInfo(current).Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                error = "Symbolic links are not followed.";
                return null;
            }
        }
        return full;
    }
}
