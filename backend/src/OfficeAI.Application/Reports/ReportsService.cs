using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Application.Delivery;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Delivery;
using OfficeAI.Domain.Finance;
using OfficeAI.Domain.Organization;
using OfficeAI.Domain.Work;
using OfficeAI.Domain.Workflows;

namespace OfficeAI.Application.Reports;

public record BudgetDto(Guid Id, BudgetScope Scope, Guid? ScopeId, string ScopeName, DateOnly Month, decimal AmountUsd, decimal SpentUsd, BudgetLevel Level, uint Version);
public record SetBudgetRequest(BudgetScope Scope, Guid? ScopeId, DateOnly Month, decimal AmountUsd);

public record ProjectFinanceDto(Guid ProjectId, string Code, string Name, int? EstimateRevision, decimal? PlannedTotalCostThb, decimal? PriceThb,
    decimal? PlannedAiCostThb, decimal ActualAiUsdMonth, decimal ActualAiUsdAllTime, decimal ActualAiThbAllTime, decimal? AiVarianceThb,
    decimal OperatingThbMonth = 0, decimal OperatingThbAllTime = 0);
/// <summary>One operating cost in the month: what it came to and where it went.</summary>
public record OperatingMonthDto(Guid Id, string Name, string Category, bool IsEstimate, OperatingCostAllocation Allocation, string? Provider, decimal Thb,
    List<OperatingSplitDto> Projects, decimal UnallocatedThb);
public record OperatingSplitDto(Guid ProjectId, string Name, decimal Thb);
public record SpendRowDto(Guid Id, string Name, decimal CostUsd, long Tokens, int Runs);
public record FinanceReportDto(DateOnly Month, decimal UsdToThb, decimal TotalUsd, decimal TotalThb, decimal SimulatedUsd,
    List<ProjectFinanceDto> Projects, List<SpendRowDto> Departments, List<SpendRowDto> Employees, List<BudgetDto> Budgets,
    decimal OperatingThb = 0, decimal OperatingEstimatedThb = 0, decimal TotalWithOperatingThb = 0, List<OperatingMonthDto>? Operating = null);

public record ProfitabilityRowDto(Guid ProjectId, string Code, string Name, int EstimateRevision, decimal PriceThb, decimal PlannedCostThb,
    decimal PlannedMarginThb, decimal PlannedMarginPercent, decimal PlannedAiThb, decimal ActualAiThb, decimal AiOverrunThb,
    decimal ForecastCostThb, decimal ForecastMarginThb, decimal ForecastMarginPercent, bool AtRisk, decimal OperatingThb = 0, decimal LabourOverrunThb = 0);
public record ProfitabilityReportDto(decimal UsdToThb, decimal TotalPriceThb, decimal TotalPlannedMarginThb, decimal TotalForecastMarginThb, List<ProfitabilityRowDto> Projects);
public record ScenarioRequest(Guid ProjectId, decimal HoursChangePercent = 0m, decimal RateChangePercent = 0m, decimal? AiCostUsd = null,
    decimal? UsdToThb = null, decimal? ContingencyPercent = null, decimal? PriceThb = null, bool RepriceAtTargetMargin = false);
public record ScenarioDto(Guid ProjectId, int EstimateRevision, ScenarioResult Baseline, ScenarioResult Scenario, decimal MarginChangeThb);

public record PerformanceRowDto(Guid EmployeeId, string Name, string JobTitle, string Department, int Runs, int Succeeded, int Failed, int LimitReached,
    decimal SuccessRate, decimal CostUsd, decimal CostPerRunUsd, double AvgTurns, int ToolCalls, int ToolDenied, int TasksDone, int ReviewsApproved,
    int ChangesRequested, decimal FirstPassApprovalRate);
public record PerformanceReportDto(DateOnly Month, List<PerformanceRowDto> Rows);

