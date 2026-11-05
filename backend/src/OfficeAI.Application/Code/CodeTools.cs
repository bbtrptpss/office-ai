using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Application.Agents;
using OfficeAI.Application.Workflows;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Code;
using OfficeAI.Domain.Workflows;

namespace OfficeAI.Application.Code;

/// <summary>Shared plumbing: the task's repository, its workspace and the agent's Git identity.</summary>
public sealed class CodeWorkspace(ICodeRepositoryStore store)
{
    public ICodeRepositoryStore Store => store;

    public async Task<(CodeRepository Repo, string Path, string Branch)?> OpenAsync(ToolContext ctx, CancellationToken ct)
    {
        var repo = await ctx.Db.CodeRepositories.AsNoTracking().Where(r => r.ProjectId == ctx.ProjectId)
            .OrderByDescending(r => r.IsDefault).ThenBy(r => r.CreatedAt).FirstOrDefaultAsync(ct);
        if (repo is null) return null;
        var branch = Branches.ForTask(ctx.TaskId);
        return (repo, await store.EnsureWorkspaceAsync(repo, ctx.TaskId, branch, ct), branch);
    }

    public static async Task<(string Name, string Email)> AuthorAsync(ToolContext ctx, CancellationToken ct)
    {
        var name = await ctx.Db.Employees.Where(e => e.Id == ctx.EmployeeId).Select(e => e.DisplayName).FirstAsync(ct);
        return (name, $"agent-{ctx.EmployeeId:N}@agents.officeai.local");
    }

    public const string NoRepository = "This project has no code repository yet. Ask a person to create one.";
}

internal static class CodeSchemas
{
    public static readonly RunPurpose[] ReadPurposes = [RunPurpose.Work, RunPurpose.Review];
    public static JsonElement Obj(object properties, string[]? required = null) =>
        JsonSerializer.SerializeToElement(new { type = "object", properties, required = required ?? [], additionalProperties = false });
    public static string? Str(JsonElement o, string n) => o.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    public static int? Int(JsonElement o, string n) => o.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
}

public sealed class ListFilesTool(CodeWorkspace code) : IAgentTool
{
    public const string ToolId = "repo.list_files";

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0",
        "List files in your task's working copy of the repository (optionally under a directory). Excludes .git.",
        CodeSchemas.Obj(new { path = new { type = "string", maxLength = 500 } }), TimeSpan.FromSeconds(20), 30_000, false, false, CodeSchemas.ReadPurposes);

    public async Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct)
    {
        if (await code.OpenAsync(ctx, ct) is not { } ws) return ToolResult.Fail(CodeWorkspace.NoRepository);
        var start = ws.Path;
        var sub = CodeSchemas.Str(input, "path");
        if (!string.IsNullOrWhiteSpace(sub) && sub != ".")
        {
            start = WorkspacePaths.Resolve(ws.Path, sub, out var err) ?? "";
            if (err is not null) return ToolResult.Fail(err);
            if (!Directory.Exists(start)) return ToolResult.Fail($"No directory '{sub}'.");
        }
        var files = Directory.EnumerateFiles(start, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
            .Select(f => Path.GetRelativePath(ws.Path, f).Replace('\\', '/'))
            .Where(f => !f.StartsWith(".git/", StringComparison.Ordinal) && !f.Contains("/bin/") && !f.Contains("/obj/") && !f.Contains("node_modules/"))
            .Order(StringComparer.Ordinal).Take(1000).ToList();
        return ToolResult.Ok(new { branch = ws.Branch, files });
    }
}

