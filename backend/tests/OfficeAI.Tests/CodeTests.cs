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
        (await ceo.PostAsJsonAsync($"/api/approvals/{approval.Id}/decision", new { approve = true, payloadHash = approval.PayloadHash })).EnsureSuccessStatusCode();

        mr = (await ceo.GetAsync<MergeRequestDetailDto>($"/api/merge-requests/{mr.Id}")).MergeRequest;
        Assert.Equal(MergeRequestStatus.Merged, mr.Status);
        Assert.Contains("app.txt", await ceo.GetAsync<List<string>>($"/api/repositories/{repo.Id}/files"));
        var res = await ceo.GetAsync($"/api/repositories/{repo.Id}/file?path=app.txt");
        Assert.Equal("hello\n", await res.Content.ReadAsStringAsync());
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
        Assert.Equal(TestRunStatus.Passed, run.Status);
        Assert.Contains("ISOLATED", run.Output);
        Assert.Null(run.CommitSha); // uncommitted changes are never treated as evidence for a commit
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
}
