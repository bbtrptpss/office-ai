using OfficeAI.Domain.Common;

namespace OfficeAI.Domain.Code;

/// <summary>A Git repository owned by a project. Stored locally (bare repo); remote hosting can be added later.</summary>
public class CodeRepository : Entity, IAuditable
{
    public Guid ProjectId { get; set; }
    public required string Name { get; set; }
    public string DefaultBranch { get; set; } = "main";

    /// <summary>Container image tests run in; must be on the sandbox allow-list.</summary>
    public required string SandboxImage { get; set; }

    /// <summary>Command run inside the sandbox to test the code. Set by people, never by agents.</summary>
    public required string TestCommand { get; set; }

    /// <summary>Optional lint / format check / static analysis command (e.g. "npx eslint . && npx prettier --check ."). Set by people, never by agents.</summary>
    public string? LintCommand { get; set; }

    /// <summary>Downloads dependencies before tests and lint, e.g. "npm ci"; it may reach the package registries only. Set by people, never by agents.</summary>
    public string? SetupCommand { get; set; }
    public bool IsDefault { get; set; } = true;

    /// <summary>Optional GitHub/GitLab (or any HTTPS Git host) mirror. Only the default branch is pushed, after people approve merges.</summary>
    public string? RemoteUrl { get; set; }

    /// <summary>Name of the server environment variable holding the access token (GIT_REMOTE_*). The token itself is never stored.</summary>
    public string? RemoteTokenEnvVar { get; set; }
    public DateTimeOffset? RemotePushedAt { get; set; }
    public string? RemotePushedSha { get; set; }
    public string? RemoteError { get; set; }
}

/// <summary>What a sandbox run checked: the test command or the lint / static analysis command.</summary>
public enum CheckKind
{
    Test = 1,
    Lint = 2,
}

public enum TestRunStatus
{
    Passed = 1,
    Failed = 2,
    TimedOut = 3,
    Error = 4,
}

/// <summary>Evidence: one execution of the repository's tests in the sandbox.</summary>
public class TestRun
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid RepositoryId { get; set; }
    public CheckKind Kind { get; set; } = CheckKind.Test;
    public Guid? TaskId { get; set; }
    public Guid? RunId { get; set; }
    public required string Branch { get; set; }
    public string? CommitSha { get; set; }
    public required string Image { get; set; }
    public required string Command { get; set; }
    public TestRunStatus Status { get; set; }
    public int? ExitCode { get; set; }
    public string? Output { get; set; }
    public int DurationMs { get; set; }
    public DateTimeOffset StartedAt { get; set; }
}

public enum MergeRequestStatus
{
    Open = 1,
    Merged = 2,
    Closed = 3,
    Conflict = 4,
}

public class MergeRequest : Entity, IAuditable
{
    public Guid ProjectId { get; set; }
    public Guid RepositoryId { get; set; }
    public Guid? TaskId { get; set; }
    public int Number { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public required string SourceBranch { get; set; }
    public required string TargetBranch { get; set; }
    public required string HeadSha { get; set; }
    public MergeRequestStatus Status { get; set; } = MergeRequestStatus.Open;
    public Guid? LatestTestRunId { get; set; }
    public Guid? AuthorEmployeeId { get; set; }
    public string? MergedSha { get; set; }
    public DateTimeOffset? MergedAt { get; set; }
}