public sealed class ReadFileTool(CodeWorkspace code) : IAgentTool
{
    public const string ToolId = "repo.read_file";

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0",
        "Read a text file from your task's working copy. Use startLine/endLine (1-based) for large files.",
        CodeSchemas.Obj(new { path = new { type = "string", maxLength = 500 }, startLine = new { type = "integer" }, endLine = new { type = "integer" } }, ["path"]),
        TimeSpan.FromSeconds(10), 120_000, false, false, CodeSchemas.ReadPurposes);

    public async Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct)
    {
        if (await code.OpenAsync(ctx, ct) is not { } ws) return ToolResult.Fail(CodeWorkspace.NoRepository);
        var path = WorkspacePaths.Resolve(ws.Path, CodeSchemas.Str(input, "path")!, out var err);
        if (path is null) return ToolResult.Fail(err!);
        if (!File.Exists(path)) return ToolResult.Fail("File not found.");
        if (new FileInfo(path).Length > 2_000_000) return ToolResult.Fail("The file is larger than 2 MB.");
        var lines = await File.ReadAllLinesAsync(path, ct);
        var from = Math.Max(1, CodeSchemas.Int(input, "startLine") ?? 1);
        var to = Math.Min(lines.Length, CodeSchemas.Int(input, "endLine") ?? lines.Length);
        var sb = new StringBuilder();
        for (var i = from; i <= to; i++) sb.Append(i).Append(": ").AppendLine(lines[i - 1]);
        return new ToolResult(true, $"{CodeSchemas.Str(input, "path")} (lines {from}-{to} of {lines.Length})\n{sb}");
    }
}

public sealed class SearchCodeTool(CodeWorkspace code) : IAgentTool
{
    public const string ToolId = "repo.search";

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0",
        "Search the working copy for a literal string; returns matching lines with file and line number.",
        CodeSchemas.Obj(new { query = new { type = "string", maxLength = 200 } }, ["query"]), TimeSpan.FromSeconds(20), 40_000, false, false, CodeSchemas.ReadPurposes);

    public async Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct)
    {
        if (await code.OpenAsync(ctx, ct) is not { } ws) return ToolResult.Fail(CodeWorkspace.NoRepository);
        var q = CodeSchemas.Str(input, "query") ?? "";
        if (q.Length == 0) return ToolResult.Fail("query must not be empty.");
        var r = await code.Store.GitAsync(ws.Path, ["grep", "-n", "-I", "--untracked", "--fixed-strings", "-e", q], ct);
        var lines = r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(200).ToList();
        return new ToolResult(true, lines.Count == 0 ? "No matches." : string.Join('\n', lines));
    }
}

public sealed class WriteFileTool(CodeWorkspace code) : IAgentTool
{
    public const string ToolId = "repo.write_file";
    public const int MaxBytes = 1_000_000;

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0",
        "Create or overwrite a text file in your task's working copy with the full new content. Changes stay on your task branch until merged.",
        CodeSchemas.Obj(new { path = new { type = "string", maxLength = 500 }, content = new { type = "string", maxLength = MaxBytes } }, ["path", "content"]),
        TimeSpan.FromSeconds(10), 1_000, false, true);

    public async Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct)
    {
        if (await code.OpenAsync(ctx, ct) is not { } ws) return ToolResult.Fail(CodeWorkspace.NoRepository);
        var path = WorkspacePaths.Resolve(ws.Path, CodeSchemas.Str(input, "path")!, out var err);
        if (path is null) return ToolResult.Fail(err!);
        var content = CodeSchemas.Str(input, "content") ?? "";
        if (Encoding.UTF8.GetByteCount(content) > MaxBytes) return ToolResult.Fail("Content is larger than 1 MB.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, new UTF8Encoding(false), ct);
        return ToolResult.Ok(new { written = CodeSchemas.Str(input, "path"), bytes = Encoding.UTF8.GetByteCount(content) });
    }
}

