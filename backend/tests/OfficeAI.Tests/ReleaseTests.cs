using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Agents;
using OfficeAI.Application.Code;
using OfficeAI.Application.Delivery;
using OfficeAI.Application.Work;
using OfficeAI.Application.Workflows;
using OfficeAI.Domain.Code;
using OfficeAI.Domain.Delivery;
using OfficeAI.Domain.Organization;
using OfficeAI.Domain.Security;
using OfficeAI.Domain.Workflows;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class ReleaseTests(ApiFixture fx)
{
    private static readonly object Plans = new
    {
        tag = "1.0.0", title = "First release", notes = "Booking form", deploymentPlan = "1. Run migrations\n2. Deploy the image",
        rollbackPlan = "Redeploy 0.9 and restore the backup",
    };

    private async Task<(HttpClient Ceo, ProjectDto Project, RepositoryDto Repo)> SetupAsync()
    {
        var ceo = await fx.CeoAsync();
        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"RL-{Guid.NewGuid():N}"[..14], name = "Release project", status = "Active" });
        var repo = await ceo.PostAsync<RepositoryDto>($"/api/projects/{project.Id}/repositories", new { name = "app", sandboxImage = "alpine:3.20", testCommand = "true" });
        return (ceo, project, repo);
    }

    /// <summary>Records a test run on the current tip of the default branch (the sandbox itself is covered by the code tests).</summary>
    private async Task RecordTestAsync(RepositoryDto repo, TestRunStatus status)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<ICodeRepositoryStore>();
        var r = await db.CodeRepositories.AsNoTracking().FirstAsync(x => x.Id == repo.Id);
        db.TestRuns.Add(new TestRun
        {
            RepositoryId = repo.Id, Branch = r.DefaultBranch, CommitSha = await ReleaseService.DefaultHeadAsync(store, r, default), Image = r.SandboxImage,
            Command = r.TestCommand, Status = status, ExitCode = status == TestRunStatus.Passed ? 0 : 1, StartedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Moves the default branch one empty commit ahead, as a merge would.</summary>
    private async Task AdvanceDefaultBranchAsync(RepositoryDto repo)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ICodeRepositoryStore>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var r = await db.CodeRepositories.AsNoTracking().FirstAsync(x => x.Id == repo.Id);
        var gitDir = ((IRepositoryLayout)store).OriginPath(r);
        var cwd = Path.GetFullPath(".");
        var head = await ReleaseService.DefaultHeadAsync(store, r, default);
        var sha = (await store.GitAsync(cwd, ["--git-dir", gitDir, "commit-tree", $"{head}^{{tree}}", "-p", head!, "-m", "hotfix"], default, ("Test", "test@example.com"))).Output.Trim();
        Assert.True((await store.GitAsync(cwd, ["--git-dir", gitDir, "update-ref", $"refs/heads/{r.DefaultBranch}", sha], default)).Ok);
    }

    private static async Task<HttpResponseMessage> DecideAsync(HttpClient client, ApprovalDto a, bool approve) =>
        await client.PostAsJsonAsync($"/api/approvals/{a.Id}/decision", new { approve, payloadHash = a.PayloadHash, note = "ok" }, ApiFixture.Json);

    [Fact]
    public async Task Release_needs_plans_and_green_tests_on_the_commit_then_a_different_person_approves_production()
    {
        var (ceo, project, repo) = await SetupAsync();
        var draft = await ceo.PostAsync<ReleaseDto>($"/api/projects/{project.Id}/releases", new { tag = "1.0.0", notes = "Booking form" });
        Assert.Equal(ReleaseStatus.Draft, draft.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await ceo.PostAsJsonAsync($"/api/projects/{project.Id}/releases", new { tag = "1.0.0" })).StatusCode);

        var readiness = await ceo.GetAsync<ReleaseReadinessDto>($"/api/releases/{draft.Id}/readiness");
        Assert.False(readiness.Ready);
        Assert.Contains(readiness.Checks, c => c.Code == "rollbackPlan" && c.Blocking && !c.Met);
        Assert.Contains(readiness.Checks, c => c.Code == "testsPassOnCommit" && !c.Met);
        var notReady = await ceo.PostAsJsonAsync($"/api/releases/{draft.Id}/submit", new { });
        Assert.Equal(HttpStatusCode.Conflict, notReady.StatusCode);
        Assert.Contains("rollbackPlan", await notReady.Content.ReadAsStringAsync());

        draft = await ceo.PutAsync<ReleaseDto>($"/api/releases/{draft.Id}", Plans);
        await RecordTestAsync(repo, TestRunStatus.Failed);
        Assert.Contains((await ceo.GetAsync<ReleaseReadinessDto>($"/api/releases/{draft.Id}/readiness")).Checks, c => c.Code == "testsPassOnCommit" && c.Detail == "Failed");
        await RecordTestAsync(repo, TestRunStatus.Passed);

        // An open (non-critical) defect is a warning carried into the approval, not a blocker.
        await ceo.PostAsync<DefectDto>($"/api/projects/{project.Id}/defects", new { title = "Typo on the receipt", stepsToReproduce = "Book a room", severity = "Low" });
        readiness = await ceo.GetAsync<ReleaseReadinessDto>($"/api/releases/{draft.Id}/readiness");
        Assert.True(readiness.Ready);
        Assert.NotNull(readiness.TestRunId);
        Assert.Contains(readiness.UnresolvedIssues, i => i.StartsWith("DEF-001"));

        var submitted = await ceo.PostAsync<ReleaseDto>($"/api/releases/{draft.Id}/submit", new { });
        Assert.Equal(ReleaseStatus.PendingApproval, submitted.Status);
        Assert.Equal(readiness.CommitSha, submitted.CommitSha);
        Assert.Equal(HttpStatusCode.Conflict, (await ceo.PutAsJsonAsync($"/api/releases/{draft.Id}", Plans, ApiFixture.Json)).StatusCode);

        var approval = await ceo.GetAsync<ApprovalDto>($"/api/approvals/{submitted.ApprovalRequestId}");
        Assert.Equal(ApprovalKind.ProductionDeployment, approval.Kind);
        Assert.Contains("noOpenDefects", approval.PayloadJson);
        // The person who asked cannot approve it.
        Assert.Equal(HttpStatusCode.Forbidden, (await DecideAsync(ceo, approval, true)).StatusCode);

        var (manager, _) = await fx.PersonAsync([SystemRoles.Manager]);
        (await DecideAsync(manager, approval, true)).EnsureSuccessStatusCode();
        var approved = (await ceo.GetAsync<List<ReleaseDto>>($"/api/projects/{project.Id}/releases")).Single();
        Assert.Equal(ReleaseStatus.Approved, approved.Status);
        Assert.NotNull(approved.ApprovedAt);

        var stages = await ceo.GetAsync<DeliveryStagesDto>($"/api/projects/{project.Id}/stages");
        Assert.True(stages.Stages[6].Approval.Met);
        Assert.Equal("1.0.0", stages.Stages[6].Approval.Detail);

        // Nothing to roll back before it is deployed.
        Assert.Equal(HttpStatusCode.Conflict, (await ceo.PostAsJsonAsync($"/api/releases/{draft.Id}/rolled-back", new { note = "x" })).StatusCode);
        var deployed = await ceo.PostAsync<ReleaseDto>($"/api/releases/{draft.Id}/deployed", new { note = "Deployed by ops at 18:00" });
        Assert.Equal(ReleaseStatus.Deployed, deployed.Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PostAsJsonAsync($"/api/releases/{draft.Id}/rolled-back", new { note = "" })).StatusCode);
        var rolledBack = await ceo.PostAsync<ReleaseDto>($"/api/releases/{draft.Id}/rolled-back", new { note = "Payment errors" });
        Assert.Equal(ReleaseStatus.RolledBack, rolledBack.Status);
    }

    [Fact]
    public async Task Approval_is_refused_when_the_branch_moved_and_critical_defects_block_submission()
    {
        var (ceo, project, repo) = await SetupAsync();
        var release = await ceo.PostAsync<ReleaseDto>($"/api/projects/{project.Id}/releases", Plans);
        await RecordTestAsync(repo, TestRunStatus.Passed);

        var critical = await ceo.PostAsync<DefectDto>($"/api/projects/{project.Id}/defects", new { title = "Data loss", stepsToReproduce = "Save", severity = "Critical" });
        var blocked = await ceo.PostAsJsonAsync($"/api/releases/{release.Id}/submit", new { });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Contains("noCriticalDefects", await blocked.Content.ReadAsStringAsync());
        await ceo.PostAsync<DefectDto>($"/api/defects/{critical.Id}/reject", new { note = "Not reproducible" });

        var submitted = await ceo.PostAsync<ReleaseDto>($"/api/releases/{release.Id}/submit", new { });
        var approval = await ceo.GetAsync<ApprovalDto>($"/api/approvals/{submitted.ApprovalRequestId}");
        await AdvanceDefaultBranchAsync(repo);

        var (manager, _) = await fx.PersonAsync([SystemRoles.Manager]);
        var moved = await DecideAsync(manager, approval, true);
        Assert.Equal(HttpStatusCode.Conflict, moved.StatusCode);
        Assert.Equal(ReleaseStatus.PendingApproval, (await ceo.GetAsync<List<ReleaseDto>>($"/api/projects/{project.Id}/releases")).Single().Status);

        var cancelled = await ceo.PostAsync<ReleaseDto>($"/api/releases/{release.Id}/cancel", new { note = "Branch moved" });
        Assert.Equal(ReleaseStatus.Cancelled, cancelled.Status);
        Assert.Equal(ApprovalStatus.Cancelled, (await ceo.GetAsync<ApprovalDto>($"/api/approvals/{approval.Id}")).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await ceo.PostAsJsonAsync($"/api/releases/{release.Id}/deployed", new { })).StatusCode);
    }

    [Fact]
    public async Task Rejected_approval_returns_the_release_to_draft_and_members_cannot_manage_releases()
    {
        var (ceo, project, repo) = await SetupAsync();
        var release = await ceo.PostAsync<ReleaseDto>($"/api/projects/{project.Id}/releases", Plans);
        await RecordTestAsync(repo, TestRunStatus.Passed);
        var submitted = await ceo.PostAsync<ReleaseDto>($"/api/releases/{release.Id}/submit", new { });
        var approval = await ceo.GetAsync<ApprovalDto>($"/api/approvals/{submitted.ApprovalRequestId}");
        var (manager, _) = await fx.PersonAsync([SystemRoles.Manager]);
        (await DecideAsync(manager, approval, false)).EnsureSuccessStatusCode();
        var back = (await ceo.GetAsync<List<ReleaseDto>>($"/api/projects/{project.Id}/releases")).Single();
        Assert.Equal(ReleaseStatus.Draft, back.Status);
        Assert.Null(back.ApprovalRequestId);

        var (member, _) = await fx.PersonAsync([SystemRoles.Member], project.Id);
        Assert.Single(await member.GetAsync<List<ReleaseDto>>($"/api/projects/{project.Id}/releases"));
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync($"/api/releases/{release.Id}/submit", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync($"/api/projects/{project.Id}/releases", new { tag = "2.0" })).StatusCode);
        var (outsider, _) = await fx.PersonAsync([SystemRoles.Member]);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/projects/{project.Id}/releases")).StatusCode);
    }

    [Fact]
    public async Task Agent_tool_drafts_a_release_but_cannot_change_a_submitted_one()
    {
        var (ceo, project, repo) = await SetupAsync();
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rm = await db.Employees.Include(e => e.RoleDefinition).FirstAsync(e => e.RoleDefinition!.Code == "RELEASE");
        var task = await ceo.PostAsync<TaskDto>($"/api/projects/{project.Id}/tasks", new { title = "Prepare 1.0.0" });
        var tool = new PrepareReleaseTool();
        var ctx = new ToolContext(Guid.NewGuid(), task.Id, project.Id, rm.Id, false, db, TimeProvider.System);
        var input = JsonSerializer.SerializeToElement(Plans);

        var res = await tool.ExecuteAsync(ctx, input, default);
        Assert.True(res.Success, res.Output);
        res = await tool.ExecuteAsync(ctx, JsonSerializer.SerializeToElement(new { tag = "1.0.0", notes = "Updated notes", deploymentPlan = "d", rollbackPlan = "r" }), default);
        Assert.True(res.Success, res.Output);
        var r = Assert.Single(await ceo.GetAsync<List<ReleaseDto>>($"/api/projects/{project.Id}/releases"));
        Assert.Equal("Updated notes", r.Notes);
        Assert.Equal(rm.DisplayName, r.AuthorName);

        await RecordTestAsync(repo, TestRunStatus.Passed);
        await ceo.PostAsync<ReleaseDto>($"/api/releases/{r.Id}/submit", new { });
        res = await tool.ExecuteAsync(ctx, input, default);
        Assert.False(res.Success);
        Assert.Contains(PrepareReleaseTool.ToolId, AgentDefaults.ToolsFor(OrgLevel.Staff, "RELEASE"));
        Assert.DoesNotContain(PrepareReleaseTool.ToolId, AgentDefaults.ToolsFor(OrgLevel.Staff, "FRONTEND"));
    }
}
