using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Agents;
using OfficeAI.Application.Workflows;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Delivery;
using OfficeAI.Domain.Workflows;

namespace OfficeAI.Application.Delivery;

internal static class Schemas
{
    public static JsonElement Of(object schema) => JsonSerializer.SerializeToElement(schema);
    public static string? Str(JsonElement o, string name) => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>Read the project's requirements and architecture decisions (all statuses).</summary>
public sealed class ReadRequirementsTool : IAgentTool
{
    public const string ToolId = "project.read_requirements";

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0",
        "Read the project's requirements (with codes, priority and status) and its architecture decisions.",
        Schemas.Of(new { type = "object", properties = new { }, additionalProperties = false }),
        TimeSpan.FromSeconds(10), 40_000, RequiresApproval: false, HasSideEffects: false, Purposes: [RunPurpose.Work, RunPurpose.Review, RunPurpose.Plan]);

    public async Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct)
    {
        var reqs = await ctx.Db.Requirements.AsNoTracking().Where(r => r.ProjectId == ctx.ProjectId).OrderBy(r => r.Code)
            .Select(r => new { r.Code, Kind = r.Kind.ToString(), r.Title, r.Description, r.AcceptanceCriteria, Priority = r.Priority.ToString(), Status = r.Status.ToString() })
            .ToListAsync(ct);
        var adrs = await ctx.Db.ArchitectureDecisions.AsNoTracking().Where(a => a.ProjectId == ctx.ProjectId).OrderBy(a => a.Number)
            .Select(a => new { a.Number, a.Title, a.Context, a.Decision, a.Consequences, Status = a.Status.ToString() })
            .ToListAsync(ct);
        return ToolResult.Ok(new { requirements = reqs, architectureDecisions = adrs });
    }
}

public sealed class AddRequirementTool : IAgentTool
{
    public const string ToolId = "requirements.add";

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0",
        "Record one requirement for the project (functional, non-functional, constraint, assumption or risk) with a MoSCoW priority. " +
        "It starts as Draft until a person approves it.",
        Schemas.Of(new
        {
            type = "object",
            properties = new
            {
                kind = new { type = "string", @enum = new[] { "Functional", "NonFunctional", "Constraint", "Assumption", "Risk" } },
                title = new { type = "string", maxLength = 200 },
                description = new { type = "string", maxLength = 4000 },
                acceptanceCriteria = new { type = "string", maxLength = 4000 },
                priority = new { type = "string", @enum = new[] { "Must", "Should", "Could", "Wont" } },
            },
            required = new[] { "kind", "title" },
            additionalProperties = false,
        }),
        TimeSpan.FromSeconds(10), 1_000, RequiresApproval: false, HasSideEffects: true);

    public async Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct)
    {
        if (!Enum.TryParse<RequirementKind>(Schemas.Str(input, "kind"), true, out var kind) || !Enum.IsDefined(kind))
            return ToolResult.Fail("kind must be Functional, NonFunctional, Constraint, Assumption or Risk.");
        var priority = Enum.TryParse<RequirementPriority>(Schemas.Str(input, "priority"), true, out var p) && Enum.IsDefined(p) ? p : RequirementPriority.Should;
        var title = Schemas.Str(input, "title")!.Trim();
        if (title.Length == 0) return ToolResult.Fail("title must not be empty.");
        if (await ctx.Db.Requirements.CountAsync(r => r.ProjectId == ctx.ProjectId, ct) >= 500) return ToolResult.Fail("A project can hold at most 500 requirements.");

        var code = await DeliveryService.NextRequirementCodeAsync(ctx.Db, ctx.ProjectId, ct);
        ctx.Db.Requirements.Add(new ProjectRequirement
        {
            ProjectId = ctx.ProjectId, Code = code, Kind = kind, Title = title, Description = Schemas.Str(input, "description"),
            AcceptanceCriteria = Schemas.Str(input, "acceptanceCriteria"), Priority = priority, AuthorEmployeeId = ctx.EmployeeId, RunId = ctx.RunId,
        });
        await ctx.Db.SaveChangesAsync(ct);
        return ToolResult.Ok(new { code, status = "Draft" });
    }
}

public sealed class ProposeDecisionTool : IAgentTool
{
    public const string ToolId = "adr.propose";

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0",
        "Propose an architecture decision record: the context, the decision and its consequences. A person accepts or rejects it.",
        Schemas.Of(new
        {
            type = "object",
            properties = new
            {
                title = new { type = "string", maxLength = 200 },
                context = new { type = "string", maxLength = 8000 },
                decision = new { type = "string", maxLength = 8000 },
                consequences = new { type = "string", maxLength = 8000 },
            },
            required = new[] { "title", "context", "decision" },
            additionalProperties = false,
        }),
        TimeSpan.FromSeconds(10), 1_000, RequiresApproval: false, HasSideEffects: true);

    public async Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct)
    {
        var number = (await ctx.Db.ArchitectureDecisions.Where(a => a.ProjectId == ctx.ProjectId).MaxAsync(a => (int?)a.Number, ct) ?? 0) + 1;
        ctx.Db.ArchitectureDecisions.Add(new ArchitectureDecision
        {
            ProjectId = ctx.ProjectId, Number = number, Title = Schemas.Str(input, "title")!.Trim(), Context = Schemas.Str(input, "context")!,
            Decision = Schemas.Str(input, "decision")!, Consequences = Schemas.Str(input, "consequences"), AuthorEmployeeId = ctx.EmployeeId, RunId = ctx.RunId,
        });
        await ctx.Db.SaveChangesAsync(ct);
        return ToolResult.Ok(new { adr = $"ADR-{number:000}", status = "Proposed" });
    }
}