public sealed class DeleteFileTool(CodeWorkspace code) : IAgentTool
{
    public const string ToolId = "repo.delete_file";

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0",
        "Delete a file from your task's working copy (recoverable from Git until merged).",
        CodeSchemas.Obj(new { path = new { type = "string", maxLength = 500 } }, ["path"]), TimeSpan.FromSeconds(10), 1_000, false, true);

    public async Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct)
    {
        if (await code.OpenAsync(ctx, ct) is not { } ws) return ToolResult.Fail(CodeWorkspace.NoRepository);
        var path = WorkspacePaths.Resolve(ws.Path, CodeSchemas.Str(input, "path")!, out var err);
        if (path is null) return ToolResult.Fail(err!);
        if (!File.Exists(path)) return ToolResult.Fail("File not found.");
        File.Delete(path);
        return ToolResult.Ok(new { deleted = CodeSchemas.Str(input, "path") });
    }
}

public sealed class DiffTool(CodeWorkspace code) : IAgentTool
{
    public const string ToolId = "repo.diff";

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0",
        "Show what this task changed compared with the target branch: committed changes, uncommitted changes and new files.",
        CodeSchemas.Obj(new { }), TimeSpan.FromSeconds(30), 80_000, false, false, CodeSchemas.ReadPurposes);

    public async Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct)
    {
        if (await code.OpenAsync(ctx, ct) is not { } ws) return ToolResult.Fail(CodeWorkspace.NoRepository);
        await code.Store.GitAsync(ws.Path, ["fetch", "--quiet", "origin"], ct);
        var committed = await code.Store.GitAsync(ws.Path, ["diff", $"origin/{ws.Repo.DefaultBranch}...HEAD"], ct);
        var pending = await code.Store.GitAsync(ws.Path, ["diff", "HEAD"], ct);
        var status = await code.Store.GitAsync(ws.Path, ["status", "--porcelain"], ct);
        return new ToolResult(true,
            $"## Status\n{(status.Output.Length == 0 ? "clean" : status.Output)}\n## Committed on {ws.Branch}\n{committed.Output}\n## Not committed yet\n{pending.Output}");
    }
}

public sealed class CommitTool(CodeWorkspace code) : IAgentTool
{
    public const string ToolId = "repo.commit";

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0",
        "Commit every change in your working copy to your task branch and push it, with a clear message.",
        CodeSchemas.Obj(new { message = new { type = "string", maxLength = 2000 } }, ["message"]), TimeSpan.FromSeconds(60), 2_000, false, true);

    public async Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct)
    {
        if (await code.OpenAsync(ctx, ct) is not { } ws) return ToolResult.Fail(CodeWorkspace.NoRepository);
        var message = CodeSchemas.Str(input, "message")?.Trim();
        if (string.IsNullOrEmpty(message)) return ToolResult.Fail("message must not be empty.");
        var author = await CodeWorkspace.AuthorAsync(ctx, ct);
        await code.Store.GitAsync(ws.Path, ["add", "-A"], ct);
        var commit = await code.Store.GitAsync(ws.Path, ["commit", "-m", $"{message}\n\nRun: {ctx.RunId}"], ct, author);
        if (!commit.Ok)
            return commit.Output.Contains("nothing to commit") ? ToolResult.Fail("Nothing to commit.") : ToolResult.Fail(commit.Error.Trim());
        var push = await code.Store.GitAsync(ws.Path, ["push", "--quiet", "origin", $"HEAD:refs/heads/{ws.Branch}"], ct);
        if (!push.Ok) return ToolResult.Fail("Committed locally but could not push: " + push.Error.Trim());
        var sha = (await code.Store.GitAsync(ws.Path, ["rev-parse", "HEAD"], ct)).Output.Trim();
        return ToolResult.Ok(new { branch = ws.Branch, sha });
    }
}

/// <summary>Runs the repository's configured test command in the sandbox. The agent cannot choose the command.</summary>
public sealed class RunTestsTool(CodeWorkspace code, ISandbox sandbox) : IAgentTool
{
    public const string ToolId = "sandbox.run_tests";

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0",
        "Run the project's tests on your working copy in an isolated sandbox (no network). Returns pass/fail and the output; the result is kept as evidence.",
        CodeSchemas.Obj(new { }), TimeSpan.FromMinutes(11), 30_000, false, true, CodeSchemas.ReadPurposes);

    public Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct) => SandboxCheck.RunAsync(code, sandbox, ctx, CheckKind.Test, ct);
}

