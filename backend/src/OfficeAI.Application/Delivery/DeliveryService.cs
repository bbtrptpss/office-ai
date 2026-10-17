using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Delivery;

namespace OfficeAI.Application.Delivery;

public record RequirementDto(Guid Id, string Code, RequirementKind Kind, string Title, string? Description, string? AcceptanceCriteria,
    RequirementPriority Priority, ReviewState Status, string? AuthorName, DateTimeOffset CreatedAt, uint Version);
public record SaveRequirementRequest(RequirementKind Kind, string Title, string? Description, string? AcceptanceCriteria, RequirementPriority Priority, uint? Version = null);
public record SetReviewStateRequest(ReviewState Status);
public record DecisionDto(Guid Id, int Number, string Title, string Context, string Decision, string? Consequences, DecisionStatus Status, string? AuthorName, DateTimeOffset CreatedAt);
public record SetDecisionStatusRequest(DecisionStatus Status);
public record EstimateItemDto(string Description, string RoleTitle, decimal Hours, decimal RateThb, decimal Amount, string? Evidence);
public record EstimateDto(Guid Id, int Revision, EstimateStatus Status, string Currency, string? Assumptions, decimal ContingencyPercent, decimal TargetMarginPercent,
    decimal AiCostUsd, decimal UsdToThb, decimal TotalHours, decimal LaborCost, decimal AiCost, decimal Contingency, decimal TotalCost, decimal Price,
    decimal GrossMargin, decimal GrossMarginPercent, List<string> Warnings, string? AuthorName, DateTimeOffset CreatedAt, List<EstimateItemDto> Items);
public record RoleRateDto(Guid RoleDefinitionId, string Code, string Title, decimal HourlyRateThb);
public record SetRoleRateRequest(decimal HourlyRateThb);
public record SettingsDto(decimal UsdToThb, decimal DefaultContingencyPercent, decimal TargetMarginPercent, uint Version);
public record SaveSettingsRequest(decimal UsdToThb, decimal DefaultContingencyPercent, decimal TargetMarginPercent, uint? Version = null);

/// <summary>Requirements, ADRs, estimates and the commercial settings behind them.</summary>
public class DeliveryService(IAppDbContext db)
{
    internal static async Task<string> NextRequirementCodeAsync(IAppDbContext db, Guid projectId, CancellationToken ct)
    {
        var count = await db.Requirements.CountAsync(r => r.ProjectId == projectId, ct);
        return $"REQ-{count + 1:000}";
    }