/// <summary>Submit an effort estimate. The arithmetic is done by the system, never by the model.</summary>
public sealed class SubmitEstimateTool : IAgentTool
{
    public const string ToolId = "estimate.submit";

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0",
        "Submit an estimate as line items (description, role code such as BACKEND or QAENG, hours, evidence for the hours) plus assumptions " +
        "and the expected AI usage cost in USD. The system prices it from the company rate card; the CEO approves it.",
        Schemas.Of(new
        {
            type = "object",
            properties = new
            {
                assumptions = new { type = "string", maxLength = 8000 },
                aiCostUsd = new { type = "number" },
                contingencyPercent = new { type = "number" },
                items = new
                {
                    type = "array",
                    maxItems = 100,
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            description = new { type = "string" },
                            roleCode = new { type = "string" },
                            hours = new { type = "number" },
                            evidence = new { type = "string" },
                        },
                        required = new[] { "description", "roleCode", "hours" },
                    },
                },
            },
            required = new[] { "assumptions", "items" },
            additionalProperties = false,
        }),
        TimeSpan.FromSeconds(15), 4_000, RequiresApproval: false, HasSideEffects: true);

    public async Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct)
    {
        if (!input.TryGetProperty("items", out var itemsEl) || itemsEl.ValueKind != JsonValueKind.Array) return ToolResult.Fail("items must be an array.");
        var roles = await ctx.Db.RoleDefinitions.AsNoTracking().ToDictionaryAsync(r => r.Code, StringComparer.OrdinalIgnoreCase, ct);
        var rates = await ctx.Db.RoleRates.AsNoTracking().ToDictionaryAsync(r => r.RoleDefinitionId, r => r.HourlyRateThb, ct);
        var settings = await DeliveryService.SettingsAsync(ctx.Db, ct);

        var estimate = new ProjectEstimate
        {
            ProjectId = ctx.ProjectId, Assumptions = Schemas.Str(input, "assumptions"),
            AiCostUsd = input.TryGetProperty("aiCostUsd", out var ai) && ai.ValueKind == JsonValueKind.Number ? Math.Max(0, ai.GetDecimal()) : 0,
            ContingencyPercent = input.TryGetProperty("contingencyPercent", out var c) && c.ValueKind == JsonValueKind.Number
                ? Math.Clamp(c.GetDecimal(), 0, 100) : settings.DefaultContingencyPercent,
            TargetMarginPercent = settings.TargetMarginPercent, UsdToThb = settings.UsdToThb, AuthorEmployeeId = ctx.EmployeeId, RunId = ctx.RunId,
        };
        foreach (var item in itemsEl.EnumerateArray())
        {
            var code = Schemas.Str(item, "roleCode") ?? "";
            if (!roles.TryGetValue(code, out var role)) return ToolResult.Fail($"Unknown role code '{code}'. Use codes from the organization, e.g. BACKEND, FRONTEND, QAENG.");
            if (!rates.TryGetValue(role.Id, out var rate)) return ToolResult.Fail($"No hourly rate is configured for {role.Title}; ask a person to set the rate card.");
            if (!item.TryGetProperty("hours", out var h) || h.ValueKind != JsonValueKind.Number || h.GetDecimal() <= 0 || h.GetDecimal() > 2000)
                return ToolResult.Fail("hours must be a number between 0 and 2000.");
            estimate.Items.Add(new EstimateLineItem
            {
                EstimateId = estimate.Id, Description = Schemas.Str(item, "description") ?? "", RoleDefinitionId = role.Id, RoleTitle = role.Title,
                Hours = h.GetDecimal(), RateThb = rate, Evidence = Schemas.Str(item, "evidence"),
            });
        }
        EstimateCalculator.Compute(estimate);
        estimate.Revision = (await ctx.Db.Estimates.Where(e => e.ProjectId == ctx.ProjectId).MaxAsync(e => (int?)e.Revision, ct) ?? 0) + 1;
        ctx.Db.Estimates.Add(estimate);

        var project = await ctx.Db.Projects.AsNoTracking().FirstAsync(p => p.Id == ctx.ProjectId, ct);
        ctx.Db.ApprovalRequests.Add(Approvals.Create(ApprovalKind.Estimate, $"Approve estimate r{estimate.Revision}: {project.Name}",
            estimate.Warnings.Count > 0 ? "Warnings: " + string.Join(" ", estimate.Warnings) : "No warnings.",
            new
            {
                estimateId = estimate.Id, estimate.Revision, estimate.TotalHours, estimate.LaborCost, estimate.AiCost, estimate.Contingency, estimate.TotalCost,
                estimate.Price, estimate.GrossMargin, estimate.GrossMarginPercent, estimate.Currency, estimate.Warnings,
            },
            ctx.Clock.GetUtcNow(), projectId: ctx.ProjectId, taskId: ctx.TaskId, runId: ctx.RunId, requestedByEmployee: ctx.EmployeeId));
        await ctx.Db.SaveChangesAsync(ct);
        return ToolResult.Ok(new
        {
            revision = estimate.Revision, estimate.TotalHours, estimate.TotalCost, estimate.Price, estimate.GrossMarginPercent, estimate.Warnings,
            status = "Submitted for approval",
        });
    }
}
