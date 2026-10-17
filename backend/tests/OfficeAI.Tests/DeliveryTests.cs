using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Agents;
using OfficeAI.Application.Delivery;
using OfficeAI.Application.Organization;
using OfficeAI.Application.Work;
using OfficeAI.Application.Workflows;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Delivery;
using OfficeAI.Domain.Workflows;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class DeliveryTests(ApiFixture fx)
{
    private async Task<(HttpClient Ceo, EmployeeDto Agent, TaskDto Task, ProjectDto Project)> AgentOnTaskAsync(string roleCode, AiResponse[] script)
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
            provider = ScriptedProvider.ProviderName, modelId, effort = "Medium", c.MaxTokensPerCall, c.MaxTurnsPerRun, c.MaxToolCallsPerRun,
            c.MaxTokensPerRun, c.TimeoutSeconds, c.MonthlyBudgetUsd, isEnabled = true,
        }, ApiFixture.Json)).EnsureSuccessStatusCode();

        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"DL-{Guid.NewGuid():N}"[..14], name = "Delivery project", status = "Active" });
        var task = await ceo.PostAsync<TaskDto>($"/api/projects/{project.Id}/tasks", new { title = "Analyse" });
        await ceo.PostAsync<TaskDto>($"/api/tasks/{task.Id}/assign", new { employeeId = agent.Id });
        return (ceo, agent, task, project);
    }

    private static async Task<AgentRunDetailDto> RunAsync(HttpClient c, Guid taskId)
    {
        var run = await c.PostAsync<AgentRunDto>($"/api/tasks/{taskId}/runs", new { });
        for (var i = 0; i < 100; i++)
        {
            var d = await c.GetAsync<AgentRunDetailDto>($"/api/agent-runs/{run.Id}");
            if (d.Run.Status is not (AgentRunStatus.Queued or AgentRunStatus.Running)) return d;
            await Task.Delay(100);
        }
        throw new TimeoutException();
    }

    [Fact]
    public void Estimate_arithmetic_is_deterministic()
    {
        var e = new ProjectEstimate
        {
            ContingencyPercent = 10m, TargetMarginPercent = 30m, AiCostUsd = 10m, UsdToThb = 36m, Assumptions = "x",
            Items =
            [
                new EstimateLineItem { Description = "API", RoleTitle = "Dev", Hours = 40m, RateThb = 1000m, Evidence = "similar to project X" },
                new EstimateLineItem { Description = "Tests", RoleTitle = "QA", Hours = 12.5m, RateThb = 1000m },
            ],
        };
        EstimateCalculator.Compute(e);
        Assert.Equal(52.5m, e.TotalHours);
        Assert.Equal(52_500m, e.LaborCost);
        Assert.Equal(360m, e.AiCost);
        Assert.Equal(5_286m, e.Contingency);          // 10% of 52,860
        Assert.Equal(58_146m, e.TotalCost);
        Assert.Equal(83_100m, e.Price);               // 58,146 / 0.7 = 83,065.71 → rounded up to 100
        Assert.Equal(24_954m, e.GrossMargin);
        Assert.Equal(30.03m, e.GrossMarginPercent);
        Assert.Single(e.Warnings, w => w.Contains("No evidence for 'Tests'"));
    }

    [Fact]
    public async Task Proposal_agent_submits_an_estimate_priced_by_the_system_and_the_CEO_approves_it()
    {
        var (ceo, agent, task, project) = await AgentOnTaskAsync("PROPOSAL",
        [
            ScriptedProvider.Call("estimate_submit", new
            {
                assumptions = "Two endpoints; existing design system",
                aiCostUsd = 5,
                items = new object[]
                {
                    new { description = "Booking API", roleCode = "BACKEND", hours = 24, evidence = "Similar to the leave-request API (22h)" },
                    new { description = "Tests", roleCode = "QAENG", hours = 8, evidence = "Usual 1:3 ratio" },
                },
            }),
        ]);
        Assert.Contains(SubmitEstimateTool.ToolId, (await ceo.GetAsync<AgentConfigDto>($"/api/agents/{agent.Id}/config")).Tools);

        var detail = await RunAsync(ceo, task.Id);
        Assert.Equal(ToolExecutionStatus.Succeeded, Assert.Single(detail.ToolExecutions).Status);

        var estimate = Assert.Single(await ceo.GetAsync<List<EstimateDto>>($"/api/projects/{project.Id}/estimates"));
        Assert.Equal(EstimateStatus.Submitted, estimate.Status);
        Assert.Equal(1, estimate.Revision);
        Assert.Equal(32m, estimate.TotalHours);
        Assert.Equal(24 * 1000m + 8 * 2000m, estimate.LaborCost); // Staff 1000/h, Head (QA lead) 2000/h from the default rate card
        Assert.Empty(estimate.Warnings);

        var approval = Assert.Single(await ceo.GetAsync<List<ApprovalDto>>($"/api/approvals?status=Pending&taskId={task.Id}"));
        Assert.Equal(ApprovalKind.Estimate, approval.Kind);
        (await ceo.PostAsJsonAsync($"/api/approvals/{approval.Id}/decision", new { approve = true, payloadHash = approval.PayloadHash })).EnsureSuccessStatusCode();

        Assert.Equal(EstimateStatus.Approved, (await ceo.GetAsync<List<EstimateDto>>($"/api/projects/{project.Id}/estimates"))[0].Status);
        Assert.Equal(estimate.TotalCost, (await ceo.GetAsync<ProjectDto>($"/api/projects/{project.Id}")).EstimatedBudget);
    }

    [Fact]
    public async Task Estimate_with_an_unknown_role_is_rejected_by_the_tool()
    {
        var (ceo, _, task, project) = await AgentOnTaskAsync("PROPOSAL",
            [ScriptedProvider.Call("estimate_submit", new { assumptions = "x", items = new object[] { new { description = "x", roleCode = "WIZARD", hours = 1 } } })]);
        var detail = await RunAsync(ceo, task.Id);
        var exec = Assert.Single(detail.ToolExecutions);
        Assert.Equal(ToolExecutionStatus.Failed, exec.Status);
        Assert.Contains("Unknown role code", exec.Error);
        Assert.Empty(await ceo.GetAsync<List<EstimateDto>>($"/api/projects/{project.Id}/estimates"));
    }

    [Fact]
    public async Task Analyst_records_requirements_and_architect_proposes_a_decision()
    {
        var (ceo, analyst, task, project) = await AgentOnTaskAsync("BA",
        [
            ScriptedProvider.Call("requirements_add", new { kind = "Functional", title = "Book a room", acceptanceCriteria = "A free room can be booked", priority = "Must" }),
            ScriptedProvider.Call("requirements_add", new { kind = "NonFunctional", title = "Respond in 300 ms" }),
        ]);
        await RunAsync(ceo, task.Id);
        var reqs = await ceo.GetAsync<List<RequirementDto>>($"/api/projects/{project.Id}/requirements");
        Assert.Equal(["REQ-001", "REQ-002"], reqs.Select(r => r.Code));
        Assert.All(reqs, r => Assert.Equal(ReviewState.Draft, r.Status));
        Assert.Equal(analyst.DisplayName, reqs[0].AuthorName);

        var approved = await ceo.PostAsync<RequirementDto>($"/api/requirements/{reqs[0].Id}/status", new { status = "Approved" });
        Assert.Equal(ReviewState.Approved, approved.Status);

        // A backend developer has no requirements.add tool.
        var (_, dev, _, _) = await AgentOnTaskAsync("BACKEND", []);
        Assert.DoesNotContain(AddRequirementTool.ToolId, (await ceo.GetAsync<AgentConfigDto>($"/api/agents/{dev.Id}/config")).Tools);

        var (ceo2, _, archTask, archProject) = await AgentOnTaskAsync("ARCH",
            [ScriptedProvider.Call("adr_propose", new { title = "Use PostgreSQL", context = "Need relational data", decision = "PostgreSQL 17", consequences = "Ops know it" })]);
        await RunAsync(ceo2, archTask.Id);
        var adr = Assert.Single(await ceo2.GetAsync<List<DecisionDto>>($"/api/projects/{archProject.Id}/decisions"));
        Assert.Equal(1, adr.Number);
        Assert.Equal(DecisionStatus.Proposed, adr.Status);
    }

    [Fact]
    public async Task Rate_card_and_settings_are_validated()
    {
        var ceo = await fx.CeoAsync();
        var rates = await ceo.GetAsync<List<RoleRateDto>>("/api/rate-card");
        var backend = rates.Single(r => r.Code == "BACKEND");
        Assert.Equal(1000m, backend.HourlyRateThb);
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PutAsJsonAsync($"/api/rate-card/{backend.RoleDefinitionId}", new { hourlyRateThb = -5 })).StatusCode);

        var settings = await ceo.GetAsync<SettingsDto>("/api/company-settings");
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PutAsJsonAsync("/api/company-settings",
            new { usdToThb = settings.UsdToThb, defaultContingencyPercent = 10, targetMarginPercent = 95, version = settings.Version })).StatusCode);
    }
}