/// <summary>Runs the repository's lint / format check / static analysis command. Set by people; the agent cannot choose it.</summary>
public sealed class RunLintTool(CodeWorkspace code, ISandbox sandbox) : IAgentTool
{
    public const string ToolId = "sandbox.run_lint";

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0",
        "Run the project's lint, format check and static analysis on your working copy in an isolated sandbox (no network). " +
        "Returns pass/fail and the findings; the result is kept as evidence. When the repository has a lint command, merge requests need it to pass.",
        CodeSchemas.Obj(new { }), TimeSpan.FromMinutes(11), 30_000, false, true, CodeSchemas.ReadPurposes);

    public Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct) => SandboxCheck.RunAsync(code, sandbox, ctx, CheckKind.Lint, ct);
}

internal static class SandboxCheck
{
    public static async Task<ToolResult> RunAsync(CodeWorkspace code, ISandbox sandbox, ToolContext ctx, CheckKind kind, CancellationToken ct)
    {
        if (await code.OpenAsync(ctx, ct) is not { } ws) return ToolResult.Fail(CodeWorkspace.NoRepository);
        var command = kind == CheckKind.Lint ? ws.Repo.LintCommand : ws.Repo.TestCommand;
        if (string.IsNullOrWhiteSpace(command)) return ToolResult.Fail("This repository has no lint command. Ask a person to set one on the repository.");
        if (!sandbox.IsAvailable) return ToolResult.Fail("The test sandbox is not enabled on this server.");
        var clean = (await code.Store.GitAsync(ws.Path, ["status", "--porcelain"], ct)).Output.Length == 0;
        var sha = (await code.Store.GitAsync(ws.Path, ["rev-parse", "HEAD"], ct)).Output.Trim();

        var started = ctx.Clock.GetUtcNow();
        var result = await sandbox.RunAsync(new SandboxRequest(ws.Repo.SandboxImage, ws.Path, command, sandbox.TestTimeout, 1.0, 2048, ws.Repo.SetupCommand), ct);
        var lockfiles = await KeepGeneratedFilesAsync(ws.Path, result, ct);
        var status = result.Error is not null ? TestRunStatus.Error
            : result.TimedOut ? TestRunStatus.TimedOut
            : result.ExitCode == 0 ? TestRunStatus.Passed : TestRunStatus.Failed;
        var run = new TestRun
        {
            RepositoryId = ws.Repo.Id, Kind = kind, TaskId = ctx.TaskId, RunId = ctx.RunId, Branch = ws.Branch, CommitSha = clean ? sha : null,
            Image = ws.Repo.SandboxImage, Command = command, Status = status, ExitCode = result.ExitCode,
            Output = result.Error ?? result.Output, DurationMs = result.DurationMs, StartedAt = started,
        };
        ctx.Db.TestRuns.Add(run);
        await ctx.Db.SaveChangesAsync(ct);
        var tail = run.Output is { Length: > 8000 } o ? "…" + o[^8000..] : run.Output;
        var what = kind == CheckKind.Lint ? "Lint" : "Tests";
        var lockfileNote = lockfiles.Count == 0 ? "" : $"\nThe dependency install produced {string.Join(", ", lockfiles)}; it is now in your working copy. Commit it, then re-run on the committed code.";
        return new ToolResult(status == TestRunStatus.Passed,
            $"{what} {status} (exit {run.ExitCode?.ToString() ?? "-"}, {run.DurationMs} ms){(clean ? "" : " — uncommitted changes were included; commit and re-run before opening a merge request")}\n{tail}{lockfileNote}");
    }