/// <summary>
/// How one model did in the month. Runs count on the model that finished them (after any fallback switch). Deliverables are
/// judged by AI reviewers; tasks done need a person's approval. SwitchedAway / TookOver count fallback switches from / to it.
/// </summary>
public record ModelPerformanceRowDto(string Provider, string ModelId, string DisplayName, int Runs, int Succeeded, int Failed, int LimitReached,
    decimal SuccessRate, int DeliverablesAccepted, int DeliverablesRejected, decimal? AcceptanceRate, int TasksDone, double AvgTurns, double AvgSeconds,
    long Tokens, decimal CostUsd, decimal CostPerRunUsd, int SwitchedAway, int TookOver, bool IsSimulated);
public record ModelPerformanceReportDto(DateOnly Month, List<ModelPerformanceRowDto> Rows);

public record CountDto(string Key, int Count);
public record SecurityEventDto(DateTimeOffset OccurredAt, string Action, string? ActorName, string? EntityId);
public record FailedTestDto(Guid Id, Guid RepositoryId, string Branch, string Status, DateTimeOffset StartedAt);
public record QualitySecurityDto(int Days, int TestRuns, int TestsPassed, decimal TestPassRate, List<FailedTestDto> RecentTestFailures,
    int OpenMergeRequests, int ConflictedMergeRequests, int MergedMergeRequests, int FailedRuns, int LimitReachedRuns, int ToolDenials,
    List<CountDto> PendingApprovals, List<CountDto> SecurityEventCounts, List<SecurityEventDto> RecentSecurityEvents);

/// <summary>Deterministic finance and performance figures. Estimates and actuals are always reported separately.</summary>
public class ReportsService(IAppDbContext db, TimeProvider clock)
{
    public DateOnly CurrentMonth => BudgetMath.MonthOf(clock.GetUtcNow());

    // ---------------- budgets ----------------

    private async Task<decimal> SpentAsync(BudgetScope scope, Guid? scopeId, DateOnly month, CancellationToken ct)
    {
        var (from, to) = BudgetMath.Range(month);
        var costs = db.CostRecords.Where(c => c.Kind == CostKind.Actual && c.OccurredAt >= from && c.OccurredAt < to);
        costs = scope switch
        {
            BudgetScope.Project => costs.Where(c => c.ProjectId == scopeId),
            BudgetScope.Department => costs.Where(c => db.Employees.Any(e => e.Id == c.EmployeeId && e.DepartmentId == scopeId)),
            _ => costs,
        };
        return await costs.SumAsync(c => (decimal?)c.AmountUsd, ct) ?? 0m;
    }

    public async Task<List<BudgetDto>> ListBudgetsAsync(DateOnly? month, CancellationToken ct)
    {
        var m = month ?? CurrentMonth;
        var rows = await db.BudgetAllocations.AsNoTracking().Where(b => b.Month == m).OrderBy(b => b.Scope).ToListAsync(ct);
        var depts = await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        var projects = await db.Projects.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var list = new List<BudgetDto>();
        foreach (var b in rows)
        {
            var spent = await SpentAsync(b.Scope, b.ScopeId, b.Month, ct);
            var name = b.Scope switch
            {
                BudgetScope.Department => depts.GetValueOrDefault(b.ScopeId!.Value, "?"),
                BudgetScope.Project => projects.GetValueOrDefault(b.ScopeId!.Value, "?"),
                _ => "Company",
            };
            list.Add(new BudgetDto(b.Id, b.Scope, b.ScopeId, name, b.Month, b.AmountUsd, spent, BudgetMath.Level(spent, b.AmountUsd), b.Version));
        }
        return list;
    }