    public static async Task<CompanySettings> SettingsAsync(IAppDbContext db, CancellationToken ct) =>
        await db.CompanySettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new CompanySettings();

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.OfType<Guid>().Distinct().ToList();
        return await db.Employees.AsNoTracking().Where(e => list.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.DisplayName, ct);
    }

    // ---------- Requirements ----------

    public async Task<List<RequirementDto>> ListRequirementsAsync(Guid projectId, CancellationToken ct)
    {
        var rows = await db.Requirements.AsNoTracking().Where(r => r.ProjectId == projectId).OrderBy(r => r.Code).ToListAsync(ct);
        var names = await NamesAsync(rows.Select(r => r.AuthorEmployeeId), ct);
        return rows.Select(r => new RequirementDto(r.Id, r.Code, r.Kind, r.Title, r.Description, r.AcceptanceCriteria, r.Priority, r.Status,
            r.AuthorEmployeeId is { } a ? names.GetValueOrDefault(a) : null, r.CreatedAt, r.Version)).ToList();
    }

    public async Task<RequirementDto> CreateRequirementAsync(Guid projectId, SaveRequirementRequest req, CancellationToken ct)
    {
        Validate.Required(req.Title, "title");
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) throw new NotFoundException("Project", projectId);
        var r = new ProjectRequirement { ProjectId = projectId, Code = await NextRequirementCodeAsync(db, projectId, ct), Title = req.Title.Trim() };
        Apply(r, req);
        db.Requirements.Add(r);
        await db.SaveChangesAsync(ct);
        return (await ListRequirementsAsync(projectId, ct)).First(x => x.Id == r.Id);
    }

    public async Task<RequirementDto> UpdateRequirementAsync(Guid id, SaveRequirementRequest req, CancellationToken ct)
    {
        Validate.Required(req.Title, "title");
        var r = await db.Requirements.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Requirement", id);
        if (req.Version is { } v) db.ExpectVersion(r, v);
        r.Title = req.Title.Trim();
        Apply(r, req);
        await db.SaveChangesAsync(ct);
        return (await ListRequirementsAsync(r.ProjectId, ct)).First(x => x.Id == id);
    }

    private static void Apply(ProjectRequirement r, SaveRequirementRequest q)
    {
        if (!Enum.IsDefined(q.Kind) || !Enum.IsDefined(q.Priority)) throw new DomainException("validation", "kind or priority is invalid.");
        r.Kind = q.Kind;
        r.Description = q.Description;
        r.AcceptanceCriteria = q.AcceptanceCriteria;
        r.Priority = q.Priority;
    }

    public async Task<RequirementDto> SetRequirementStatusAsync(Guid id, SetReviewStateRequest req, CancellationToken ct)
    {
        var r = await db.Requirements.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Requirement", id);
        if (!Enum.IsDefined(req.Status)) throw new DomainException("validation", "status is invalid.");
        r.Status = req.Status;
        await db.SaveChangesAsync(ct);
        return (await ListRequirementsAsync(r.ProjectId, ct)).First(x => x.Id == id);
    }

    // ---------- Architecture decisions ----------

    public async Task<List<DecisionDto>> ListDecisionsAsync(Guid projectId, CancellationToken ct)
    {
        var rows = await db.ArchitectureDecisions.AsNoTracking().Where(a => a.ProjectId == projectId).OrderBy(a => a.Number).ToListAsync(ct);
        var names = await NamesAsync(rows.Select(r => r.AuthorEmployeeId), ct);
        return rows.Select(a => new DecisionDto(a.Id, a.Number, a.Title, a.Context, a.Decision, a.Consequences, a.Status,
            a.AuthorEmployeeId is { } x ? names.GetValueOrDefault(x) : null, a.CreatedAt)).ToList();
    }

    public async Task<DecisionDto> SetDecisionStatusAsync(Guid id, SetDecisionStatusRequest req, CancellationToken ct)
    {
        var a = await db.ArchitectureDecisions.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("ArchitectureDecision", id);
        if (!Enum.IsDefined(req.Status)) throw new DomainException("validation", "status is invalid.");
        a.Status = req.Status;
        await db.SaveChangesAsync(ct);
        return (await ListDecisionsAsync(a.ProjectId, ct)).First(x => x.Id == id);
    }

    // ---------- Estimates ----------

    public async Task<List<EstimateDto>> ListEstimatesAsync(Guid projectId, CancellationToken ct)
    {
        var rows = await db.Estimates.AsNoTracking().Include(e => e.Items).Where(e => e.ProjectId == projectId).OrderByDescending(e => e.Revision).ToListAsync(ct);
        var names = await NamesAsync(rows.Select(r => r.AuthorEmployeeId), ct);
        return rows.Select(e => new EstimateDto(e.Id, e.Revision, e.Status, e.Currency, e.Assumptions, e.ContingencyPercent, e.TargetMarginPercent, e.AiCostUsd,
            e.UsdToThb, e.TotalHours, e.LaborCost, e.AiCost, e.Contingency, e.TotalCost, e.Price, e.GrossMargin, e.GrossMarginPercent, e.Warnings,
            e.AuthorEmployeeId is { } a ? names.GetValueOrDefault(a) : null, e.CreatedAt,
            e.Items.Select(i => new EstimateItemDto(i.Description, i.RoleTitle, i.Hours, i.RateThb, i.Amount, i.Evidence)).ToList())).ToList();
    }

    // ---------- Rate card & settings ----------

    public async Task<List<RoleRateDto>> ListRatesAsync(CancellationToken ct) =>
        await db.RoleDefinitions.AsNoTracking().OrderBy(r => r.Level).ThenBy(r => r.Title)
            .Select(r => new RoleRateDto(r.Id, r.Code, r.Title, db.RoleRates.Where(x => x.RoleDefinitionId == r.Id).Select(x => x.HourlyRateThb).FirstOrDefault()))
            .ToListAsync(ct);

    public async Task<RoleRateDto> SetRateAsync(Guid roleDefinitionId, SetRoleRateRequest req, CancellationToken ct)
    {
        if (req.HourlyRateThb is < 0 or > 1_000_000) throw new DomainException("validation", "hourlyRateThb must be between 0 and 1,000,000.");
        if (!await db.RoleDefinitions.AnyAsync(r => r.Id == roleDefinitionId, ct)) throw new NotFoundException("RoleDefinition", roleDefinitionId);
        var rate = await db.RoleRates.FirstOrDefaultAsync(r => r.RoleDefinitionId == roleDefinitionId, ct);
        if (rate is null) db.RoleRates.Add(rate = new RoleRate { RoleDefinitionId = roleDefinitionId });
        rate.HourlyRateThb = req.HourlyRateThb;
        await db.SaveChangesAsync(ct);
        return (await ListRatesAsync(ct)).First(r => r.RoleDefinitionId == roleDefinitionId);
    }

    public async Task<SettingsDto> GetSettingsAsync(CancellationToken ct)
    {
        var s = await SettingsAsync(db, ct);
        return new SettingsDto(s.UsdToThb, s.DefaultContingencyPercent, s.TargetMarginPercent, s.Version);
    }

    public async Task<SettingsDto> SaveSettingsAsync(SaveSettingsRequest req, CancellationToken ct)
    {
        if (req.UsdToThb is <= 0 or > 1000) throw new DomainException("validation", "usdToThb must be between 0 and 1000.");
        if (req.DefaultContingencyPercent is < 0 or > 100 || req.TargetMarginPercent is < 0 or > 90)
            throw new DomainException("validation", "Contingency must be 0–100% and target margin 0–90%.");
        var s = await db.CompanySettings.FirstOrDefaultAsync(ct);
        if (s is null) db.CompanySettings.Add(s = new CompanySettings());
        else if (req.Version is { } v) db.ExpectVersion(s, v);
        s.UsdToThb = req.UsdToThb;
        s.DefaultContingencyPercent = req.DefaultContingencyPercent;
        s.TargetMarginPercent = req.TargetMarginPercent;
        await db.SaveChangesAsync(ct);
        return await GetSettingsAsync(ct);
    }
}