    /// <summary>Lockfiles come back from the setup step into the working copy, where the agent commits them like any other change.</summary>
    private static async Task<List<string>> KeepGeneratedFilesAsync(string workspace, SandboxResult result, CancellationToken ct)
    {
        var changed = new List<string>();
        foreach (var (name, content) in result.GeneratedFiles ?? new Dictionary<string, string>())
        {
            if (WorkspacePaths.Resolve(workspace, name, out _) is not { } path) continue;
            if (File.Exists(path) && await File.ReadAllTextAsync(path, ct) == content) continue;
            await File.WriteAllTextAsync(path, content, ct);
            changed.Add(name);
        }
        return changed;
    }
}

/// <summary>Looks for credentials in what this task changed (committed and not yet committed). No sandbox needed.</summary>
public sealed class ScanSecretsTool(CodeWorkspace code) : IAgentTool
{
    public const string ToolId = "repo.scan_secrets";

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0",
        "Scan the lines your task added (committed and uncommitted, new files included) for credentials such as API keys, tokens, private keys " +
        "and hard-coded passwords. Reports file, line and kind, never the value. Merge requests are refused while committed changes contain one; " +
        "read secrets from configuration or environment variables instead.",
        CodeSchemas.Obj(new { }), TimeSpan.FromSeconds(30), 8_000, false, false, CodeSchemas.ReadPurposes);

    public async Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct)
    {
        if (await code.OpenAsync(ctx, ct) is not { } ws) return ToolResult.Fail(CodeWorkspace.NoRepository);
        await code.Store.GitAsync(ws.Path, ["fetch", "--quiet", "origin"], ct);
        // Intent-to-add makes new files show in the diff without staging their content.
        await code.Store.GitAsync(ws.Path, ["add", "--intent-to-add", "--all"], ct);
        var diff = await code.Store.GitAsync(ws.Path, ["diff", "--no-color", "--unified=0", $"origin/{ws.Repo.DefaultBranch}"], ct);
        if (!diff.Ok) return ToolResult.Fail(diff.Error.Trim());
        var findings = SecretScanner.ScanDiff(diff.Output);
        return findings.Count == 0
            ? ToolResult.Ok(new { findings = 0, result = "No credentials found in your changes." })
            : new ToolResult(false, $"{findings.Count} possible credential(s) found. Move them to configuration and remove them from the code:\n" + Format(findings));
    }

    internal static string Format(List<SecretFinding> findings) => string.Join("\n", findings.Select(f => $"- {f.File}:{f.Line} ({f.Rule})"));

    /// <summary>The gate used when opening a merge request: committed changes only.</summary>
    internal static async Task<List<SecretFinding>> CommittedFindingsAsync(CodeWorkspace code, string path, string defaultBranch, CancellationToken ct)
    {
        var diff = await code.Store.GitAsync(path, ["diff", "--no-color", "--unified=0", $"origin/{defaultBranch}...HEAD"], ct);
        return diff.Ok ? SecretScanner.ScanDiff(diff.Output) : [];
    }
}

