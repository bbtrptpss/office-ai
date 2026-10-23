using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Application.Agents;
using OfficeAI.Application.Delivery;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Finance;

namespace OfficeAI.Application.Reports;

public record OperatingCostDto(Guid Id, string Name, string Category, decimal MonthlyAmount, CostCurrency Currency, decimal MonthlyThb, bool IsEstimate,
    DateOnly StartMonth, DateOnly? EndMonth, OperatingCostAllocation Allocation, Guid? ProjectId, string? ProjectName, string? Provider, string? Notes, uint Version);
public record SaveOperatingCostRequest(string Name, string Category, decimal MonthlyAmount, CostCurrency Currency, bool IsEstimate,
    DateOnly StartMonth, DateOnly? EndMonth, OperatingCostAllocation Allocation, Guid? ProjectId = null, string? Provider = null, string? Notes = null, uint? Version = null);

/// <summary>One cost's share for one month: to a project, or unallocated (overhead, or no AI use that month to split it by).</summary>
public record OperatingShare(Guid CostId, DateOnly Month, Guid? ProjectId, decimal Thb);

/// <summary>AI tokens used in one month, by provider and project: what <see cref="OperatingCostAllocation.AiUsage"/> splits by.</summary>
public record UsageByMonth(DateOnly Month, string Provider, Guid ProjectId, long Tokens);

/// <summary>Deterministic month-by-month split of operating costs. Amounts are THB, rounded to satang, and each month's shares add up exactly.</summary>
public static class OperatingCostAllocator
{
    public static decimal MonthlyThb(OperatingCost c, decimal usdToThb) =>
        c.Currency == CostCurrency.Usd ? Math.Round(c.MonthlyAmount * usdToThb, 2, MidpointRounding.AwayFromZero) : c.MonthlyAmount;

    public static List<OperatingShare> Allocate(IEnumerable<OperatingCost> costs, IReadOnlyCollection<UsageByMonth> usage, DateOnly from, DateOnly to, decimal usdToThb)
    {
        var shares = new List<OperatingShare>();
        foreach (var c in costs)
        {
            var thb = MonthlyThb(c, usdToThb);
            for (var m = c.StartMonth < from ? from : c.StartMonth; m <= to; m = m.AddMonths(1))
            {
                if (!c.AppliesTo(m)) continue;
                switch (c.Allocation)
                {
                    case OperatingCostAllocation.Project:
                        shares.Add(new OperatingShare(c.Id, m, c.ProjectId, thb));
                        break;
                    case OperatingCostAllocation.AiUsage:
                        var byProject = usage.Where(u => u.Month == m && (c.Provider == null || string.Equals(u.Provider, c.Provider, StringComparison.OrdinalIgnoreCase)))
                            .GroupBy(u => u.ProjectId).Select(g => (Project: g.Key, Tokens: g.Sum(u => u.Tokens))).Where(x => x.Tokens > 0)
                            .OrderByDescending(x => x.Tokens).ThenBy(x => x.Project).ToList();
                        var total = byProject.Sum(x => x.Tokens);
                        if (total == 0)
                        {
                            shares.Add(new OperatingShare(c.Id, m, null, thb)); // nothing to split by this month
                            break;
                        }
                        var parts = byProject.Select(x => Math.Round(thb * x.Tokens / total, 2, MidpointRounding.AwayFromZero)).ToList();
                        parts[0] += thb - parts.Sum(); // rounding remainder to the largest user, so the month adds up exactly
                        shares.AddRange(byProject.Select((x, i) => new OperatingShare(c.Id, m, x.Project, parts[i])));
                        break;
                    default:
                        shares.Add(new OperatingShare(c.Id, m, null, thb));
                        break;
                }
            }
        }
        return shares;
    }
}

/// <summary>Operating costs people enter (finance.manage), and their allocation to projects for the finance reports.</summary>
public class OperatingCostService(IAppDbContext db, AiProviderRegistry providers)
{
    public static readonly string[] Categories = ["electricity", "ai_subscription", "hosting", "hardware", "software", "other"];

    public async Task<List<OperatingCostDto>> ListAsync(CancellationToken ct)
    {
        var settings = await DeliveryService.SettingsAsync(db, ct);
        var projects = await db.Projects.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        return (await db.OperatingCosts.AsNoTracking().OrderBy(c => c.EndMonth != null).ThenBy(c => c.Name).ToListAsync(ct))
            .Select(c => Dto(c, settings.UsdToThb, projects)).ToList();
    }

    private static OperatingCostDto Dto(OperatingCost c, decimal usdToThb, IReadOnlyDictionary<Guid, string> projects) =>
        new(c.Id, c.Name, c.Category, c.MonthlyAmount, c.Currency, OperatingCostAllocator.MonthlyThb(c, usdToThb), c.IsEstimate, c.StartMonth, c.EndMonth,
            c.Allocation, c.ProjectId, c.ProjectId is { } p ? projects.GetValueOrDefault(p, "?") : null, c.Provider, c.Notes, c.Version);

    public async Task<OperatingCostDto> CreateAsync(SaveOperatingCostRequest req, CancellationToken ct)
    {
        var cost = new OperatingCost { Name = "", Category = "" };
        await ApplyAsync(cost, req, ct);
        db.OperatingCosts.Add(cost);
        await db.SaveChangesAsync(ct);
        return (await ListAsync(ct)).First(x => x.Id == cost.Id);
    }

