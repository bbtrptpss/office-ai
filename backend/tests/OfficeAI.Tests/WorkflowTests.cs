using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Abstractions;
using OfficeAI.Application.Agents;
using OfficeAI.Application.Organization;
using OfficeAI.Application.Work;
using OfficeAI.Application.Workflows;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Organization;
using OfficeAI.Domain.Work;
using OfficeAI.Domain.Security;
using OfficeAI.Domain.Workflows;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class WorkflowTests(ApiFixture fx)
{
    // ---------------- kit ----------------

    private async Task<EmployeeDto> NewAgentAsync(HttpClient ceo, string roleCode, string provider = "simulated", string model = "simulated-v1")
    {
        var role = (await ceo.GetAsync<List<RoleDefinitionDto>>("/api/role-definitions")).First(r => r.Code == roleCode);
        var agent = await ceo.PostAsync<EmployeeDto>("/api/employees",
            new { displayName = $"{roleCode} {Guid.NewGuid():N}"[..24], kind = "AI", departmentId = role.DepartmentId, roleDefinitionId = role.Id });
        var c = await ceo.GetAsync<AgentConfigDto>($"/api/agents/{agent.Id}/config");
        (await ceo.PutAsJsonAsync($"/api/agents/{agent.Id}/config", new
        {
            provider, modelId = model, effort = "Medium", c.MaxTokensPerCall, c.MaxTurnsPerRun, c.MaxToolCallsPerRun, maxTokensPerRun = 10_000_000,
            c.TimeoutSeconds, monthlyBudgetUsd = 1000m, isEnabled = true,
        }, ApiFixture.Json)).EnsureSuccessStatusCode();
        return agent;
    }

    /// <summary>Only these AI employees may be picked by the engine during the test.</summary>
    private async Task OnlyTheseAgentsAsync(params Guid[] keep)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.AgentConfigurations.ExecuteUpdateAsync(s => s.SetProperty(c => c.IsEnabled, c => keep.Contains(c.EmployeeId)));
    }

    private async Task<string> ScriptedModelAsync(Func<AiRequest, CancellationToken, Task<AiResponse>> script, decimal input = 0m)
    {
        var modelId = $"script-{Guid.NewGuid():N}";
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AiModels.Add(new AiModel { Provider = ScriptedProvider.ProviderName, ModelId = modelId, DisplayName = modelId, InputUsdPerMTok = input });
        await db.SaveChangesAsync();
        fx.Scripted.Register(modelId, script);
        return modelId;
    }

    private async Task TickAsync()
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<WorkflowEngine>().TickAsync(default));
    }

    private async Task WaitForIdleAsync()
    {
        for (var i = 0; i < 200; i++)
        {
            await using var scope = fx.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await db.AgentRuns.AnyAsync(r => r.Status == AgentRunStatus.Queued || r.Status == AgentRunStatus.Running)) return;
            await Task.Delay(50);
        }
        throw new TimeoutException("Runs did not finish.");
    }

    private static async Task ApproveAsync(HttpClient c, ApprovalDto a, bool approve = true, string? note = null) =>
        (await c.PostAsJsonAsync($"/api/approvals/{a.Id}/decision", new { approve, payloadHash = a.PayloadHash, note })).EnsureSuccessStatusCode();

    private static Task<List<ApprovalDto>> PendingAsync(HttpClient c, Guid workflowId) =>
        c.GetAsync<List<ApprovalDto>>($"/api/approvals?status=Pending&workflowId={workflowId}");

    private async Task<(HttpClient Ceo, ProjectDto Project)> ProjectAsync()
    {
        var ceo = await fx.CeoAsync();
        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"WF-{Guid.NewGuid():N}"[..14], name = "Workflow project", status = "Active" });
        return (ceo, project);
    }

    // ---------------- the end-to-end objective ----------------

    [Fact]
    public async Task Objective_is_planned_approved_worked_reviewed_and_approved_end_to_end()
    {
        var (ceo, project) = await ProjectAsync();
        var planner = await NewAgentAsync(ceo, "COO");
        var analyst = await NewAgentAsync(ceo, "BA");
        var developer = await NewAgentAsync(ceo, "BACKEND");
        var qa = await NewAgentAsync(ceo, "QAENG");
        await OnlyTheseAgentsAsync(planner.Id, analyst.Id, developer.Id, qa.Id);

        var workflow = await ceo.PostAsync<WorkflowDto>($"/api/projects/{project.Id}/objectives",
            new { objective = "Build a meeting-room booking API", plannerEmployeeId = planner.Id });
        Assert.Equal(WorkflowStatus.Planning, workflow.Status);

        var decided = 0;
        for (var round = 0; round < 40; round++)
        {
            await TickAsync();
            await WaitForIdleAsync();
            foreach (var a in await PendingAsync(ceo, workflow.Id))
            {
                Assert.True(a.Kind is ApprovalKind.Plan or ApprovalKind.TaskCompletion, $"Unexpected {a.Kind}: {a.Details}");
                await ApproveAsync(ceo, a);
                decided++;
            }
            workflow = await ceo.GetAsync<WorkflowDto>($"/api/workflows/{workflow.Id}");
            if (workflow.Status != WorkflowStatus.Executing && workflow.Status != WorkflowStatus.Planning && workflow.Status != WorkflowStatus.AwaitingPlanApproval) break;
        }

        Assert.Equal(WorkflowStatus.Completed, workflow.Status);
        Assert.Equal(3, workflow.TaskCount);
        Assert.Equal(3, workflow.DoneCount);
        Assert.Equal(4, decided); // the plan + three completions

        var tasks = await ceo.GetAsync<List<TaskDto>>($"/api/projects/{project.Id}/tasks");
        Assert.All(tasks, t => Assert.Equal(WorkTaskStatus.Done, t.Status));
        var build = tasks.Single(t => t.Title == "[SIMULATED] Build the first version");
        Assert.Equal(developer.Id, Assert.Single(build.Assignees).EmployeeId); // matched on csharp/api skills
        Assert.Contains(tasks.Single(t => t.Title == "[SIMULATED] Clarify requirements").Id, build.DependsOn);

        var reviews = await ceo.GetAsync<List<ReviewDto>>($"/api/tasks/{build.Id}/reviews");
        Assert.Contains(reviews, r => r.Outcome == ReviewOutcome.Approved && r.ReviewerName == qa.DisplayName);

        var events = await ceo.GetAsync<List<WorkflowEventDto>>($"/api/workflows/{workflow.Id}/events");
        foreach (var kind in new[] { "started", "plan_submitted", "plan_approved", "assigned", "reviewer", "task_done", "completed" })
            Assert.Contains(events, e => e.Kind == kind);
    }

    [Fact]
    public async Task A_department_objective_is_planned_done_and_reviewed_only_inside_that_department()
    {
        var (ceo, project) = await ProjectAsync();
        var planner = await NewAgentAsync(ceo, "ARCH");    // head of the engineering department: may plan
        var developer = await NewAgentAsync(ceo, "BACKEND");
        var analyst = await NewAgentAsync(ceo, "BA");      // better skills for "Clarify requirements", other department
        var qa = await NewAgentAsync(ceo, "QAENG");        // the usual reviewer, other department
        Assert.NotEqual(planner.DepartmentId, analyst.DepartmentId);
        Assert.NotEqual(planner.DepartmentId, qa.DepartmentId);
        await OnlyTheseAgentsAsync(planner.Id, developer.Id, analyst.Id, qa.Id);

        // The planner must belong to the department.
        var wrong = await ceo.PostAsJsonAsync($"/api/projects/{project.Id}/objectives",
            new { objective = "x", plannerEmployeeId = analyst.Id, departmentId = planner.DepartmentId });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        // ...and be allowed to plan (a staff developer is not), instead of leaving the objective stuck in planning.
        var cannotPlan = await ceo.PostAsJsonAsync($"/api/projects/{project.Id}/objectives",
            new { objective = "x", plannerEmployeeId = developer.Id, departmentId = planner.DepartmentId });
        Assert.Equal(HttpStatusCode.Conflict, cannotPlan.StatusCode);
        Assert.Contains("plan.submit", await cannotPlan.Content.ReadAsStringAsync());

        var workflow = await ceo.PostAsync<WorkflowDto>($"/api/projects/{project.Id}/objectives",
            new { objective = "Build a meeting-room booking API", plannerEmployeeId = planner.Id, departmentId = planner.DepartmentId });
        Assert.Equal(planner.DepartmentId, workflow.DepartmentId);
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var plan = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Tasks.AsNoTracking().SingleAsync(t => t.Id == workflow.PlanningTaskId);
            Assert.Contains(developer.DisplayName, plan.Description); // the planner is told who is in the team
            Assert.DoesNotContain(analyst.DisplayName, plan.Description);
        }

        for (var round = 0; round < 40; round++)
        {
            await TickAsync();
            await WaitForIdleAsync();
            foreach (var a in await PendingAsync(ceo, workflow.Id)) await ApproveAsync(ceo, a);
            workflow = await ceo.GetAsync<WorkflowDto>($"/api/workflows/{workflow.Id}");
            if (workflow.Status is not (WorkflowStatus.Executing or WorkflowStatus.Planning or WorkflowStatus.AwaitingPlanApproval)) break;
        }

        Assert.Equal(WorkflowStatus.Completed, workflow.Status);
        var team = new[] { planner.Id, developer.Id };
        var tasks = await ceo.GetAsync<List<TaskDto>>($"/api/projects/{project.Id}/tasks");
        Assert.All(tasks, t => Assert.All(t.Assignees, a => Assert.Contains(a.EmployeeId, team)));
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var reviewers = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Tasks.AsNoTracking()
                .Where(t => t.WorkflowExecutionId == workflow.Id && t.ReviewerEmployeeId != null).Select(t => t.ReviewerEmployeeId!.Value).ToListAsync();
            Assert.All(reviewers, r => Assert.Contains(r, team));
        }
    }

    [Fact]
    public async Task Operations_plans_its_own_objectives_and_executives_do_their_departments_work()
    {
        var (ceo, project) = await ProjectAsync();
        var ops = await NewAgentAsync(ceo, "OPERATIONS");
        var coo = await NewAgentAsync(ceo, "COO");
        var cto = await NewAgentAsync(ceo, "CTO");
        var developer = await NewAgentAsync(ceo, "BACKEND");
        Assert.Equal(coo.DepartmentId, cto.DepartmentId);
        await OnlyTheseAgentsAsync(ops.Id, coo.Id, cto.Id, developer.Id);

        // Operations has only staff positions; its operations lead may still plan, so "automatic" finds a planner.
        var opsObjective = await ceo.PostAsync<WorkflowDto>($"/api/projects/{project.Id}/objectives",
            new { objective = "Tidy up the supplier list", departmentId = ops.DepartmentId });
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var planner = await db.Employees.AsNoTracking().Include(e => e.RoleDefinition).SingleAsync(e => e.Id == opsObjective.PlannerEmployeeId);
            Assert.Equal("OPERATIONS", planner.RoleDefinition!.Code);
        }
        (await ceo.PostAsJsonAsync($"/api/workflows/{opsObjective.Id}/cancel", new { })).EnsureSuccessStatusCode();

        // The executive team's own objective is done by executives (normally they are never assigned work).
        var workflow = await ceo.PostAsync<WorkflowDto>($"/api/projects/{project.Id}/objectives",
            new { objective = "Build a meeting-room booking API", plannerEmployeeId = coo.Id, departmentId = coo.DepartmentId });
        for (var round = 0; round < 40; round++)
        {
            await TickAsync();
            await WaitForIdleAsync();
            foreach (var a in await PendingAsync(ceo, workflow.Id)) await ApproveAsync(ceo, a);
            workflow = await ceo.GetAsync<WorkflowDto>($"/api/workflows/{workflow.Id}");
            if (workflow.Status is not (WorkflowStatus.Executing or WorkflowStatus.Planning or WorkflowStatus.AwaitingPlanApproval)) break;
        }
        Assert.Equal(WorkflowStatus.Completed, workflow.Status);
        var tasks = await ceo.GetAsync<List<TaskDto>>($"/api/projects/{project.Id}/tasks");
        var done = tasks.Where(t => t.Status == WorkTaskStatus.Done && t.Assignees.Count > 0 && !t.Title.StartsWith("Plan:")).ToList();
        Assert.NotEmpty(done);
        Assert.All(done, t => Assert.All(t.Assignees, a => Assert.Contains(a.EmployeeId, new[] { coo.Id, cto.Id })));
    }

    // ---------------- approvals ----------------

    [Fact]
    public async Task Approval_requires_matching_hash_the_right_permission_and_a_different_person()
    {
        var (ceo, project) = await ProjectAsync();
        var planner = await NewAgentAsync(ceo, "COO");
        await OnlyTheseAgentsAsync(planner.Id);
        var workflow = await ceo.PostAsync<WorkflowDto>($"/api/projects/{project.Id}/objectives", new { objective = "Plan only", plannerEmployeeId = planner.Id });
        await TickAsync();
        await WaitForIdleAsync();
        var plan = Assert.Single(await PendingAsync(ceo, workflow.Id));
        Assert.Equal(ApprovalKind.Plan, plan.Kind);
        Assert.Equal(planner.Id, plan.RequestedByEmployeeId);
        Assert.Contains("\"requiredSkills\"", plan.PayloadJson); // the UI reads camelCase
        Assert.Contains("\"priority\":\"", plan.PayloadJson); // enums as names, not numbers

        // Wrong hash: the approver must have seen exactly this payload.
        var res = await ceo.PostAsJsonAsync($"/api/approvals/{plan.Id}/decision", new { approve = true, payloadHash = new string('0', 64) });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);

        // A Member of the project cannot approve plans.
        var (member, _) = await fx.PersonAsync([SystemRoles.Member], project.Id);
        res = await member.PostAsJsonAsync($"/api/approvals/{plan.Id}/decision", new { approve = true, payloadHash = plan.PayloadHash });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);

        // Nobody approves their own request.
        var me = await ceo.GetAsync<Api.Endpoints.MeDto>("/api/auth/me");
        Guid ownId;
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var own = Approvals.Create(ApprovalKind.Escalation, "Mine", null, new { x = 1 }, DateTimeOffset.UtcNow, requestedByUser: me.UserId);
            db.ApprovalRequests.Add(own);
            await db.SaveChangesAsync();
            ownId = own.Id;
        }
        var ownDto = await ceo.GetAsync<ApprovalDto>($"/api/approvals/{ownId}");
        res = await ceo.PostAsJsonAsync($"/api/approvals/{ownId}/decision", new { approve = true, payloadHash = ownDto.PayloadHash });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);

        // Rejecting a plan sends it back to the planner with the note.
        await ApproveAsync(ceo, plan, approve: false, note: "Split testing out");
        workflow = await ceo.GetAsync<WorkflowDto>($"/api/workflows/{workflow.Id}");
        Assert.Equal(WorkflowStatus.Planning, workflow.Status);
        res = await ceo.PostAsJsonAsync($"/api/approvals/{plan.Id}/decision", new { approve = true, payloadHash = plan.PayloadHash });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode); // already decided
        var reviews = await ceo.GetAsync<List<ReviewDto>>($"/api/tasks/{workflow.PlanningTaskId}/reviews");
        Assert.Contains(reviews, r => r.Comments == "Split testing out");

        // The engine re-plans.
        await TickAsync();
        await WaitForIdleAsync();
        Assert.Single(await PendingAsync(ceo, workflow.Id));
        await ceo.PostAsync<WorkflowDto>($"/api/workflows/{workflow.Id}/cancel", new { });
    }

    [Fact]
    public async Task An_AI_identity_can_never_decide_an_approval()
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var approval = Approvals.Create(ApprovalKind.Escalation, "x", null, new { y = 1 }, DateTimeOffset.UtcNow);
        db.ApprovalRequests.Add(approval);
        await db.SaveChangesAsync();

        using var acting = ActingAs.Use(new ActingIdentity(Guid.NewGuid(), "Sneaky agent", IsAgent: true, null));
        var service = ActivatorUtilities.CreateInstance<ApprovalService>(scope.ServiceProvider, new ActingCurrentUser());
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.DecideAsync(approval.Id, new DecideApprovalRequest(true, approval.PayloadHash, null), new HashSet<string> { "task.manage" }, default));
    }

    private sealed class ActingCurrentUser : ICurrentUser
    {
        public Guid? UserId => ActingAs.Identity?.UserId;
        public string? Name => ActingAs.Identity?.Name;
        public bool IsAgent => ActingAs.Identity?.IsAgent ?? false;
        public string? CorrelationId => null;
    }

    // ---------------- retries and escalation ----------------

    private async Task<(HttpClient Ceo, WorkflowDto Workflow, ProjectDto Project)> ApprovedOneTaskWorkflowAsync(EmployeeDto worker, string? planJson = null, decimal? budget = null, int? parallel = null)
    {
        var (ceo, project) = await ProjectAsync();
        var plannerModel = await ScriptedModelAsync(ScriptedProvider.Sequence(ScriptedProvider.Call("plan_submit",
            JsonSerializer.Deserialize<JsonElement>(planJson ?? """{"summary":"one task","tasks":[{"key":"only","title":"Only task"}]}"""))));
        var planner = await NewAgentAsync(ceo, "COO", ScriptedProvider.ProviderName, plannerModel);
        await OnlyTheseAgentsAsync(planner.Id, worker.Id);
        var workflow = await ceo.PostAsync<WorkflowDto>($"/api/projects/{project.Id}/objectives",
            new { objective = "Scripted objective", plannerEmployeeId = planner.Id, budgetUsd = budget, maxParallelRuns = parallel });
        await TickAsync();
        await WaitForIdleAsync();
        await ApproveAsync(ceo, Assert.Single(await PendingAsync(ceo, workflow.Id)));
        return (ceo, workflow, project);
    }

    [Fact]
    public async Task Failing_runs_are_retried_then_escalated_and_try_again_resumes()
    {
        var ceo = await fx.CeoAsync();
        var failing = await ScriptedModelAsync((_, _) => throw new AiProviderException("boom", retryable: false));
        var worker = await NewAgentAsync(ceo, "BACKEND", ScriptedProvider.ProviderName, failing);
        var (_, workflow, project) = await ApprovedOneTaskWorkflowAsync(worker);

        for (var i = 0; i < 6; i++)
        {
            await TickAsync();
            await WaitForIdleAsync();
        }
        var task = (await ceo.GetAsync<List<TaskDto>>($"/api/projects/{project.Id}/tasks")).Single(t => t.Title == "Only task");
        var runs = await ceo.GetAsync<List<AgentRunDto>>($"/api/agent-runs?taskId={task.Id}");
        Assert.Equal(3, runs.Count); // first try + 2 retries
        Assert.Equal(WorkTaskStatus.Blocked, task.Status);
        var escalation = Assert.Single(await PendingAsync(ceo, workflow.Id));
        Assert.Equal(ApprovalKind.Escalation, escalation.Kind);

        // "Try again": the engine starts a fresh series of attempts.
        await ApproveAsync(ceo, escalation, note: "Provider fixed");
        await TickAsync();
        await WaitForIdleAsync();
        Assert.Equal(4, (await ceo.GetAsync<List<AgentRunDto>>($"/api/agent-runs?taskId={task.Id}")).Count);
        await ceo.PostAsync<WorkflowDto>($"/api/workflows/{workflow.Id}/cancel", new { });
    }

    [Fact]
    public async Task Parallel_limit_is_respected_and_cancelling_stops_everything()
    {
        var ceo = await fx.CeoAsync();
        var slow = await ScriptedModelAsync(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return ScriptedProvider.Text("late");
        });
        var worker = await NewAgentAsync(ceo, "BACKEND", ScriptedProvider.ProviderName, slow);
        var (_, workflow, project) = await ApprovedOneTaskWorkflowAsync(worker,
            """{"summary":"three","tasks":[{"key":"a","title":"A"},{"key":"b","title":"B"},{"key":"c","title":"C"}]}""", parallel: 1);

        await TickAsync();
        await TickAsync();
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var active = await db.AgentRuns.CountAsync(r => (r.Status == AgentRunStatus.Queued || r.Status == AgentRunStatus.Running) &&
                db.Tasks.Any(t => t.Id == r.TaskId && t.WorkflowExecutionId == workflow.Id));
            Assert.Equal(1, active);
        }

        workflow = await ceo.PostAsync<WorkflowDto>($"/api/workflows/{workflow.Id}/cancel", new { });
        Assert.Equal(WorkflowStatus.Cancelled, workflow.Status);
        await WaitForIdleAsync();
        var tasks = await ceo.GetAsync<List<TaskDto>>($"/api/projects/{project.Id}/tasks");
        Assert.All(tasks.Where(t => !t.Title.StartsWith("Plan:")), t => Assert.Equal(WorkTaskStatus.Cancelled, t.Status));
        Assert.Empty(await PendingAsync(ceo, workflow.Id));
    }

    [Fact]
    public async Task Workflow_budget_stops_new_runs_and_asks_for_a_decision()
    {
        var ceo = await fx.CeoAsync();
        var pricey = await ScriptedModelAsync((_, _) => Task.FromResult(ScriptedProvider.Text("no deliverable", inTok: 1_000_000)), input: 1m);
        var worker = await NewAgentAsync(ceo, "BACKEND", ScriptedProvider.ProviderName, pricey);
        var (_, workflow, _) = await ApprovedOneTaskWorkflowAsync(worker, budget: 1m);

        for (var i = 0; i < 4; i++)
        {
            await TickAsync();
            await WaitForIdleAsync();
        }
        workflow = await ceo.GetAsync<WorkflowDto>($"/api/workflows/{workflow.Id}");
        Assert.Equal(1m, workflow.SpentUsd); // exactly one run before the cap
        var pending = await PendingAsync(ceo, workflow.Id);
        var escalation = Assert.Single(pending, a => a.Kind == ApprovalKind.Escalation && a.Title.StartsWith("Budget reached"));

        // The budget must stay above what is already spent.
        var tooLow = await ceo.PostAsJsonAsync($"/api/approvals/{escalation.Id}/decision", new { approve = true, payloadHash = escalation.PayloadHash, newBudgetUsd = 1m });
        Assert.Equal(HttpStatusCode.BadRequest, tooLow.StatusCode);

        // Approving raises the budget: one more run happens, then the new limit asks again.
        await ceo.PostAsync<ApprovalDto>($"/api/approvals/{escalation.Id}/decision", new { approve = true, payloadHash = escalation.PayloadHash, newBudgetUsd = 2m });
        Assert.Equal(2m, (await ceo.GetAsync<WorkflowDto>($"/api/workflows/{workflow.Id}")).BudgetUsd);
        for (var i = 0; i < 4; i++)
        {
            await TickAsync();
            await WaitForIdleAsync();
        }
        workflow = await ceo.GetAsync<WorkflowDto>($"/api/workflows/{workflow.Id}");
        Assert.Equal(2m, workflow.SpentUsd);
        Assert.Single(await PendingAsync(ceo, workflow.Id), a => a.Kind == ApprovalKind.Escalation && a.Title.StartsWith("Budget reached"));
        await ceo.PostAsync<WorkflowDto>($"/api/workflows/{workflow.Id}/cancel", new { });
    }

    // ---------------- delegation ----------------

    [Fact]
    public async Task Delegated_subtask_blocks_the_parent_until_done_and_depth_is_limited()
    {
        var ceo = await fx.CeoAsync();
        var leadModel = await ScriptedModelAsync(ScriptedProvider.Sequence(
            ScriptedProvider.Call("task_delegate", new { title = "Write SQL schema", description = "Tables for bookings", requiredSkills = "postgresql" })));
        var lead = await NewAgentAsync(ceo, "ARCH", ScriptedProvider.ProviderName, leadModel);
        var dba = await NewAgentAsync(ceo, "DBENG");
        var (_, workflow, project) = await ApprovedOneTaskWorkflowAsync(lead);
        await OnlyTheseAgentsAsync(lead.Id, dba.Id);

        await TickAsync(); // lead runs and delegates
        await WaitForIdleAsync();
        var tasks = await ceo.GetAsync<List<TaskDto>>($"/api/projects/{project.Id}/tasks");
        var parent = tasks.Single(t => t.Title == "Only task");
        var child = tasks.Single(t => t.Title == "Write SQL schema");
        Assert.Contains(child.Id, parent.DependsOn);

        await TickAsync(); // child assigned to the DBA (skill match) and started; parent waits
        await WaitForIdleAsync();
        child = await ceo.GetAsync<TaskDto>($"/api/tasks/{child.Id}");
        Assert.Equal(dba.Id, Assert.Single(child.Assignees).EmployeeId);
        Assert.Single(await ceo.GetAsync<List<AgentRunDto>>($"/api/agent-runs?taskId={parent.Id}"));

        // Depth limit: a task already at the maximum depth cannot delegate.
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var deep = new WorkTask { ProjectId = project.Id, WorkflowExecutionId = workflow.Id, Title = "Deep", DelegationDepth = 2 };
        db.Tasks.Add(deep);
        await db.SaveChangesAsync();
        var result = await new DelegateTaskTool().ExecuteAsync(
            new ToolContext(Guid.NewGuid(), deep.Id, project.Id, lead.Id, false, db, TimeProvider.System),
            JsonSerializer.SerializeToElement(new { title = "Deeper", description = "x" }), default);
        Assert.False(result.Success);
        Assert.Contains("depth", result.Output);
        await ceo.PostAsync<WorkflowDto>($"/api/workflows/{workflow.Id}/cancel", new { });
    }

    // ---------------- approval-gated tool ----------------

    [Fact]
    public async Task Approved_tool_action_runs_exactly_once_and_rejected_never()
    {
        var ceo = await fx.CeoAsync();
        var model = await ScriptedModelAsync(ScriptedProvider.Sequence(ScriptedProvider.Call("test_needs_approval", new { })));
        var agent = await NewAgentAsync(ceo, "BACKEND", ScriptedProvider.ProviderName, model);
        (await ceo.PutAsJsonAsync($"/api/agents/{agent.Id}/tools", new { tools = new[] { ApprovalRequiredTool.ToolId } })).EnsureSuccessStatusCode();
        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"TA-{Guid.NewGuid():N}"[..14], name = "Tool approval", status = "Active" });
        var task = await ceo.PostAsync<TaskDto>($"/api/projects/{project.Id}/tasks", new { title = "Needs approval" });
        await ceo.PostAsync<TaskDto>($"/api/tasks/{task.Id}/assign", new { employeeId = agent.Id });

        var before = ApprovalRequiredTool.Executions;
        await ceo.PostAsync<AgentRunDto>($"/api/tasks/{task.Id}/runs", new { });
        await WaitForIdleAsync();
        var approval = Assert.Single(await ceo.GetAsync<List<ApprovalDto>>($"/api/approvals?status=Pending&taskId={task.Id}"));
        Assert.Equal(ApprovalKind.ToolAction, approval.Kind);
        Assert.Equal(before, ApprovalRequiredTool.Executions);

        await ApproveAsync(ceo, approval);
        await TickAsync();
        await TickAsync();
        Assert.Equal(before + 1, ApprovalRequiredTool.Executions);

        // A second request, rejected, never runs.
        fx.Scripted.Register(model, ScriptedProvider.Sequence(ScriptedProvider.Call("test_needs_approval", new { })));
        await ceo.PostAsync<AgentRunDto>($"/api/tasks/{task.Id}/runs", new { });
        await WaitForIdleAsync();
        var second = Assert.Single(await ceo.GetAsync<List<ApprovalDto>>($"/api/approvals?status=Pending&taskId={task.Id}"));
        await ApproveAsync(ceo, second, approve: false);
        await TickAsync();
        Assert.Equal(before + 1, ApprovalRequiredTool.Executions);
    }

    // ---------------- plan validation ----------------

    [Theory]
    [InlineData("""[{"key":"a","title":"A","dependsOn":["b"]},{"key":"b","title":"B","dependsOn":["a"]}]""", "loop")]
    [InlineData("""[{"key":"a","title":"A","dependsOn":["zzz"]}]""", "unknown")]
    [InlineData("""[{"key":"a","title":"A"},{"key":"a","title":"B"}]""", "twice")]
    [InlineData("""[{"key":"Bad Key","title":"A"}]""", "lowercase")]
    [InlineData("""[]""", "no tasks")]
    public void Invalid_plans_are_rejected_with_a_reason(string tasksJson, string expected)
    {
        var (items, error) = PlanParser.Parse(JsonSerializer.Deserialize<JsonElement>(tasksJson));
        Assert.Null(items);
        Assert.Contains(expected, error);
    }

    [Fact]
    public void Valid_plan_is_parsed_with_defaults()
    {
        var (items, error) = PlanParser.Parse(JsonSerializer.Deserialize<JsonElement>(
            """[{"key":"a","title":"A","requiredSkills":["SQL"," api "]},{"key":"b","title":"B","dependsOn":["a"],"priority":"critical"}]"""));
        Assert.Null(error);
        Assert.Equal(["sql", "api"], items![0].RequiredSkills);
        Assert.Equal(TaskPriority.Medium, items[0].Priority);
        Assert.Equal(TaskPriority.Critical, items[1].Priority);
    }
}
