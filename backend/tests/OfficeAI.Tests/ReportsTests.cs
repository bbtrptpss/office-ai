using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Agents;
using OfficeAI.Application.Organization;
using OfficeAI.Application.Reports;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Agents;
using Microsoft.EntityFrameworkCore;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Delivery;
using OfficeAI.Domain.Finance;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class ReportsTests(ApiFixture fx)
{
    private static DateOnly ThisMonth => new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

    /// <summary>An AI employee whose every turn costs exactly 1 USD, on a fresh project and task.</summary>
    private async Task<(HttpClient Ceo, EmployeeDto Agent, ProjectDto Project, TaskDto Task)> PricedAgentAsync()
    {
        var ceo = await fx.CeoAsync();
        var role = (await ceo.GetAsync<List<RoleDefinitionDto>>("/api/role-definitions")).First(r => r.Code == "BACKEND");
        var agent = await ceo.PostAsync<EmployeeDto>("/api/employees",
            new { displayName = $"Fin {Guid.NewGuid():N}"[..20], kind = "AI", departmentId = role.DepartmentId, roleDefinitionId = role.Id });
        var modelId = $"script-{Guid.NewGuid():N}";
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AiModels.Add(new AiModel { Provider = ScriptedProvider.ProviderName, ModelId = modelId, DisplayName = modelId, InputUsdPerMTok = 1m });
            await db.SaveChangesAsync();
        }
        fx.Scripted.Register(modelId, (_, _) => Task.FromResult(ScriptedProvider.Text("done", inTok: 1_000_000, outTok: 0)));
        var c = await ceo.GetAsync<AgentConfigDto>($"/api/agents/{agent.Id}/config");
        (await ceo.PutAsJsonAsync($"/api/agents/{agent.Id}/config", new
        {
            provider = ScriptedProvider.ProviderName, modelId, effort = "Medium", c.MaxTokensPerCall, c.MaxTurnsPerRun, c.MaxToolCallsPerRun,
            maxTokensPerRun = 10_000_000, c.TimeoutSeconds, monthlyBudgetUsd = 100m, isEnabled = true,
        }, ApiFixture.Json)).EnsureSuccessStatusCode();
        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"FN-{Guid.NewGuid():N}"[..14], name = "Finance project", status = "Active" });
        var task = await ceo.PostAsync<TaskDto>($"/api/projects/{project.Id}/tasks", new { title = "Costly" });
        await ceo.PostAsync<TaskDto>($"/api/tasks/{task.Id}/assign", new { employeeId = agent.Id });
        return (ceo, agent, project, task);
    }

    private static async Task RunToEndAsync(HttpClient c, Guid taskId)
    {
        var run = await c.PostAsync<AgentRunDto>($"/api/tasks/{taskId}/runs", new { });
        for (var i = 0; i < 100; i++)
        {
            var d = await c.GetAsync<AgentRunDetailDto>($"/api/agent-runs/{run.Id}");
            if (d.Run.Status is not (AgentRunStatus.Queued or AgentRunStatus.Running)) return;
            await Task.Delay(100);
        }
        throw new TimeoutException();
    }

    [Fact]
    public void Budget_levels_warn_at_80_percent_and_block_at_100()
    {
        Assert.Equal(BudgetLevel.Ok, BudgetMath.Level(79.99m, 100m));
        Assert.Equal(BudgetLevel.Warning, BudgetMath.Level(80m, 100m));
        Assert.Equal(BudgetLevel.Exceeded, BudgetMath.Level(100m, 100m));
    }

    [Fact]
    public async Task Project_budget_stops_new_runs_once_used_up()
    {
        var (ceo, _, project, task) = await PricedAgentAsync();
        var budget = await ceo.PutAsync<BudgetDto>("/api/budgets", new { scope = "Project", scopeId = project.Id, month = ThisMonth, amountUsd = 1m });
        Assert.Equal(BudgetLevel.Ok, budget.Level);

        await RunToEndAsync(ceo, task.Id); // costs exactly 1 USD
        var res = await ceo.PostAsJsonAsync($"/api/tasks/{task.Id}/runs", new { });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Contains("project AI budget", await res.Content.ReadAsStringAsync());

        var listed = (await ceo.GetAsync<List<BudgetDto>>("/api/budgets")).Single(b => b.Id == budget.Id);
        Assert.Equal(1m, listed.SpentUsd);
        Assert.Equal(BudgetLevel.Exceeded, listed.Level);

        // Raising it lets work continue.
        await ceo.PutAsync<BudgetDto>("/api/budgets", new { scope = "Project", scopeId = project.Id, month = ThisMonth, amountUsd = 5m });
        await RunToEndAsync(ceo, task.Id);
    }

    [Fact]
    public async Task Finance_and_performance_reports_show_actual_spend()
    {
        var (ceo, agent, project, task) = await PricedAgentAsync();
        await RunToEndAsync(ceo, task.Id);

        var finance = await ceo.GetAsync<FinanceReportDto>("/api/reports/finance");
        Assert.Equal(ThisMonth, finance.Month);
        var row = Assert.Single(finance.Projects, p => p.ProjectId == project.Id);
        Assert.Equal(1m, row.ActualAiUsdMonth);
        Assert.Equal(Math.Round(finance.UsdToThb, 2), row.ActualAiThbAllTime);
        Assert.Null(row.EstimateRevision); // no approved estimate: planned figures stay empty, never guessed
        Assert.Contains(finance.Employees, e => e.Id == agent.Id && e.CostUsd == 1m && e.Runs == 1);
        Assert.True(finance.TotalUsd >= 1m);

        var perf = await ceo.GetAsync<PerformanceReportDto>("/api/reports/performance");
        var me = Assert.Single(perf.Rows, r => r.EmployeeId == agent.Id);
        Assert.Equal(1, me.Runs);
        Assert.Equal(100m, me.SuccessRate);
        Assert.Equal(1m, me.CostPerRunUsd);
    }

    [Fact]
    public async Task Reports_need_finance_permission_and_budgets_are_validated()
    {
        var member = await fx.UserWithRolesAsync("Member");
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/reports/finance")).StatusCode);
        var manager = await fx.UserWithRolesAsync("Manager");
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/api/reports/performance")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PutAsJsonAsync("/api/budgets", new { scope = "Company", month = ThisMonth, amountUsd = 1 })).StatusCode);

        var ceo = await fx.CeoAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PutAsJsonAsync("/api/budgets", new { scope = "Company", month = ThisMonth.AddDays(3), amountUsd = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PutAsJsonAsync("/api/budgets", new { scope = "Company", scopeId = Guid.NewGuid(), month = ThisMonth, amountUsd = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ceo.PutAsJsonAsync("/api/budgets", new { scope = "Project", scopeId = Guid.NewGuid(), month = ThisMonth, amountUsd = 1 })).StatusCode);
    }
}

[Collection(ApiCollection.Name)]
public class QualitySecurityTests(ApiFixture fx)
{
    [Fact]
    public async Task Overview_counts_security_events_and_needs_audit_permission()
    {
        await fx.Anonymous().PostAsJsonAsync("/api/auth/login", new { email = ApiFixture.CeoEmail, password = "definitely-wrong" });
        var ceo = await fx.CeoAsync();
        var report = await ceo.GetAsync<OfficeAI.Application.Reports.QualitySecurityDto>("/api/reports/quality-security?days=7");
        Assert.Equal(7, report.Days);
        Assert.Contains(report.SecurityEventCounts, c => c.Key == "LoginFailed" && c.Count >= 1);
        Assert.Contains(report.RecentSecurityEvents, e => e.Action == "LoginFailed");

        var manager = await fx.UserWithRolesAsync("Manager");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await manager.GetAsync("/api/reports/quality-security")).StatusCode);
    }
}