    public async Task<OperatingCostDto> UpdateAsync(Guid id, SaveOperatingCostRequest req, CancellationToken ct)
    {
        var cost = await db.OperatingCosts.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("OperatingCost", id);
        if (req.Version is { } v) db.ExpectVersion(cost, v);
        await ApplyAsync(cost, req, ct);
        await db.SaveChangesAsync(ct);
        return (await ListAsync(ct)).First(x => x.Id == id);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var cost = await db.OperatingCosts.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("OperatingCost", id);
        db.OperatingCosts.Remove(cost);
        await db.SaveChangesAsync(ct);
    }

    private async Task ApplyAsync(OperatingCost c, SaveOperatingCostRequest req, CancellationToken ct)
    {
        Validate.Required(req.Name, "name");
        var name = req.Name.Trim();
        if (name.Length > 200) throw new DomainException("validation", "name must be at most 200 characters.");
        if (!Categories.Contains(req.Category)) throw new DomainException("validation", $"category must be one of: {string.Join(", ", Categories)}.");
        if (req.MonthlyAmount is < 0 or > 10_000_000) throw new DomainException("validation", "monthlyAmount must be between 0 and 10,000,000.");
        if (!Enum.IsDefined(req.Currency)) throw new DomainException("validation", "currency is invalid.");
        if (!Enum.IsDefined(req.Allocation)) throw new DomainException("validation", "allocation is invalid.");
        if (req.StartMonth.Day != 1 || req.EndMonth is { Day: not 1 }) throw new DomainException("validation", "Months must be the first day of a month.");
        if (req.EndMonth < req.StartMonth) throw new DomainException("validation", "The end month is before the start month.");
        if (req.Notes is { Length: > 1000 }) throw new DomainException("validation", "notes must be at most 1000 characters.");
        Guid? projectId = null;
        string? provider = null;
        if (req.Allocation == OperatingCostAllocation.Project)
        {
            projectId = req.ProjectId ?? throw new DomainException("validation", "Choose the project this cost belongs to.");
            if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) throw new NotFoundException("Project", projectId.Value);
        }
        else if (req.Allocation == OperatingCostAllocation.AiUsage && !string.IsNullOrWhiteSpace(req.Provider))
        {
            provider = providers.Find(req.Provider.Trim())?.Name ?? throw new DomainException("validation", $"Unknown provider '{req.Provider}'.");
        }

        c.Name = name;
        c.Category = req.Category;
        c.MonthlyAmount = req.MonthlyAmount;
        c.Currency = req.Currency;
        c.IsEstimate = req.IsEstimate;
        c.StartMonth = req.StartMonth;
        c.EndMonth = req.EndMonth;
        c.Allocation = req.Allocation;
        c.ProjectId = projectId;
        c.Provider = provider;
        c.Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim();
    }

    /// <summary>Every operating cost's shares from <paramref name="from"/> to <paramref name="to"/> (inclusive months), with the costs themselves.</summary>
    public static async Task<(List<OperatingCost> Costs, List<OperatingShare> Shares)> AllocateAsync(IAppDbContext db, DateOnly from, DateOnly to, decimal usdToThb, CancellationToken ct)
    {
        var costs = await db.OperatingCosts.AsNoTracking().Where(c => c.StartMonth <= to && (c.EndMonth == null || c.EndMonth >= from)).ToListAsync(ct);
        if (costs.Count == 0) return (costs, []);
        List<UsageByMonth> usage = [];
        if (costs.Any(c => c.Allocation == OperatingCostAllocation.AiUsage))
        {
            var (start, _) = BudgetMath.Range(costs.Min(c => c.StartMonth) < from ? from : costs.Min(c => c.StartMonth));
            var (_, end) = BudgetMath.Range(to);
            usage = (await db.UsageRecords.AsNoTracking().Where(u => !u.IsSimulated && u.OccurredAt >= start && u.OccurredAt < end)
                    .GroupBy(u => new { u.OccurredAt.Year, u.OccurredAt.Month, u.Provider, u.ProjectId })
                    .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Provider, g.Key.ProjectId, Tokens = g.Sum(u => u.InputTokens + u.OutputTokens + u.CacheReadTokens + u.CacheWriteTokens) })
                    .ToListAsync(ct))
                .Select(x => new UsageByMonth(new DateOnly(x.Year, x.Month, 1), x.Provider, x.ProjectId, x.Tokens)).ToList();
        }
        return (costs, OperatingCostAllocator.Allocate(costs, usage, from, to, usdToThb));
    }

    /// <summary>Allocated operating cost per project from the first cost to <paramref name="to"/>.</summary>
    public static async Task<Dictionary<Guid, decimal>> AllTimeByProjectAsync(IAppDbContext db, DateOnly to, decimal usdToThb, CancellationToken ct)
    {
        var (_, shares) = await AllocateAsync(db, DateOnly.MinValue, to, usdToThb, ct);
        return shares.Where(s => s.ProjectId != null).GroupBy(s => s.ProjectId!.Value).ToDictionary(g => g.Key, g => g.Sum(s => s.Thb));
    }
}
