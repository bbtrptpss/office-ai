using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Organization;

namespace OfficeAI.Application.Agents;

public static class AgentDefaults
{
    public const string AnthropicProvider = "anthropic";
    public const string SimulatedProvider = "simulated";
    public const string SimulatedModel = "simulated-v1";

    /// <summary>Anthropic list prices (USD per million tokens), cached 2026-10-06. Editable in the app; never assumed free.</summary>
    public static IEnumerable<AiModel> Models() =>
    [
        new() { Provider = AnthropicProvider, ModelId = "claude-opus-5-5", DisplayName = "Claude Opus 5.5", Tier = ModelTier.Advanced, InputUsdPerMTok = 4m, OutputUsdPerMTok = 20m, CacheReadUsdPerMTok = 0.20m, CacheWriteUsdPerMTok = 5m },
        new() { Provider = AnthropicProvider, ModelId = "claude-sonnet-5-5", DisplayName = "Claude Sonnet 5.5", Tier = ModelTier.Standard, InputUsdPerMTok = 2m, OutputUsdPerMTok = 10m, CacheReadUsdPerMTok = 0.20m, CacheWriteUsdPerMTok = 2.50m },
        new() { Provider = AnthropicProvider, ModelId = "claude-haiku-5-5", DisplayName = "Claude Haiku 5.5", Tier = ModelTier.Routine, InputUsdPerMTok = 0.10m, OutputUsdPerMTok = 0.50m, CacheReadUsdPerMTok = 0.01m, CacheWriteUsdPerMTok = 0.125m },
        new() { Provider = SimulatedProvider, ModelId = SimulatedModel, DisplayName = "Simulated (development only)", Tier = ModelTier.Routine },
    ];

    public static string ModelFor(OrgLevel level) => level switch
    {
        OrgLevel.Staff => "claude-sonnet-5-5",
        _ => "claude-opus-5-5",
    };

    public static AgentConfiguration ConfigFor(EmployeeProfile employee, OrgLevel level) => new()
    {
        EmployeeId = employee.Id,
        Provider = AnthropicProvider,
        ModelId = ModelFor(level),
    };

    /// <summary>Default tools by position level: everyone works and reviews; heads and executives plan; heads and seniors may delegate.</summary>
    public static IEnumerable<string> ToolsFor(OrgLevel level, string? roleCode = null)
    {
        foreach (var id in ToolRegistry.DefaultGrants) yield return id;
        yield return Delivery.ReadRequirementsTool.ToolId;
        yield return Knowledge.SearchKnowledgeTool.ToolId;
        foreach (var id in CommsTools) yield return id;
        if (Writers.Contains(roleCode ?? "") || level is OrgLevel.Executive or OrgLevel.Head) yield return Communication.DraftMinutesTool.ToolId;
        if (level is OrgLevel.Executive) yield return Communication.DraftAnnouncementTool.ToolId;
        if (level is OrgLevel.Executive or OrgLevel.Head or OrgLevel.Senior || Writers.Contains(roleCode ?? "")) yield return Knowledge.ProposeKnowledgeTool.ToolId;
        if (Writers.Contains(roleCode ?? "")) yield return Knowledge.WriteDocumentTool.ToolId;
        if (level is OrgLevel.Executive or OrgLevel.Head) yield return Workflows.PlanSubmitTool.ToolId;
        if (level is OrgLevel.Head or OrgLevel.Senior) yield return Workflows.DelegateTaskTool.ToolId;
        foreach (var id in RoleTools.GetValueOrDefault(roleCode ?? "", [])) yield return id;
        if (Engineers.Contains(roleCode ?? "")) foreach (var id in CodeWriteTools) yield return id;
        if (Engineers.Contains(roleCode ?? "") || Quality.Contains(roleCode ?? "")) foreach (var id in CodeReadTools) yield return id;
        if (Quality.Contains(roleCode ?? "")) yield return Delivery.ReportDefectTool.ToolId;
    }

    /// <summary>Specialist tools by position code.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> RoleTools = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["BA"] = [Delivery.AddRequirementTool.ToolId],
        ["BREQ"] = [Delivery.AddRequirementTool.ToolId],
        ["SOLCON"] = [Delivery.AddRequirementTool.ToolId, Delivery.SubmitEstimateTool.ToolId],
        ["PROPOSAL"] = [Delivery.SubmitEstimateTool.ToolId],
        ["CFO"] = [Delivery.SubmitEstimateTool.ToolId],
        ["PM"] = [Delivery.AddRequirementTool.ToolId],
        ["CTO"] = [Delivery.ProposeDecisionTool.ToolId],
        ["ARCH"] = [Delivery.ProposeDecisionTool.ToolId],
        ["RELEASE"] = [Delivery.PrepareReleaseTool.ToolId],
        ["DEVOPS"] = [Delivery.PrepareReleaseTool.ToolId],
        // Corporate Operations has no head position; its operations lead plans the department's objectives.
        ["OPERATIONS"] = [Workflows.PlanSubmitTool.ToolId],
    };

    private static readonly HashSet<string> Engineers = new(StringComparer.OrdinalIgnoreCase) { "ARCH", "BACKEND", "FRONTEND", "MOBILE", "DBENG", "DEVOPS", "INTEG" };
    private static readonly HashSet<string> Writers = new(StringComparer.OrdinalIgnoreCase) { "DOCS", "PM", "PROPOSAL", "SOLCON", "RELEASE", "BA", "BREQ", "ACCMGR" };
    private static readonly HashSet<string> Quality = new(StringComparer.OrdinalIgnoreCase) { "QAENG", "AUTOTEST", "REVIEWER", "SECENG", "RELEASE" };

    /// <summary>Every AI employee can follow and post in its project's channel and see the project's meetings.</summary>
    public static readonly string[] CommsTools =
        [Communication.ReadChatTool.ToolId, Communication.PostChatTool.ToolId, Communication.ListMeetingsTool.ToolId];

    public static readonly string[] CodeReadTools =
        [Code.ListFilesTool.ToolId, Code.ReadFileTool.ToolId, Code.SearchCodeTool.ToolId, Code.DiffTool.ToolId, Code.RunTestsTool.ToolId];
    public static readonly string[] CodeWriteTools =
        [Code.WriteFileTool.ToolId, Code.DeleteFileTool.ToolId, Code.CommitTool.ToolId, Code.OpenMergeRequestTool.ToolId];

    /// <summary>Starting hourly rates (THB) by level; the CEO edits the real rate card.</summary>
    public static decimal DefaultRateThb(OrgLevel level) => level switch
    {
        OrgLevel.Executive => 3000m,
        OrgLevel.Head => 2000m,
        OrgLevel.Senior => 1500m,
        _ => 1000m,
    };

    public static IEnumerable<AgentToolPermission> GrantsFor(Guid employeeId, OrgLevel level, DateTimeOffset now, string? roleCode = null) =>
        ToolsFor(level, roleCode).Distinct().Select(id => new AgentToolPermission { EmployeeId = employeeId, ToolId = id, GrantedAt = now });
}