[Collection(ApiCollection.Name)]
public class ProfitabilityTests(ApiFixture fx)
{
    private static ProjectEstimate Sample(Guid projectId, Guid roleId)
    {
        var e = new ProjectEstimate
        {
            ProjectId = projectId, Revision = 1, Status = EstimateStatus.Approved, Assumptions = "fixed scope",
            ContingencyPercent = 10m, TargetMarginPercent = 30m, AiCostUsd = 10m, UsdToThb = 36m,
            Items = [new EstimateLineItem { Description = "API", RoleDefinitionId = roleId, RoleTitle = "Dev", Hours = 100m, RateThb = 1000m, Evidence = "past project" }],
        };
        EstimateCalculator.Compute(e);
        return e;
    }

    [Fact]
    public void Scenario_keeps_the_agreed_price_unless_asked_to_reprice()
    {
        var e = Sample(Guid.NewGuid(), Guid.NewGuid());
        // 100h × 1000 = 100,000 + AI 360 = 100,360; +10% = 110,396; price = ceil(110,396 / 0.7 / 100) × 100 = 157,800
        Assert.Equal(110_396m, e.TotalCost);
        Assert.Equal(157_800m, e.Price);

        var same = ScenarioCalculator.Run(e, new ScenarioInput());
        Assert.Equal(ScenarioCalculator.Baseline(e), same);

        var slower = ScenarioCalculator.Run(e, new ScenarioInput(HoursChangePercent: 20m));
        Assert.Equal(120m, slower.TotalHours);
        Assert.Equal(157_800m, slower.Price); // the price was agreed; extra hours eat the margin
        Assert.Equal(157_800m - (120_000m + 360m) * 1.1m, slower.GrossMargin);

        var repriced = ScenarioCalculator.Run(e, new ScenarioInput(HoursChangePercent: 20m, RepriceAtTargetMargin: true));
        Assert.True(repriced.Price > e.Price);
        Assert.True(repriced.GrossMarginPercent >= 29.9m);

        Assert.Throws<DomainException>(() => ScenarioCalculator.Run(e, new ScenarioInput(HoursChangePercent: -150m)));
        Assert.Throws<DomainException>(() => ScenarioCalculator.Run(e, new ScenarioInput(UsdToThb: 0m)));
    }