/// <summary>Asks to merge the task branch. Requires the latest commit to have passing tests; a person approves the merge.</summary>
public sealed class OpenMergeRequestTool(CodeWorkspace code) : IAgentTool
{
    public const string ToolId = "repo.open_merge_request";

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0",
        "Open a merge request for your task branch into the main branch. Needs committed work whose latest commit passed sandbox.run_tests " +
        "(and sandbox.run_lint when the repository has a lint command) and contains no credentials. " +
        "A person reviews and approves the merge.",
        CodeSchemas.Obj(new { title = new { type = "string", maxLength = 200 }, description = new { type = "string", maxLength = 8000 } }, ["title"]),
        TimeSpan.FromSeconds(30), 2_000, false, true);

    public async Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct)
    {
        if (await code.OpenAsync(ctx, ct) is not { } ws) return ToolResult.Fail(CodeWorkspace.NoRepository);
        if ((await code.Store.GitAsync(ws.Path, ["status", "--porcelain"], ct)).Output.Length > 0)
            return ToolResult.Fail("You have uncommitted changes. Commit them and run the tests first.");
        await code.Store.GitAsync(ws.Path, ["fetch", "--quiet", "origin"], ct);
        var head = (await code.Store.GitAsync(ws.Path, ["rev-parse", "HEAD"], ct)).Output.Trim();
        var remoteHead = (await code.Store.GitAsync(ws.Path, ["rev-parse", $"origin/{ws.Branch}"], ct)).Output.Trim();
        if (head != remoteHead) return ToolResult.Fail("Your latest commit is not pushed. Use repo.commit.");
        var ahead = (await code.Store.GitAsync(ws.Path, ["rev-list", "--count", $"origin/{ws.Repo.DefaultBranch}..HEAD"], ct)).Output.Trim();
        if (ahead == "0") return ToolResult.Fail("The branch has no commits to merge.");

        var test = await ctx.Db.TestRuns.Where(t => t.RepositoryId == ws.Repo.Id && t.Kind == CheckKind.Test && t.Branch == ws.Branch && t.CommitSha == head)
            .OrderByDescending(t => t.StartedAt).FirstOrDefaultAsync(ct);
        if (test is not { Status: TestRunStatus.Passed })
            return ToolResult.Fail("The latest commit has no passing test run. Run sandbox.run_tests on the committed code first.");
        if (!string.IsNullOrWhiteSpace(ws.Repo.LintCommand))
        {
            var lint = await ctx.Db.TestRuns.Where(t => t.RepositoryId == ws.Repo.Id && t.Kind == CheckKind.Lint && t.Branch == ws.Branch && t.CommitSha == head)
                .OrderByDescending(t => t.StartedAt).Select(t => (TestRunStatus?)t.Status).FirstOrDefaultAsync(ct);
            if (lint != TestRunStatus.Passed)
                return ToolResult.Fail("The latest commit has no passing lint run. Run sandbox.run_lint on the committed code and fix the findings first.");
        }
        var secrets = await ScanSecretsTool.CommittedFindingsAsync(code, ws.Path, ws.Repo.DefaultBranch, ct);
        if (secrets.Count > 0)
            return ToolResult.Fail("Your commits contain possible credentials; move them to configuration first:\n" + ScanSecretsTool.Format(secrets));
        if (await ctx.Db.MergeRequests.AnyAsync(m => m.RepositoryId == ws.Repo.Id && m.SourceBranch == ws.Branch && m.Status == MergeRequestStatus.Open, ct))
            return ToolResult.Fail("A merge request for this branch is already open.");

        var number = (await ctx.Db.MergeRequests.Where(m => m.RepositoryId == ws.Repo.Id).MaxAsync(m => (int?)m.Number, ct) ?? 0) + 1;
        var mr = new MergeRequest
        {
            ProjectId = ctx.ProjectId, RepositoryId = ws.Repo.Id, TaskId = ctx.TaskId, Number = number, Title = CodeSchemas.Str(input, "title")!.Trim(),
            Description = CodeSchemas.Str(input, "description"), SourceBranch = ws.Branch, TargetBranch = ws.Repo.DefaultBranch, HeadSha = head,
            LatestTestRunId = test.Id, AuthorEmployeeId = ctx.EmployeeId,
        };
        ctx.Db.MergeRequests.Add(mr);
        var task = await ctx.Db.Tasks.AsNoTracking().FirstAsync(t => t.Id == ctx.TaskId, ct);
        ctx.Db.ApprovalRequests.Add(Approvals.Create(ApprovalKind.Merge, $"Merge !{number}: {mr.Title}", mr.Description,
            new { mergeRequestId = mr.Id, number, mr.SourceBranch, mr.TargetBranch, headSha = head, testRunId = test.Id }, ctx.Clock.GetUtcNow(),
            projectId: ctx.ProjectId, taskId: ctx.TaskId, workflowId: task.WorkflowExecutionId, runId: ctx.RunId, requestedByEmployee: ctx.EmployeeId));
        await ctx.Db.SaveChangesAsync(ct);
        return ToolResult.Ok(new { mergeRequest = number, status = "Open — waiting for approval" });
    }
}