    public async Task<BudgetDto> SetBudgetAsync(SetBudgetRequest req, CancellationToken ct)
    {
        if (!Enum.IsDefined(req.Scope)) throw new DomainException("validation", "scope is invalid.");
        if (req.AmountUsd is < 0 or > 1_000_000) throw new DomainException("validation", "amountUsd must be between 0 and 1,000,000.");
        if (req.Month.Day != 1) throw new DomainException("validation", "month must be the first day of a month.");
        if (req.Scope == BudgetScope.Company && req.ScopeId is not null) throw new DomainException("validation", "A company budget has no scopeId.");
        if (req.Scope == BudgetScope.Department && !await db.Departments.AnyAsync(d => d.Id == req.ScopeId, ct)) throw new NotFoundException("Department", req.ScopeId ?? Guid.Empty);
        if (req.Scope == BudgetScope.Project && !await db.Projects.AnyAsync(p => p.Id == req.ScopeId, ct)) throw new NotFoundException("Project", req.ScopeId ?? Guid.Empty);

        var b = await db.BudgetAllocations.FirstOrDefaultAsync(x => x.Scope == req.Scope && x.ScopeId == req.ScopeId && x.Month == req.Month, ct);
        if (b is null) db.BudgetAllocations.Add(b = new BudgetAllocation { Scope = req.Scope, ScopeId = req.ScopeId, Month = req.Month });
        b.AmountUsd = req.AmountUsd;
        await db.SaveChangesAsync(ct);
        return (await ListBudgetsAsync(req.Month, ct)).First(x => x.Id == b.Id);
    }

    public async Task DeleteBudgetAsync(Guid id, CancellationToken ct)
    {
        var b = await db.BudgetAllocations.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Budget", id);
        db.BudgetAllocations.Remove(b);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Null when a run may start; otherwise which budget is used up.</summary>
    public async Task<string?> BudgetBlockAsync(Guid employeeId, Guid projectId, CancellationToken ct)
    {
        var month = CurrentMonth;
        var deptId = await db.Employees.Where(e => e.Id == employeeId).Select(e => (Guid?)e.DepartmentId).FirstOrDefaultAsync(ct);
        var budgets = await db.BudgetAllocations.AsNoTracking()
            .Where(b => b.Month == month && (b.Scope == BudgetScope.Company || (b.Scope == BudgetScope.Department && b.ScopeId == deptId) || (b.Scope == BudgetScope.Project && b.ScopeId == projectId)))
            .ToListAsync(ct);
        foreach (var b in budgets)
            if (await SpentAsync(b.Scope, b.ScopeId, month, ct) >= b.AmountUsd)
                return $"The {b.Scope.ToString().ToLowerInvariant()} AI budget for {month:yyyy-MM} ({b.AmountUsd} USD) is used up.";
        return null;
    }

    // ---------------- finance ----------------

    public async Task<FinanceReportDto> FinanceAsync(DateOnly? month, CancellationToken ct)
    {
        var m = month ?? CurrentMonth;
        var (from, to) = BudgetMath.Range(m);
        var settings = await DeliveryService.SettingsAsync(db, ct);
        var monthCosts = await db.CostRecords.AsNoTracking().Where(c => c.Kind == CostKind.Actual && c.OccurredAt >= from && c.OccurredAt < to)
            .Select(c => new { c.AmountUsd, c.ProjectId, c.EmployeeId, c.RunId }).ToListAsync(ct);
        var simulatedRuns = await db.AgentRuns.AsNoTracking().Where(r => r.IsSimulated && r.CreatedAt >= from.AddDays(-1) && r.CreatedAt < to).Select(r => r.Id).ToListAsync(ct);
        var allTime = await db.CostRecords.AsNoTracking().Where(c => c.Kind == CostKind.Actual && c.ProjectId != null)
            .GroupBy(c => c.ProjectId!.Value).Select(g => new { ProjectId = g.Key, Usd = g.Sum(c => c.AmountUsd) }).ToDictionaryAsync(x => x.ProjectId, x => x.Usd, ct);
        var estimates = (await db.Estimates.AsNoTracking().Where(e => e.Status == EstimateStatus.Approved).ToListAsync(ct))
            .GroupBy(e => e.ProjectId).ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.Revision).First());
        var projects = await db.Projects.AsNoTracking().Where(p => p.Status != ProjectStatus.Cancelled).OrderBy(p => p.Code).ToListAsync(ct);
        var (opCosts, opShares) = await OperatingCostService.AllocateAsync(db, m, m, settings.UsdToThb, ct);
        var opAllTime = await OperatingCostService.AllTimeByProjectAsync(db, m, settings.UsdToThb, ct);