    [Fact]
    public async Task Profitability_forecast_adds_ai_overrun_and_scenarios_are_read_only()
    {
        var ceo = await fx.CeoAsync();
        var role = (await ceo.GetAsync<List<RoleDefinitionDto>>("/api/role-definitions")).First(r => r.Code == "BACKEND");
        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"PR-{Guid.NewGuid():N}"[..14], name = "Profit project", status = "Active" });
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var e = Sample(project.Id, role.Id);
            e.UsdToThb = (await db.CompanySettings.FirstOrDefaultAsync())?.UsdToThb ?? 36m;
            EstimateCalculator.Compute(e);
            db.Estimates.Add(e);
            // Actual AI spend: planned AI cost plus 1,000 THB worth of overrun.
            db.CostRecords.Add(new CostRecord { Kind = CostKind.Actual, Category = "ai", ProjectId = project.Id, AmountUsd = e.AiCostUsd + 1000m / e.UsdToThb, OccurredAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        var report = await ceo.GetAsync<ProfitabilityReportDto>("/api/reports/profitability");
        var row = Assert.Single(report.Projects, p => p.ProjectId == project.Id);
        Assert.Equal(1000m, Math.Round(row.AiOverrunThb));
        Assert.Equal(row.PlannedCostThb + row.AiOverrunThb, row.ForecastCostThb);
        Assert.Equal(row.PriceThb - row.ForecastCostThb, row.ForecastMarginThb);
        Assert.False(row.AtRisk);

        var s = await ceo.PostAsync<ScenarioDto>("/api/reports/scenario", new { projectId = project.Id, hoursChangePercent = 50 });
        Assert.Equal(150m, s.Scenario.TotalHours);
        Assert.True(s.MarginChangeThb < 0);
        var again = await ceo.GetAsync<ProfitabilityReportDto>("/api/reports/profitability");
        Assert.Equal(row, Assert.Single(again.Projects, p => p.ProjectId == project.Id)); // nothing was stored

        var other = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"PN-{Guid.NewGuid():N}"[..14], name = "No estimate", status = "Active" });
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PostAsJsonAsync("/api/reports/scenario", new { projectId = other.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PostAsJsonAsync("/api/reports/scenario", new { projectId = project.Id, rateChangePercent = 900 })).StatusCode);
        var member = await fx.UserWithRolesAsync("Member");
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/reports/profitability")).StatusCode);
    }
}
