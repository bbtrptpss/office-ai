using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Organization;
using OfficeAI.Application.Reports;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Delivery;
using OfficeAI.Domain.Finance;
using OfficeAI.Domain.Security;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

/// <summary>Costs that are not per-token AI charges (electricity, flat-rate plans, hosting), split to projects for the finance reports.</summary>
[Collection(ApiCollection.Name)]
public class OperatingCostTests(ApiFixture fx)
{
    private static readonly DateOnly Oct = new(2026, 10, 1), Nov = new(2026, 11, 1), Dec = new(2026, 12, 1);

    private static OperatingCost Cost(OperatingCostAllocation allocation, decimal amount, DateOnly start, DateOnly? end = null, Guid? project = null,
        string? provider = null, CostCurrency currency = CostCurrency.Thb) =>
        new() { Name = "c", Category = "other", MonthlyAmount = amount, Currency = currency, StartMonth = start, EndMonth = end, Allocation = allocation, ProjectId = project, Provider = provider };

    [Fact]
    public void Usage_based_costs_split_by_tokens_of_the_chosen_provider_and_each_month_adds_up_exactly()
    {
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        UsageByMonth[] usage =
        [
            new(Oct, "ollama", a, 1), new(Oct, "ollama", b, 1), new(Oct, "ollama", c, 1), // three equal users: 100 / 3 does not divide evenly
            new(Oct, "gemini", c, 1_000_000),                                              // another provider: ignored by an Ollama-only cost
            new(Nov, "gemini", a, 10),                                                     // no Ollama use in November
        ];
        var electricity = Cost(OperatingCostAllocation.AiUsage, 100m, Oct, provider: "ollama");
        var shares = OperatingCostAllocator.Allocate([electricity], usage, Oct, Nov, 36m);

        var october = shares.Where(s => s.Month == Oct).ToList();
        Assert.Equal(3, october.Count);
        Assert.Equal(100m, october.Sum(s => s.Thb));
        Assert.All(october, s => Assert.InRange(s.Thb, 33.33m, 33.34m));
        var november = Assert.Single(shares, s => s.Month == Nov);
        Assert.Null(november.ProjectId); // nothing to split by: unallocated, never invented
        Assert.Equal(100m, november.Thb);

        // All providers: gemini's million tokens take nearly everything.
        var plan = OperatingCostAllocator.Allocate([Cost(OperatingCostAllocation.AiUsage, 50m, Oct, Oct)], usage, Oct, Dec, 36m);
        Assert.Equal(50m, plan.Sum(s => s.Thb)); // only October: the cost ended
        Assert.Equal(50m, plan.Where(s => s.ProjectId == c).Sum(s => s.Thb));
    }

    [Fact]
    public void Project_and_overhead_costs_and_dollar_amounts()
    {
        var p = Guid.NewGuid();
        var shares = OperatingCostAllocator.Allocate(
            [Cost(OperatingCostAllocation.Project, 20m, Nov, project: p, currency: CostCurrency.Usd), Cost(OperatingCostAllocation.Overhead, 500m, Oct)], [], Oct, Dec, 35.5m);
        Assert.Equal([710m, 710m], shares.Where(s => s.ProjectId == p).Select(s => s.Thb)); // November and December at 20 USD × 35.5
        Assert.Equal(3, shares.Count(s => s.ProjectId == null && s.Thb == 500m));
    }