        var projectRows = projects.Select(p =>
        {
            var est = estimates.GetValueOrDefault(p.Id);
            var allUsd = allTime.GetValueOrDefault(p.Id);
            var allThb = Math.Round(allUsd * settings.UsdToThb, 2);
            return new ProjectFinanceDto(p.Id, p.Code, p.Name, est?.Revision, est?.TotalCost, est?.Price, est?.AiCost,
                monthCosts.Where(c => c.ProjectId == p.Id).Sum(c => c.AmountUsd), allUsd, allThb, est is null ? null : allThb - est.AiCost,
                opShares.Where(s => s.ProjectId == p.Id).Sum(s => s.Thb), opAllTime.GetValueOrDefault(p.Id));
        }).Where(r => r.EstimateRevision is not null || r.ActualAiUsdAllTime > 0 || r.OperatingThbAllTime > 0).ToList();
        var projectNames = projects.ToDictionary(p => p.Id, p => p.Name);
        var operating = opCosts.Where(c => c.AppliesTo(m)).Select(c =>
        {
            var mine = opShares.Where(s => s.CostId == c.Id).ToList();
            return new OperatingMonthDto(c.Id, c.Name, c.Category, c.IsEstimate, c.Allocation, c.Provider, mine.Sum(s => s.Thb),
                mine.Where(s => s.ProjectId != null).Select(s => new OperatingSplitDto(s.ProjectId!.Value, projectNames.GetValueOrDefault(s.ProjectId.Value, "?"), s.Thb)).ToList(),
                mine.Where(s => s.ProjectId == null).Sum(s => s.Thb));
        }).OrderByDescending(o => o.Thb).ThenBy(o => o.Name).ToList();
        var operatingThb = operating.Sum(o => o.Thb);

