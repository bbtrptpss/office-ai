using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Agents;
using OfficeAI.Application.Code;
using OfficeAI.Application.Organization;
using OfficeAI.Application.Work;
using OfficeAI.Application.Workflows;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Code;
using OfficeAI.Domain.Workflows;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class CodeTests(ApiFixture fx)
{
    private const string Image = "alpine:3.20";

    private async Task<(HttpClient Ceo, EmployeeDto Agent, TaskDto Task, ProjectDto Project, RepositoryDto Repo)> SetupAsync(
        string roleCode, AiResponse[] script, string testCommand = "sh test.sh")
    {
        var ceo = await fx.CeoAsync();
        var role = (await ceo.GetAsync<List<RoleDefinitionDto>>("/api/role-definitions")).First(r => r.Code == roleCode);
        var agent = await ceo.PostAsync<EmployeeDto>("/api/employees",
            new { displayName = $"{roleCode} {Guid.NewGuid():N}"[..24], kind = "AI", departmentId = role.DepartmentId, roleDefinitionId = role.Id });
        var modelId = $"script-{Guid.NewGuid():N}";
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AiModels.Add(new AiModel { Provider = ScriptedProvider.ProviderName, ModelId = modelId, DisplayName = modelId });
            await db.SaveChangesAsync();
        }
        fx.Scripted.Register(modelId, ScriptedProvider.Sequence(script));
        var c = await ceo.GetAsync<AgentConfigDto>($"/api/agents/{agent.Id}/config");
        (await ceo.PutAsJsonAsync($"/api/agents/{agent.Id}/config", new
        {
            provider = ScriptedProvider.ProviderName, modelId, effort = "Medium", c.MaxTokensPerCall, maxTurnsPerRun = 30, maxToolCallsPerRun = 40,
            c.MaxTokensPerRun, c.TimeoutSeconds, c.MonthlyBudgetUsd, isEnabled = true,
        }, ApiFixture.Json)).EnsureSuccessStatusCode();

        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"CD-{Guid.NewGuid():N}"[..14], name = "Code project", status = "Active" });
        var repo = await ceo.PostAsync<RepositoryDto>($"/api/projects/{project.Id}/repositories", new { name = "app", sandboxImage = Image, testCommand });
        var task = await ceo.PostAsync<TaskDto>($"/api/projects/{project.Id}/tasks", new { title = "Implement" });
        await ceo.PostAsync<TaskDto>($"/api/tasks/{task.Id}/assign", new { employeeId = agent.Id });
        return (ceo, agent, task, project, repo);
    }

    private static async Task<AgentRunDetailDto> RunAsync(HttpClient c, Guid taskId)
    {
        var run = await c.PostAsync<AgentRunDto>($"/api/tasks/{taskId}/runs", new { });
        for (var i = 0; i < 300; i++)
        {
            var d = await c.GetAsync<AgentRunDetailDto>($"/api/agent-runs/{run.Id}");
            if (d.Run.Status is not (AgentRunStatus.Queued or AgentRunStatus.Running)) return d;
            await Task.Delay(100);
        }
        throw new TimeoutException();
    }

    private static AiResponse Write(string path, string content) => ScriptedProvider.Call("repo_write_file", new { path, content });
    private static AiResponse Commit(string message) => ScriptedProvider.Call("repo_commit", new { message });
    private static AiResponse Test() => ScriptedProvider.Call("sandbox_run_tests", new { });
    private static AiResponse MergeMain(bool abort = false) => ScriptedProvider.Call("repo_merge_main", abort ? new { abort } : new { });

    /// <summary>Someone else's work lands on main while the task branch is open.</summary>
    private async Task PushToMainAsync(Guid repoId, string file, string content)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<ICodeRepositoryStore>();
        var repo = await db.CodeRepositories.AsNoTracking().FirstAsync(r => r.Id == repoId);
        var dir = await store.CheckoutBranchAsync(repo, repo.DefaultBranch, default);
        await File.WriteAllTextAsync(Path.Combine(dir, file), content);
        Assert.True((await store.GitAsync(dir, ["add", "-A"], default)).Ok);
        Assert.True((await store.GitAsync(dir, ["commit", "-m", $"Change {file} on main"], default, ("Someone", "someone@example.test"))).Ok);
        Assert.True((await store.GitAsync(dir, ["push", "--quiet", "origin", $"HEAD:refs/heads/{repo.DefaultBranch}"], default)).Ok);
    }

    [Fact]
    public async Task A_branch_behind_main_merges_main_resolves_the_conflict_and_only_then_opens_a_merge_request()
    {
        var (ceo, _, task, _, repo) = await SetupAsync("BACKEND",
        [
            Write("app.txt", "mine\n"),
            Write("test.sh", "grep -q mine app.txt && grep -q theirs other.txt && echo BOTH_OK"),
            Commit("My change"),
            ScriptedProvider.Text("Paused."),
            // main moved meanwhile
            OpenMr("Refused: behind main"),
            MergeMain(),
            Commit("Commit with the markers still in"),
            Write("app.txt", "mine\n"),
            Commit("Resolve the conflict"),
            MergeMain(),
            Test(),
            OpenMr("Mine, on top of main"),
        ]);
        await RunAsync(ceo, task.Id);
        await PushToMainAsync(repo.Id, "app.txt", "theirs\n");
        await PushToMainAsync(repo.Id, "other.txt", "theirs\n");

        var run = await RunAsync(ceo, task.Id);
        var calls = run.ToolExecutions.OrderBy(x => x.StartedAt).ToList();
        Assert.Contains("has changed since your branch started", calls[0].Error);
        Assert.Contains("stopped on conflicts in: app.txt", calls[1].Error);
        Assert.Contains("still contain conflict markers", calls[2].Error);
        Assert.Equal(ToolExecutionStatus.Succeeded, calls[4].Status);
        Assert.Contains("already contains the latest", calls[5].Output);
        Assert.Contains("BOTH_OK", calls[6].Output); // tests ran on the combined code
        Assert.Equal(ToolExecutionStatus.Succeeded, calls[7].Status);
    }

    [Fact]
    public async Task An_approved_merge_request_is_not_merged_when_main_moved_after_its_tests()
    {
        var (ceo, _, task, project, repo) = await SetupAsync("BACKEND",
        [
            Write("app.txt", "hello\n"),
            Write("test.sh", "grep -q hello app.txt && echo OK"),
            Commit("Add greeting"),
            Test(),
            OpenMr("Greeting"),
        ]);
        await RunAsync(ceo, task.Id);
        await PushToMainAsync(repo.Id, "unrelated.txt", "no conflict\n");

        var approval = Assert.Single(await ceo.GetAsync<List<ApprovalDto>>($"/api/approvals?status=Pending&taskId={task.Id}"));
        (await ceo.PostAsJsonAsync($"/api/approvals/{approval.Id}/decision", new { approve = true, payloadHash = approval.PayloadHash })).EnsureSuccessStatusCode();

        var mr = Assert.Single(await ceo.GetAsync<List<MergeRequestDto>>($"/api/projects/{project.Id}/merge-requests"));
        Assert.Equal(MergeRequestStatus.Closed, mr.Status);
        Assert.DoesNotContain("app.txt", await ceo.GetAsync<List<string>>($"/api/repositories/{repo.Id}/files"));
        var reviews = await ceo.GetAsync<List<ReviewDto>>($"/api/tasks/{task.Id}/reviews");
        Assert.Contains(reviews, r => r.Comments.Contains("repo.merge_main"));
    }

    private static AiResponse OpenMr(string title) => ScriptedProvider.Call("repo_open_merge_request", new { title, description = "Adds the greeting" });

    [Fact]
    public async Task Agent_codes_on_its_branch_tests_in_the_sandbox_and_a_person_merges()
    {
        var (ceo, agent, task, project, repo) = await SetupAsync("BACKEND",
        [
            Write("app.txt", "hello\n"),
            Write("test.sh", "grep -q hello app.txt && echo GREETING_OK"),
            Commit("Add greeting"),
            Test(),
            OpenMr("Greeting"),
        ]);
        Assert.Equal(["main"], await ceo.GetAsync<List<string>>($"/api/repositories/{repo.Id}/branches"));

        var detail = await RunAsync(ceo, task.Id);
        Assert.All(detail.ToolExecutions, x => Assert.Equal(ToolExecutionStatus.Succeeded, x.Status));

        var testRun = Assert.Single(await ceo.GetAsync<List<TestRunDto>>($"/api/repositories/{repo.Id}/test-runs?taskId={task.Id}"));
        Assert.Equal(TestRunStatus.Passed, testRun.Status);
        Assert.Contains("GREETING_OK", testRun.Output);
        Assert.NotNull(testRun.CommitSha);

        var branches = await ceo.GetAsync<List<string>>($"/api/repositories/{repo.Id}/branches");
        Assert.Contains(Branches.ForTask(task.Id), branches);
        Assert.DoesNotContain("app.txt", await ceo.GetAsync<List<string>>($"/api/repositories/{repo.Id}/files")); // not on main yet

        var mr = Assert.Single(await ceo.GetAsync<List<MergeRequestDto>>($"/api/projects/{project.Id}/merge-requests"));
        Assert.Equal(MergeRequestStatus.Open, mr.Status);
        Assert.Equal(TestRunStatus.Passed, mr.LatestTestStatus);
        Assert.Equal(agent.DisplayName, mr.AuthorName);
        var diff = (await ceo.GetAsync<MergeRequestDetailDto>($"/api/merge-requests/{mr.Id}")).Diff;
        Assert.Contains("+hello", diff);

        var approval = Assert.Single(await ceo.GetAsync<List<ApprovalDto>>($"/api/approvals?status=Pending&taskId={task.Id}"));
        Assert.Equal(ApprovalKind.Merge, approval.Kind);
        await ceo.PostAsync<TaskDto>($"/api/tasks/{task.Id}/status", new { status = "InReview" });
        (await ceo.PostAsJsonAsync($"/api/approvals/{approval.Id}/decision", new { approve = true, payloadHash = approval.PayloadHash })).EnsureSuccessStatusCode();

        mr = (await ceo.GetAsync<MergeRequestDetailDto>($"/api/merge-requests/{mr.Id}")).MergeRequest;
        Assert.Equal(MergeRequestStatus.Merged, mr.Status);
        Assert.Contains("app.txt", await ceo.GetAsync<List<string>>($"/api/repositories/{repo.Id}/files"));
        var res = await ceo.GetAsync($"/api/repositories/{repo.Id}/file?path=app.txt");
        Assert.Equal("hello\n", await res.Content.ReadAsStringAsync());
        // Outside a workflow nothing else would end the task: approving its merge does.
        Assert.Equal(OfficeAI.Domain.Work.WorkTaskStatus.Done, (await ceo.GetAsync<TaskDto>($"/api/tasks/{task.Id}")).Status);

        // A branch must exist: anything else, above all a value git would read as an option, never reaches git.
        var marker = Path.Combine(Path.GetTempPath(), $"officeai-{Guid.NewGuid():N}");
        foreach (var branch in new[] { $"--output={marker}", "--help", "no-such-branch" })
        {
            var q = Uri.EscapeDataString(branch);
            Assert.Equal(HttpStatusCode.BadRequest, (await ceo.GetAsync($"/api/repositories/{repo.Id}/file?path=app.txt&branch={q}")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await ceo.GetAsync($"/api/repositories/{repo.Id}/files?branch={q}")).StatusCode);
        }
        Assert.Empty(Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(marker) + "*"));
        Assert.Equal("hello\n", await (await ceo.GetAsync($"/api/repositories/{repo.Id}/file?path=app.txt&branch=main")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Paths_outside_the_workspace_and_the_git_directory_are_refused()
    {
        var (ceo, _, task, _, _) = await SetupAsync("BACKEND",
        [
            Write("../escape.txt", "x"),
            Write(".git/config", "x"),
            Write("/etc/passwd", "x"),
            Write("C:/Windows/evil.txt", "x"),
            ScriptedProvider.Call("repo_read_file", new { path = "../../origin.git/config" }),
            Write("src/ok.txt", "fine"),
        ]);
        var detail = await RunAsync(ceo, task.Id);
        var results = detail.ToolExecutions.Select(x => x.Status).ToList();
        Assert.Equal([ToolExecutionStatus.Failed, ToolExecutionStatus.Failed, ToolExecutionStatus.Failed, ToolExecutionStatus.Failed,
            ToolExecutionStatus.Failed, ToolExecutionStatus.Succeeded], results);
        Assert.False(File.Exists(Path.Combine(fx.RepoRoot, "escape.txt")));
    }

    [Fact]
    public async Task Sandbox_has_no_network_and_cannot_modify_the_workspace()
    {
        var (ceo, _, task, _, repo) = await SetupAsync("BACKEND",
        [
            Write("test.sh", """
                if wget -q -T 3 -O /dev/null http://1.1.1.1; then echo NETWORK_REACHABLE; exit 1; fi
                if touch /src/hacked 2>/dev/null; then echo WORKSPACE_WRITABLE; exit 1; fi
                if [ "$(id -u)" = "0" ]; then echo RUNNING_AS_ROOT; exit 1; fi
                echo ISOLATED
                """),
            Test(),
        ]);
        await RunAsync(ceo, task.Id);
        var run = Assert.Single(await ceo.GetAsync<List<TestRunDto>>($"/api/repositories/{repo.Id}/test-runs?taskId={task.Id}"));
        Assert.True(run.Status == TestRunStatus.Passed, run.Output);
        Assert.Contains("ISOLATED", run.Output);
        Assert.Null(run.CommitSha); // uncommitted changes are never treated as evidence for a commit
    }

    [Fact]
    public async Task Setup_prepares_the_workspace_and_the_tests_still_run_without_a_network()
    {
        var (ceo, _, _, _, repo) = await SetupAsync("BACKEND", [], testCommand: """
            test "$(cat deps/lib.txt)" = "installed" || { echo DEPS_MISSING; exit 1; }
            if wget -q -T 3 -O /dev/null http://1.1.1.1; then echo NETWORK_REACHABLE; exit 1; fi
            if touch /src/hacked 2>/dev/null; then echo WORKSPACE_WRITABLE; exit 1; fi
            [ "$(id -u)" != "0" ] || { echo RUNNING_AS_ROOT; exit 1; }
            echo ISOLATED
            """);
        repo = await ceo.PutAsync<RepositoryDto>($"/api/repositories/{repo.Id}", new { sandboxImage = Image, testCommand = repo.TestCommand, setupCommand = "mkdir deps && echo installed > deps/lib.txt" });
        Assert.StartsWith("mkdir deps", repo.SetupCommand);

        var run = await ceo.PostAsync<TestRunDto>($"/api/repositories/{repo.Id}/test-runs", new { });
        Assert.True(run.Status == TestRunStatus.Passed, run.Output);
        Assert.Contains("ISOLATED", run.Output);
        Assert.Contains("--- setup:", run.Output);
    }

    [Fact]
    public async Task A_lockfile_from_setup_reaches_the_working_copy_for_the_agent_to_commit()
    {
        var (ceo, _, task, _, repo) = await SetupAsync("BACKEND",
        [
            Test(),
            ScriptedProvider.Call("repo_read_file", new { path = "package-lock.json" }),
            Commit("Add the lockfile"),
            Test(),
        ], testCommand: "echo RAN");
        await ceo.PutAsync<RepositoryDto>($"/api/repositories/{repo.Id}", new { sandboxImage = Image, testCommand = repo.TestCommand, setupCommand = "echo '{\"lockfileVersion\":3}' > package-lock.json; echo other > other.lock" });

        var run = await RunAsync(ceo, task.Id);
        var calls = run.ToolExecutions.OrderBy(x => x.StartedAt).ToList();
        Assert.Contains("package-lock.json; it is now in your working copy", calls[0].Output);
        Assert.Contains("lockfileVersion", calls[1].Output);
        Assert.Equal(ToolExecutionStatus.Succeeded, calls[2].Status);
        Assert.DoesNotContain("it is now in your working copy", calls[3].Output); // unchanged: no loop of commit and re-run

        var runs = await ceo.GetAsync<List<TestRunDto>>($"/api/repositories/{repo.Id}/test-runs?taskId={task.Id}");
        Assert.NotNull(runs.OrderBy(r => r.StartedAt).Last().CommitSha); // the committed code is clean evidence

        var branch = Branches.ForTask(task.Id);
        var files = await ceo.GetAsync<List<string>>($"/api/repositories/{repo.Id}/files?branch={Uri.EscapeDataString(branch)}");
        Assert.Contains("package-lock.json", files);
        Assert.DoesNotContain("other.lock", files); // only the known lockfiles come back
    }

    [Fact]
    public async Task A_failed_setup_fails_the_run_without_running_the_tests_and_leaves_nothing_behind()
    {
        var (ceo, _, _, _, repo) = await SetupAsync("BACKEND", [], testCommand: "echo TESTS_RAN");
        await ceo.PutAsync<RepositoryDto>($"/api/repositories/{repo.Id}", new { sandboxImage = Image, testCommand = repo.TestCommand, setupCommand = "echo cannot download; exit 3" });

        var run = await ceo.PostAsync<TestRunDto>($"/api/repositories/{repo.Id}/test-runs", new { });
        Assert.Equal(TestRunStatus.Failed, run.Status);
        Assert.Equal(3, run.ExitCode);
        Assert.Contains("cannot download", run.Output);
        Assert.Contains("setup failed", run.Output);
        Assert.DoesNotContain("TESTS_RAN", run.Output);

        using var volumes = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("docker", ["volume", "ls", "-q", "--filter", "name=officeai-sbx-work-"]) { RedirectStandardOutput = true })!;
        var left = await volumes.StandardOutput.ReadToEndAsync();
        await volumes.WaitForExitAsync();
        Assert.Equal("", left.Trim());
    }

    [Fact]
    public async Task Runaway_tests_are_stopped_at_the_timeout()
    {
        var (ceo, _, task, _, repo) = await SetupAsync("BACKEND", [Test()], testCommand: "sleep 60");
        await RunAsync(ceo, task.Id);
        var run = Assert.Single(await ceo.GetAsync<List<TestRunDto>>($"/api/repositories/{repo.Id}/test-runs?taskId={task.Id}"));
        Assert.Equal(TestRunStatus.TimedOut, run.Status);
        Assert.True(run.DurationMs < 30_000);
    }

    [Fact]
    public async Task Merge_request_needs_passing_tests_on_the_latest_commit()
    {
        var (ceo, _, task, project, _) = await SetupAsync("BACKEND",
        [
            Write("app.txt", "bye\n"),
            Write("test.sh", "grep -q hello app.txt"),
            Commit("Wrong greeting"),
            Test(),               // fails
            OpenMr("Should be refused"),
        ]);
        var detail = await RunAsync(ceo, task.Id);
        var mrCall = detail.ToolExecutions.Last();
        Assert.Equal(ToolExecutionStatus.Failed, mrCall.Status);
        Assert.Contains("no passing test run", mrCall.Error);
        Assert.Empty(await ceo.GetAsync<List<MergeRequestDto>>($"/api/projects/{project.Id}/merge-requests"));
    }

    [Fact]
    public async Task Approval_is_refused_if_the_branch_moved_after_the_merge_request()
    {
        var (ceo, _, task, project, _) = await SetupAsync("BACKEND",
        [
            Write("app.txt", "hello\n"),
            Write("test.sh", "true"),
            Commit("First"),
            Test(),
            OpenMr("First"),
            Write("app.txt", "hello again\n"),
            Commit("Sneaky follow-up"),
        ]);
        await RunAsync(ceo, task.Id);
        var approval = Assert.Single(await ceo.GetAsync<List<ApprovalDto>>($"/api/approvals?status=Pending&taskId={task.Id}"));
        var res = await ceo.PostAsJsonAsync($"/api/approvals/{approval.Id}/decision", new { approve = true, payloadHash = approval.PayloadHash });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal(MergeRequestStatus.Open, Assert.Single(await ceo.GetAsync<List<MergeRequestDto>>($"/api/projects/{project.Id}/merge-requests")).Status);
    }

    [Fact]
    public async Task A_person_can_run_the_tests_on_a_branch()
    {
        var (ceo, _, _, _, repo) = await SetupAsync("BACKEND", [], testCommand: "test -f README.md && echo HAS_README");
        var run = await ceo.PostAsync<TestRunDto>($"/api/repositories/{repo.Id}/test-runs", new { branch = "main" });
        Assert.Equal(TestRunStatus.Passed, run.Status);
        Assert.Contains("HAS_README", run.Output);
        Assert.NotNull(run.CommitSha);
        var res = await ceo.PostAsJsonAsync($"/api/repositories/{repo.Id}/test-runs", new { branch = "nope" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Reviewers_can_read_code_but_not_write_it_and_images_are_allow_listed()
    {
        var (ceo, qa, _, project, _) = await SetupAsync("QAENG", []);
        var tools = (await ceo.GetAsync<AgentConfigDto>($"/api/agents/{qa.Id}/config")).Tools;
        Assert.Contains(DiffTool.ToolId, tools);
        Assert.Contains(RunTestsTool.ToolId, tools);
        Assert.DoesNotContain(WriteFileTool.ToolId, tools);
        Assert.DoesNotContain(CommitTool.ToolId, tools);

        var res = await ceo.PostAsJsonAsync($"/api/projects/{project.Id}/repositories", new { name = "other", sandboxImage = "ubuntu:latest", testCommand = "true" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // ---------------- remotes (GitHub / GitLab) ----------------

    /// <summary>A bare repository standing in for GitHub/GitLab.</summary>
    private static async Task<string> FakeRemoteAsync(bool withMain = false)
    {
        var dir = Path.Combine(Path.GetTempPath(), "officeai-remote-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        await GitAsync(dir, "init", "--bare", "--initial-branch", "main", "remote.git");
        var remote = Path.Combine(dir, "remote.git");
        if (withMain)
        {
            var seed = Path.Combine(dir, "seed");
            await GitAsync(dir, "clone", remote, seed);
            await File.WriteAllTextAsync(Path.Combine(seed, "imported.txt"), "from github\n");
            await GitAsync(seed, "add", "-A");
            await GitAsync(seed, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-m", "upstream");
            await GitAsync(seed, "push", "origin", "HEAD:main");
        }
        return remote;
    }

    private static async Task<string> GitAsync(string dir, params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        var output = await p.StandardOutput.ReadToEndAsync();
        var error = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        Assert.True(p.ExitCode == 0, $"git {string.Join(' ', args)}: {error}");
        return output.Trim();
    }

    [Fact]
    public async Task Approved_merges_are_pushed_to_the_remote_and_only_security_managers_choose_it()
    {
        var (ceo, _, task, _, repo) = await SetupAsync("BACKEND",
            [Write("app.txt", "hello\n"), Write("test.sh", "grep -q hello app.txt"), Commit("Add greeting"), Test(), OpenMr("Greeting")]);
        var remote = await FakeRemoteAsync();

        // Validation and permissions.
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PutAsJsonAsync($"/api/repositories/{repo.Id}/remote", new { remoteUrl = "http://github.com/acme/app.git" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PutAsJsonAsync($"/api/repositories/{repo.Id}/remote", new { remoteUrl = "https://user:pw@github.com/acme/app.git" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PutAsJsonAsync($"/api/repositories/{repo.Id}/remote",
            new { remoteUrl = "https://github.com/acme/app.git", remoteTokenEnvVar = "POSTGRES_PASSWORD" })).StatusCode);
        var manager = await fx.UserWithRolesAsync(OfficeAI.Domain.Security.SystemRoles.Manager);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PutAsJsonAsync($"/api/repositories/{repo.Id}/remote", new { remoteUrl = remote })).StatusCode);

        Environment.SetEnvironmentVariable("GIT_REMOTE_TESTS", "tok-123");
        var configured = await ceo.PutAsync<RepositoryDto>($"/api/repositories/{repo.Id}/remote", new { remoteUrl = remote, remoteTokenEnvVar = "GIT_REMOTE_TESTS" });
        Assert.True(configured.RemoteTokenPresent);
        Assert.Null(configured.RemotePushedAt);

        await RunAsync(ceo, task.Id);
        var approval = Assert.Single(await ceo.GetAsync<List<ApprovalDto>>($"/api/approvals?status=Pending&taskId={task.Id}"));
        (await ceo.PostAsJsonAsync($"/api/approvals/{approval.Id}/decision", new { approve = true, payloadHash = approval.PayloadHash })).EnsureSuccessStatusCode();

        var after = (await ceo.GetAsync<List<RepositoryDto>>($"/api/projects/{repo.ProjectId}/repositories")).Single(r => r.Id == repo.Id);
        Assert.Null(after.RemoteError);
        Assert.NotNull(after.RemotePushedAt);
        Assert.Equal(after.RemotePushedSha, await GitAsync(remote, "rev-parse", "refs/heads/main"));
        Assert.Contains("app.txt", await GitAsync(remote, "ls-tree", "-r", "--name-only", "main"));
        // Task branches stay private.
        Assert.DoesNotContain("task/", await GitAsync(remote, "for-each-ref", "--format=%(refname)"));

        // Someone changed GitHub directly: pushing never forces, the problem is recorded instead.
        var clone = Path.Combine(Path.GetDirectoryName(remote)!, "other");
        await GitAsync(Path.GetDirectoryName(remote)!, "clone", remote, clone);
        await File.WriteAllTextAsync(Path.Combine(clone, "upstream.txt"), "x");
        await GitAsync(clone, "add", "-A");
        await GitAsync(clone, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-m", "direct change");
        await GitAsync(clone, "push", "origin", "HEAD:main");
        var upstreamSha = await GitAsync(remote, "rev-parse", "refs/heads/main");
        var pushed = await manager.PostAsync<RepositoryDto>($"/api/repositories/{repo.Id}/push", new { }); // nothing new locally: up to date or rejected
        var rejected = await ceo.GetAsync<List<RepositoryDto>>($"/api/projects/{repo.ProjectId}/repositories");
        Assert.Equal(upstreamSha, await GitAsync(remote, "rev-parse", "refs/heads/main")); // GitHub's commit was not overwritten
        Assert.NotNull(rejected.Single(r => r.Id == repo.Id).RemoteError);
        Assert.DoesNotContain("tok-123", pushed.RemoteError ?? "");
    }

    [Fact]
    public async Task A_repository_can_start_from_an_existing_remote()
    {
        var ceo = await fx.CeoAsync();
        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"IM-{Guid.NewGuid():N}"[..14], name = "Import project", status = "Active" });
        var remote = await FakeRemoteAsync(withMain: true);

        var manager = await fx.UserWithRolesAsync(OfficeAI.Domain.Security.SystemRoles.Manager);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync($"/api/projects/{project.Id}/repositories",
            new { name = "imported", sandboxImage = Image, testCommand = "true", importUrl = remote })).StatusCode);

        var repo = await ceo.PostAsync<RepositoryDto>($"/api/projects/{project.Id}/repositories",
            new { name = "imported", sandboxImage = Image, testCommand = "true", importUrl = remote });
        Assert.Equal(remote, repo.RemoteUrl);
        Assert.Contains("imported.txt", await ceo.GetAsync<List<string>>($"/api/repositories/{repo.Id}/files"));

        var empty = await FakeRemoteAsync();
        var res = await ceo.PostAsJsonAsync($"/api/projects/{project.Id}/repositories", new { name = "nothing", sandboxImage = Image, testCommand = "true", importUrl = empty });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode); // no main branch to start from
    }

    private static AiResponse Lint() => ScriptedProvider.Call("sandbox_run_lint", new { });
    private static AiResponse ScanSecrets() => ScriptedProvider.Call("repo_scan_secrets", new { });

    [Fact]
    public async Task Lint_command_is_set_by_people_and_must_pass_before_a_merge_request()
    {
        var (ceo, _, task, project, repo) = await SetupAsync("BACKEND",
        [
            Write("app.txt", "hello\n"),
            Write("test.sh", "true"),
            Write("lint.sh", "grep -q hello app.txt && echo LINT_OK"),
            Commit("Add greeting"),
            Test(),
            OpenMr("Too early"),
            Lint(),
            OpenMr("Greeting"),
        ]);
        repo = await ceo.PutAsync<RepositoryDto>($"/api/repositories/{repo.Id}", new { sandboxImage = Image, testCommand = "sh test.sh", lintCommand = "sh lint.sh", version = repo.Version });
        Assert.Equal("sh lint.sh", repo.LintCommand);

        var detail = await RunAsync(ceo, task.Id);
        var results = detail.ToolExecutions.Select(x => (x.ToolId, x.Status)).ToList();
        Assert.Equal((OpenMergeRequestTool.ToolId, ToolExecutionStatus.Failed), results[5]);
        Assert.Equal((RunLintTool.ToolId, ToolExecutionStatus.Succeeded), results[6]);
        Assert.Equal((OpenMergeRequestTool.ToolId, ToolExecutionStatus.Succeeded), results[7]);

        var runs = await ceo.GetAsync<List<TestRunDto>>($"/api/repositories/{repo.Id}/test-runs?taskId={task.Id}");
        Assert.Contains(runs, r => r.Kind == CheckKind.Lint && r.Status == TestRunStatus.Passed && r.Output!.Contains("LINT_OK"));
        Assert.Contains(runs, r => r.Kind == CheckKind.Test);
        Assert.Single(await ceo.GetAsync<List<MergeRequestDto>>($"/api/projects/{project.Id}/merge-requests"));

        // People can run it on a branch too; without a lint command it is refused.
        var manual = await ceo.PostAsync<TestRunDto>($"/api/repositories/{repo.Id}/test-runs", new { branch = "main", kind = "Lint" });
        Assert.Equal(CheckKind.Lint, manual.Kind);
        await ceo.PutAsync<RepositoryDto>($"/api/repositories/{repo.Id}", new { sandboxImage = Image, testCommand = "sh test.sh", lintCommand = " " });
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PostAsJsonAsync($"/api/repositories/{repo.Id}/test-runs", new { kind = "Lint" })).StatusCode);
    }

    [Fact]
    public async Task Credentials_in_committed_code_block_the_merge_request_and_are_never_echoed()
    {
        const string key = "sk-ant-api03-abcdefghijklmnopqrstuvwxyz0123456789";
        var (ceo, _, task, project, _) = await SetupAsync("BACKEND",
        [
            Write("test.sh", "true"),
            Write("config.js", $"const apiKey = \"{key}\";\nconst url = process.env.API_URL;\n"),
            ScanSecrets(),
            Commit("Add config"),
            Test(),
            OpenMr("Config"),
        ]);
        var detail = await RunAsync(ceo, task.Id);
        var scan = detail.ToolExecutions.First(x => x.ToolId == ScanSecretsTool.ToolId);
        Assert.Equal(ToolExecutionStatus.Failed, scan.Status);
        var mr = detail.ToolExecutions.Last(x => x.ToolId == OpenMergeRequestTool.ToolId);
        Assert.Equal(ToolExecutionStatus.Failed, mr.Status);
        Assert.Empty(await ceo.GetAsync<List<MergeRequestDto>>($"/api/projects/{project.Id}/merge-requests"));
        // The agent wrote the value itself; the checks report where it is without repeating it.
        foreach (var x in new[] { scan, mr }) Assert.DoesNotContain(key, (x.Output ?? "") + (x.Error ?? ""));
        Assert.Contains("config.js:1 (anthropic-key)", (scan.Output ?? "") + (scan.Error ?? ""));
        Assert.Contains("config.js:1 (anthropic-key)", (mr.Output ?? "") + (mr.Error ?? ""));
    }

    [Fact]
    public void Secret_scanner_reports_added_lines_only_and_skips_placeholders()
    {
        var diff = string.Join("\n",
            "diff --git a/src/app.py b/src/app.py",
            "--- a/src/app.py",
            "+++ b/src/app.py",
            "@@ -10,2 +10,4 @@",
            " import os",
            "-OLD = 'AKIAABCDEFGHIJKLMNOP'",
            "+password = \"hunter2hunter2\"",
            "+token = os.environ['TOKEN']",
            "+example_key = \"AKIAEXAMPLEEXAMPLE12\"",
            "+aws = 'AKIAQWERTYUIOPASDFGH'",
            "diff --git a/k.pem b/k.pem",
            "--- /dev/null",
            "+++ b/k.pem",
            "@@ -0,0 +1 @@",
            "+-----BEGIN RSA PRIVATE KEY-----");
        var findings = SecretScanner.ScanDiff(diff);
        Assert.Equal([new SecretFinding("src/app.py", 11, "hardcoded-credential"), new SecretFinding("src/app.py", 14, "aws-access-key"),
            new SecretFinding("k.pem", 1, "private-key")], findings);
    }
}
