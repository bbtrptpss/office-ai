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
    public static IEnumerable<string> ToolsFor(OrgLevel level)
    {
        foreach (var id in ToolRegistry.DefaultGrants) yield return id;
        if (level is OrgLevel.Executive or OrgLevel.Head) yield return Workflows.PlanSubmitTool.ToolId;
        if (level is OrgLevel.Head or OrgLevel.Senior) yield return Workflows.DelegateTaskTool.ToolId;
    }

    public static IEnumerable<AgentToolPermission> GrantsFor(Guid employeeId, OrgLevel level, DateTimeOffset now) =>
        ToolsFor(level).Select(id => new AgentToolPermission { EmployeeId = employeeId, ToolId = id, GrantedAt = now });
}