        var usage = await db.UsageRecords.AsNoTracking().Where(u => u.OccurredAt >= from && u.OccurredAt < to)
            .Select(u => new { u.EmployeeId, u.RunId, Tokens = u.InputTokens + u.OutputTokens + u.CacheReadTokens + u.CacheWriteTokens }).ToListAsync(ct);
        var employees = await db.Employees.AsNoTracking().Select(e => new { e.Id, e.DisplayName, e.DepartmentId }).ToDictionaryAsync(e => e.Id, ct);
        var departments = await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);

        var byEmployee = monthCosts.Where(c => c.EmployeeId != null).GroupBy(c => c.EmployeeId!.Value)
            .Select(g => new SpendRowDto(g.Key, employees.TryGetValue(g.Key, out var e) ? e.DisplayName : "?", g.Sum(c => c.AmountUsd),
                usage.Where(u => u.EmployeeId == g.Key).Sum(u => u.Tokens), usage.Where(u => u.EmployeeId == g.Key).Select(u => u.RunId).Distinct().Count()))
            .OrderByDescending(r => r.CostUsd).ToList();
        var byDept = byEmployee.Where(r => employees.ContainsKey(r.Id)).GroupBy(r => employees[r.Id].DepartmentId)
            .Select(g => new SpendRowDto(g.Key, departments.GetValueOrDefault(g.Key, "?"), g.Sum(r => r.CostUsd), g.Sum(r => r.Tokens), g.Sum(r => r.Runs)))
            .OrderByDescending(r => r.CostUsd).ToList();

        var total = monthCosts.Sum(c => c.AmountUsd);
        var totalThb = Math.Round(total * settings.UsdToThb, 2);
        return new FinanceReportDto(m, settings.UsdToThb, total, totalThb,
            monthCosts.Where(c => c.RunId is { } r && simulatedRuns.Contains(r)).Sum(c => c.AmountUsd),
            projectRows, byDept, byEmployee.Take(50).ToList(), await ListBudgetsAsync(m, ct),
            operatingThb, operating.Where(o => o.IsEstimate).Sum(o => o.Thb), totalThb + operatingThb, operating);
    }

    // ---------------- profitability & scenarios ----------------

    /// <summary>
    /// Estimated profitability per project from its latest approved estimate. The forecast keeps the estimate's
    /// labor and contingency and adds any AI spend above the planned AI cost; it is an estimate, not an actual result.
    /// </summary>
    public async Task<ProfitabilityReportDto> ProfitabilityAsync(CancellationToken ct)
    {
        var settings = await DeliveryService.SettingsAsync(db, ct);
        var estimates = (await db.Estimates.AsNoTracking().Where(e => e.Status == EstimateStatus.Approved).ToListAsync(ct))
            .GroupBy(e => e.ProjectId).ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.Revision).First());
        var actual = await db.CostRecords.AsNoTracking().Where(c => c.Kind == CostKind.Actual && c.ProjectId != null)
            .GroupBy(c => c.ProjectId!.Value).Select(g => new { ProjectId = g.Key, Usd = g.Sum(c => c.AmountUsd) }).ToDictionaryAsync(x => x.ProjectId, x => x.Usd, ct);
        var projects = await db.Projects.AsNoTracking().Where(p => p.Status != ProjectStatus.Cancelled && estimates.Keys.Contains(p.Id))
            .OrderBy(p => p.Code).ToListAsync(ct);
        var operating = await OperatingCostService.AllTimeByProjectAsync(db, CurrentMonth, settings.UsdToThb, ct);
        var labour = new Dictionary<Guid, decimal?>();
        foreach (var p in projects) labour[p.Id] = (await Work.TimesheetService.ActualAsync(db, p.Id, ct)).LabourThb;

        var rows = projects.Select(p => Forecast(p, estimates[p.Id], actual.GetValueOrDefault(p.Id), settings.UsdToThb, operating.GetValueOrDefault(p.Id), labour[p.Id])).ToList();
        return new ProfitabilityReportDto(settings.UsdToThb, rows.Sum(r => r.PriceThb), rows.Sum(r => r.PlannedMarginThb), rows.Sum(r => r.ForecastMarginThb), rows);
    }

    /// <summary>
    /// Planned figures from the approved estimate; forecast cost adds any actual AI spend above the planned AI cost, and the
    /// operating costs (electricity, flat-rate AI plans, hosting...) allocated to the project, which the estimate does not cover,
    /// and recorded labour above the planned labour (hours people logged × their position rates; null when not costable).
    /// </summary>
    internal static ProfitabilityRowDto Forecast(Project p, ProjectEstimate e, decimal actualAiUsd, decimal usdToThb, decimal operatingThb = 0, decimal? actualLabourThb = null)
    {
        var actualThb = Math.Round(actualAiUsd * usdToThb, 2, MidpointRounding.AwayFromZero);
        var overrun = Math.Max(0m, actualThb - e.AiCost);
        var labourOverrun = actualLabourThb is { } l ? Math.Max(0m, l - e.LaborCost) : 0m;
        var forecastCost = e.TotalCost + overrun + operatingThb + labourOverrun;
        var forecastMargin = e.Price - forecastCost;
        var forecastPct = e.Price == 0 ? 0 : Math.Round(forecastMargin / e.Price * 100m, 2, MidpointRounding.AwayFromZero);
        // At risk once the forecast margin has lost more than half of the planned margin, or turned negative.
        var atRisk = forecastMargin < 0 || forecastMargin < e.GrossMargin / 2;
        return new ProfitabilityRowDto(p.Id, p.Code, p.Name, e.Revision, e.Price, e.TotalCost, e.GrossMargin, e.GrossMarginPercent,
            e.AiCost, actualThb, overrun, forecastCost, forecastMargin, forecastPct, atRisk, operatingThb, labourOverrun);
    }

    /// <summary>What-if planning on the latest approved estimate. Read-only: nothing is stored or changed.</summary>
    public async Task<ScenarioDto> ScenarioAsync(ScenarioRequest req, CancellationToken ct)
    {
        var e = await db.Estimates.AsNoTracking().Include(x => x.Items)
            .Where(x => x.ProjectId == req.ProjectId && x.Status == EstimateStatus.Approved).OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct)
            ?? throw new DomainException("scenario.no_estimate", "This project has no approved estimate to plan from.");
        var baseline = ScenarioCalculator.Baseline(e);
        var scenario = ScenarioCalculator.Run(e, new ScenarioInput(req.HoursChangePercent, req.RateChangePercent, req.AiCostUsd, req.UsdToThb,
            req.ContingencyPercent, req.PriceThb, req.RepriceAtTargetMargin));
        return new ScenarioDto(req.ProjectId, e.Revision, baseline, scenario, scenario.GrossMargin - baseline.GrossMargin);
    }

    // ---------------- quality & security ----------------

    private static readonly string[] SecurityActions =
        [Domain.Audit.AuditActions.LoginFailed, Domain.Audit.AuditActions.LockedOut, Domain.Audit.AuditActions.AccessDenied, Domain.Audit.AuditActions.PermissionChangeDenied];

    public async Task<QualitySecurityDto> QualitySecurityAsync(int days, CancellationToken ct)
    {
        days = Math.Clamp(days, 1, 365);
        var since = clock.GetUtcNow().AddDays(-days);
        var tests = await db.TestRuns.AsNoTracking().Where(t => t.StartedAt >= since && t.Kind == Domain.Code.CheckKind.Test)
            .Select(t => new { t.Id, t.RepositoryId, t.Branch, t.Status, t.StartedAt }).ToListAsync(ct);
        var passed = tests.Count(t => t.Status == Domain.Code.TestRunStatus.Passed);
        var mrs = await db.MergeRequests.AsNoTracking().GroupBy(m => m.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var runs = await db.AgentRuns.AsNoTracking().Where(r => r.CreatedAt >= since).GroupBy(r => r.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var denials = await db.ToolExecutions.AsNoTracking().CountAsync(x => x.StartedAt >= since && x.Status == ToolExecutionStatus.Denied, ct);
        var approvals = await db.ApprovalRequests.AsNoTracking().Where(a => a.Status == ApprovalStatus.Pending)
            .GroupBy(a => a.Kind).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var events = db.AuditLogs.AsNoTracking().Where(a => a.OccurredAt >= since && SecurityActions.Contains(a.Action));
        var eventCounts = await events.GroupBy(a => a.Action).Select(g => new CountDto(g.Key, g.Count())).ToListAsync(ct);
        var recent = await events.OrderByDescending(a => a.Id).Take(25)
            .Select(a => new SecurityEventDto(a.OccurredAt, a.Action, a.ActorName, a.EntityId)).ToListAsync(ct);
        int Mr(Domain.Code.MergeRequestStatus s) => mrs.FirstOrDefault(m => m.Key == s)?.Count ?? 0;
        int Run(AgentRunStatus s) => runs.FirstOrDefault(r => r.Key == s)?.Count ?? 0;
        return new QualitySecurityDto(days, tests.Count, passed, tests.Count == 0 ? 0 : Math.Round(100m * passed / tests.Count, 1),
            tests.Where(t => t.Status != Domain.Code.TestRunStatus.Passed).OrderByDescending(t => t.StartedAt).Take(10)
                .Select(t => new FailedTestDto(t.Id, t.RepositoryId, t.Branch, t.Status.ToString(), t.StartedAt)).ToList(),
            Mr(Domain.Code.MergeRequestStatus.Open), Mr(Domain.Code.MergeRequestStatus.Conflict), Mr(Domain.Code.MergeRequestStatus.Merged),
            Run(AgentRunStatus.Failed), Run(AgentRunStatus.LimitReached), denials,
            approvals.Select(a => new CountDto(a.Key.ToString(), a.Count)).ToList(), eventCounts, recent);
    }

    // ---------------- performance ----------------

    private static readonly System.Text.RegularExpressions.Regex Switched = new(@"^Switched from (\S+) to the fallback model (\S+):", System.Text.RegularExpressions.RegexOptions.Compiled);

    public async Task<ModelPerformanceReportDto> ModelPerformanceAsync(DateOnly? month, CancellationToken ct)
    {
        var m = month ?? CurrentMonth;
        var (from, to) = BudgetMath.Range(m);
        var runs = await db.AgentRuns.AsNoTracking().Where(r => r.CreatedAt >= from && r.CreatedAt < to)
            .Select(r => new { r.Id, r.Provider, r.ModelId, r.Status, r.Turns, r.CostUsd, r.TaskId, r.Purpose, r.IsSimulated, r.StartedAt, r.FinishedAt, Tokens = r.InputTokens + r.OutputTokens })
            .ToListAsync(ct);
        var runIds = runs.Select(r => r.Id).ToList();
        var deliverables = await db.Deliverables.AsNoTracking().Where(d => d.RunId != null && runIds.Contains(d.RunId.Value))
            .Select(d => new { RunId = d.RunId!.Value, d.Status, d.TaskId }).ToListAsync(ct);
        var doneTasks = (await db.Tasks.AsNoTracking().Where(t => t.Status == WorkTaskStatus.Done && deliverables.Select(d => d.TaskId).Contains(t.Id)).Select(t => t.Id).ToListAsync(ct)).ToHashSet();
        var switches = (await db.AgentRunSteps.AsNoTracking().Where(s => runIds.Contains(s.RunId) && s.Kind == RunStepKind.Notice && s.Content.StartsWith("Switched from "))
            .Select(s => s.Content).ToListAsync(ct)).Select(c => Switched.Match(c)).Where(x => x.Success).Select(x => (From: x.Groups[1].Value, To: x.Groups[2].Value)).ToList();
        var names = await db.AiModels.AsNoTracking().ToDictionaryAsync(x => $"{x.Provider}/{x.ModelId}", x => x.DisplayName, ct);

        var keys = runs.Select(r => $"{r.Provider}/{r.ModelId}").Concat(switches.Select(s => s.From)).Distinct();
        var rows = keys.Select(key =>
        {
            var mine = runs.Where(r => $"{r.Provider}/{r.ModelId}" == key).ToList();
            var mineIds = mine.Select(r => r.Id).ToHashSet();
            var delivered = deliverables.Where(d => mineIds.Contains(d.RunId)).ToList();
            var accepted = delivered.Count(d => d.Status == DeliverableStatus.Accepted);
            var rejected = delivered.Count(d => d.Status == DeliverableStatus.Rejected);
            var finished = mine.Where(r => r.StartedAt != null && r.FinishedAt != null).ToList();
            var cost = mine.Sum(r => r.CostUsd);
            var slash = key.IndexOf('/');
            return new ModelPerformanceRowDto(key[..slash], key[(slash + 1)..], names.GetValueOrDefault(key, key[(slash + 1)..]), mine.Count,
                mine.Count(r => r.Status == AgentRunStatus.Succeeded), mine.Count(r => r.Status == AgentRunStatus.Failed), mine.Count(r => r.Status == AgentRunStatus.LimitReached),
                mine.Count == 0 ? 0 : Math.Round(100m * mine.Count(r => r.Status == AgentRunStatus.Succeeded) / mine.Count, 1),
                accepted, rejected, accepted + rejected == 0 ? null : Math.Round(100m * accepted / (accepted + rejected), 1),
                delivered.Select(d => d.TaskId).Distinct().Count(doneTasks.Contains),
                mine.Count == 0 ? 0 : Math.Round(mine.Average(r => r.Turns), 1),
                finished.Count == 0 ? 0 : Math.Round(finished.Average(r => (r.FinishedAt!.Value - r.StartedAt!.Value).TotalSeconds), 0),
                mine.Sum(r => r.Tokens), cost, mine.Count == 0 ? 0 : Math.Round(cost / mine.Count, 6),
                switches.Count(s => s.From == key), switches.Count(s => s.To == key), mine.Count > 0 && mine.All(r => r.IsSimulated));
        }).OrderBy(r => r.IsSimulated).ThenByDescending(r => r.Runs).ToList();
        return new ModelPerformanceReportDto(m, rows);
    }

    public async Task<PerformanceReportDto> PerformanceAsync(DateOnly? month, CancellationToken ct)
    {
        var m = month ?? CurrentMonth;
        var (from, to) = BudgetMath.Range(m);
        var runs = await db.AgentRuns.AsNoTracking().Where(r => r.CreatedAt >= from && r.CreatedAt < to)
            .Select(r => new { r.Id, r.EmployeeId, r.Status, r.Turns, r.ToolCalls, r.CostUsd, r.TaskId, r.Purpose }).ToListAsync(ct);
        var runIds = runs.Select(r => r.Id).ToList();
        var denied = await db.ToolExecutions.AsNoTracking().Where(x => runIds.Contains(x.RunId) && x.Status == ToolExecutionStatus.Denied)
            .GroupBy(x => x.RunId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        // Work outcome per employee: reviews of tasks they were assigned to, and tasks they completed.
        var reviews = await db.Reviews.AsNoTracking().Where(r => r.CreatedAt >= from && r.CreatedAt < to)
            .Join(db.TaskAssignments, r => r.TaskId, a => a.TaskId, (r, a) => new { a.EmployeeId, r.Outcome, r.TaskId }).ToListAsync(ct);
        var done = await db.Tasks.AsNoTracking().Where(t => t.Status == WorkTaskStatus.Done && t.UpdatedAt >= from && t.UpdatedAt < to)
            .Join(db.TaskAssignments.Where(a => a.UnassignedAt == null), t => t.Id, a => a.TaskId, (t, a) => a.EmployeeId).ToListAsync(ct);
        var people = await db.Employees.AsNoTracking().Where(e => e.Kind == EmployeeKind.AI)
            .Select(e => new { e.Id, e.DisplayName, Title = e.RoleDefinition!.Title, Dept = e.Department!.Name }).ToListAsync(ct);

        var rows = people.Select(p =>
        {
            var mine = runs.Where(r => r.EmployeeId == p.Id).ToList();
            var approved = reviews.Count(r => r.EmployeeId == p.Id && r.Outcome == Domain.Workflows.ReviewOutcome.Approved);
            var changes = reviews.Count(r => r.EmployeeId == p.Id && r.Outcome == Domain.Workflows.ReviewOutcome.ChangesRequested);
            var reviewedTasks = reviews.Where(r => r.EmployeeId == p.Id).GroupBy(r => r.TaskId).ToList();
            var firstPass = reviewedTasks.Count == 0 ? 0m : Math.Round(100m * reviewedTasks.Count(g => g.All(r => r.Outcome == Domain.Workflows.ReviewOutcome.Approved)) / reviewedTasks.Count, 1);
            var cost = mine.Sum(r => r.CostUsd);
            return new PerformanceRowDto(p.Id, p.DisplayName, p.Title, p.Dept, mine.Count,
                mine.Count(r => r.Status == AgentRunStatus.Succeeded), mine.Count(r => r.Status == AgentRunStatus.Failed), mine.Count(r => r.Status == AgentRunStatus.LimitReached),
                mine.Count == 0 ? 0 : Math.Round(100m * mine.Count(r => r.Status == AgentRunStatus.Succeeded) / mine.Count, 1),
                cost, mine.Count == 0 ? 0 : Math.Round(cost / mine.Count, 6), mine.Count == 0 ? 0 : Math.Round(mine.Average(r => r.Turns), 1),
                mine.Sum(r => r.ToolCalls), mine.Sum(r => denied.GetValueOrDefault(r.Id)), done.Count(e => e == p.Id), approved, changes, firstPass);
        }).Where(r => r.Runs > 0 || r.TasksDone > 0).OrderByDescending(r => r.Runs).ToList();
        return new PerformanceReportDto(m, rows);
    }
}