    [Fact]
    public async Task Finance_and_profitability_include_operating_costs_but_AI_budgets_do_not()
    {
        var ceo = await fx.CeoAsync();
        var role = (await ceo.GetAsync<List<RoleDefinitionDto>>("/api/role-definitions")).First(r => r.Code == "BACKEND");
        var heavy = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"OP-{Guid.NewGuid():N}"[..14], name = "Heavy user", status = "Active" });
        var light = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"OP-{Guid.NewGuid():N}"[..14], name = "Light user", status = "Active" });
        var month = BudgetMath.MonthOf(DateTimeOffset.UtcNow);
        decimal planned;
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var e = new ProjectEstimate
            {
                ProjectId = heavy.Id, Revision = 1, Status = EstimateStatus.Approved, Assumptions = "fixed scope", ContingencyPercent = 10m, TargetMarginPercent = 30m,
                AiCostUsd = 0m, UsdToThb = (await db.CompanySettings.FirstOrDefaultAsync())?.UsdToThb ?? 36m,
                Items = [new EstimateLineItem { Description = "API", RoleDefinitionId = role.Id, RoleTitle = "Dev", Hours = 10m, RateThb = 1000m, Evidence = "past project" }],
            };
            EstimateCalculator.Compute(e);
            planned = e.TotalCost;
            db.Estimates.Add(e);
            // Free local model: tokens but no per-token cost.
            var employee = await db.Employees.Where(x => x.Kind == Domain.Organization.EmployeeKind.AI).Select(x => x.Id).FirstAsync();
            await db.SaveChangesAsync();
            foreach (var (project, tokens) in new[] { (heavy.Id, 3_000L), (light.Id, 1_000L) })
            {
                var task = await ceo.PostAsync<TaskDto>($"/api/projects/{project}/tasks", new { title = "Local model work" });
                var run = new AgentRun { TaskId = task.Id, ProjectId = project, EmployeeId = employee, Provider = "ollama", ModelId = "officeai-qwen3:r1", Status = AgentRunStatus.Succeeded };
                db.AgentRuns.Add(run);
                db.UsageRecords.Add(new UsageRecord { RunId = run.Id, EmployeeId = employee, ProjectId = project, Provider = "ollama", ModelId = "officeai-qwen3:r1", InputTokens = tokens, OccurredAt = DateTimeOffset.UtcNow });
            }
            await db.SaveChangesAsync();
        }

        var budgetBefore = await ceo.GetAsync<List<BudgetDto>>("/api/budgets");
        var created = new List<Guid>();
        try
        {
            async Task<OperatingCostDto> Add(object body)
            {
                var dto = await ceo.PostAsync<OperatingCostDto>("/api/operating-costs", body);
                created.Add(dto.Id);
                return dto;
            }
            // The GPU's electricity follows Ollama use; hosting is overhead; a design tool belongs to one project.
            await Add(new { name = "ค่าไฟ GPU", category = "electricity", monthlyAmount = 400m, currency = "Thb", isEstimate = true, startMonth = month,
                allocation = "AiUsage", provider = "ollama", notes = "130 W × 10 h × 30 d × 4.5 THB/kWh ≈ 175 THB, plus the PC" });
            await Add(new { name = "VPS", category = "hosting", monthlyAmount = 300m, currency = "Thb", isEstimate = false, startMonth = month, allocation = "Overhead" });
            var tool = await Add(new { name = "Design tool", category = "software", monthlyAmount = 10m, currency = "Usd", isEstimate = false, startMonth = month,
                allocation = "Project", projectId = light.Id });
            Assert.Equal("Light user", tool.ProjectName);

            var finance = await ceo.GetAsync<FinanceReportDto>("/api/reports/finance");
            var rate = finance.UsdToThb;
            var heavyRow = Assert.Single(finance.Projects, p => p.ProjectId == heavy.Id);
            var lightRow = Assert.Single(finance.Projects, p => p.ProjectId == light.Id);
            // The 400 THB follows Ollama tokens 3:1 (no other test runs on Ollama); the design tool adds 10 USD to the light project.
            Assert.Equal(300m, heavyRow.OperatingThbMonth);
            Assert.Equal(100m + Math.Round(10m * rate, 2), lightRow.OperatingThbMonth);
            var gpu = Assert.Single(finance.Operating!, o => o.Name == "ค่าไฟ GPU");
            Assert.Equal(400m, gpu.Thb);
            Assert.True(gpu.IsEstimate);
            Assert.Equal(300m, Assert.Single(finance.Operating!, o => o.Name == "VPS").UnallocatedThb);
            Assert.True(finance.OperatingEstimatedThb >= 400m);
            Assert.Equal(finance.TotalThb + finance.OperatingThb, finance.TotalWithOperatingThb);

            var profit = Assert.Single((await ceo.GetAsync<ProfitabilityReportDto>("/api/reports/profitability")).Projects, p => p.ProjectId == heavy.Id);
            Assert.Equal(heavyRow.OperatingThbAllTime, profit.OperatingThb);
            Assert.Equal(planned + profit.OperatingThb, profit.ForecastCostThb);

            // AI budgets only count AI charges: operating costs never stop a run.
            Assert.Equal(budgetBefore.Select(b => b.SpentUsd), (await ceo.GetAsync<List<BudgetDto>>("/api/budgets")).Select(b => b.SpentUsd));

            // Only people with finance.manage edit them; a direct cost needs its project.
            var manager = await fx.UserWithRolesAsync(SystemRoles.Manager, SystemRoles.Member);
            Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync("/api/operating-costs", new { name = "x", category = "other", monthlyAmount = 1m, currency = "Thb", startMonth = month, allocation = "Overhead" }, ApiFixture.Json)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PostAsJsonAsync("/api/operating-costs", new { name = "x", category = "other", monthlyAmount = 1m, currency = "Thb", startMonth = month, allocation = "Project" }, ApiFixture.Json)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PostAsJsonAsync("/api/operating-costs", new { name = "x", category = "other", monthlyAmount = 1m, currency = "Thb", startMonth = month.AddDays(3), allocation = "Overhead" }, ApiFixture.Json)).StatusCode);

            // Ending a cost last month takes it out of this month.
            var ended = await ceo.PutAsync<OperatingCostDto>($"/api/operating-costs/{tool.Id}", new
            {
                tool.Name, tool.Category, tool.MonthlyAmount, tool.Currency, tool.IsEstimate, startMonth = month.AddMonths(-2), endMonth = month.AddMonths(-1),
                tool.Allocation, tool.ProjectId, tool.Version,
            });
            Assert.Equal(month.AddMonths(-1), ended.EndMonth);
            Assert.DoesNotContain((await ceo.GetAsync<FinanceReportDto>("/api/reports/finance")).Operating!, o => o.Id == tool.Id);
        }
        finally
        {
            // Company-wide figures: leave nothing behind for other tests.
            foreach (var id in created) (await ceo.DeleteAsync($"/api/operating-costs/{id}")).EnsureSuccessStatusCode();
        }
    }
}
