using OfficeAI.Domain.Common;

namespace OfficeAI.Domain.Agents;

public enum ModelTier
{
    Routine = 1,
    Standard = 2,
    Advanced = 3,
}

/// <summary>A model offered by a provider, with the prices used for deterministic cost calculation (USD per million tokens).</summary>
public class AiModel : Entity, IAuditable
{
    public required string Provider { get; set; }
    public required string ModelId { get; set; }
    public required string DisplayName { get; set; }
    public ModelTier Tier { get; set; }
    public decimal InputUsdPerMTok { get; set; }
    public decimal OutputUsdPerMTok { get; set; }
    public decimal CacheReadUsdPerMTok { get; set; }
    public decimal CacheWriteUsdPerMTok { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public enum Effort
{
    Low = 1,
    Medium = 2,
    High = 3,
    XHigh = 4,
    Max = 5,
}

/// <summary>How one AI employee runs: which model, and the hard limits for each run.</summary>
public class AgentConfiguration : Entity, IAuditable
{
    public Guid EmployeeId { get; set; }
    public required string Provider { get; set; }
    public required string ModelId { get; set; }
    public Effort Effort { get; set; } = Effort.Medium;
    public int MaxTokensPerCall { get; set; } = 8_000;
    public int MaxTurnsPerRun { get; set; } = 12;
    public int MaxToolCallsPerRun { get; set; } = 20;
    public int MaxTokensPerRun { get; set; } = 200_000;
    public int TimeoutSeconds { get; set; } = 600;
    public decimal MonthlyBudgetUsd { get; set; } = 5m;
    public bool IsEnabled { get; set; } = true;
}

/// <summary>A tool an AI employee may call. Only humans with agent.manage can grant these.</summary>
public class AgentToolPermission : IAuditable
{
    public Guid EmployeeId { get; set; }
    public required string ToolId { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
    public Guid? GrantedBy { get; set; }
}

public enum AgentRunStatus
{
    Queued = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
    LimitReached = 6,
}

public class AgentRun : Entity
{
    public Guid TaskId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid EmployeeId { get; set; }
    public AgentRunStatus Status { get; set; } = AgentRunStatus.Queued;
    public required string Provider { get; set; }
    public required string ModelId { get; set; }

    /// <summary>Produced by the simulated provider: never counts as real work.</summary>
    public bool IsSimulated { get; set; }
    public Guid? TriggeredBy { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public int Turns { get; set; }
    public int ToolCalls { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
    public string? StopReason { get; set; }
    public string? Error { get; set; }
    public bool CancelRequested { get; set; }

    public bool IsActive => Status is AgentRunStatus.Queued or AgentRunStatus.Running;
}

public enum RunStepKind
{
    Instructions = 1,
    Input = 2,
    AssistantText = 3,
    ToolCall = 4,
    ToolResult = 5,
    Notice = 6,
}

/// <summary>One entry of a run's transcript, in order.</summary>
public class AgentRunStep
{
    public long Id { get; set; }
    public Guid RunId { get; set; }
    public int Sequence { get; set; }
    public RunStepKind Kind { get; set; }
    public required string Content { get; set; }
    public string? ToolCallId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public enum ToolExecutionStatus
{
    Succeeded = 1,
    Failed = 2,
    Denied = 3,
    TimedOut = 4,
    InvalidInput = 5,
}

public class ToolExecution
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid RunId { get; set; }
    public required string ToolId { get; set; }
    public required string ToolVersion { get; set; }
    public required string ToolCallId { get; set; }

    /// <summary>Unique per run + call; prevents a retried step from executing a side effect twice.</summary>
    public required string IdempotencyKey { get; set; }
    public required string InputJson { get; set; }
    public string? Output { get; set; }
    public ToolExecutionStatus Status { get; set; }
    public string? Error { get; set; }
    public int DurationMs { get; set; }
    public DateTimeOffset StartedAt { get; set; }
}

public class UsageRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid RunId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid ProjectId { get; set; }
    public required string Provider { get; set; }
    public required string ModelId { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheWriteTokens { get; set; }
    public bool IsSimulated { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

public enum CostKind
{
    Estimate = 1,
    Actual = 2,
}

public class CostRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public CostKind Kind { get; set; }
    public required string Category { get; set; }
    public decimal AmountUsd { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? RunId { get; set; }
    public Guid? UsageRecordId { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

public enum DeliverableStatus
{
    Submitted = 1,
    Accepted = 2,
    Rejected = 3,
}

/// <summary>Work product of a task: what an employee actually handed in.</summary>
public class Deliverable : Entity, IAuditable
{
    public Guid TaskId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? RunId { get; set; }
    public required string Title { get; set; }
    public string Format { get; set; } = "markdown";
    public required string Content { get; set; }
    public DeliverableStatus Status { get; set; } = DeliverableStatus.Submitted;
    public bool IsSimulated { get; set; }
}

/// <summary>Deterministic cost from token counts and per-million-token prices.</summary>
public static class Pricing
{
    public static decimal CostUsd(AiModel price, long input, long output, long cacheRead, long cacheWrite) =>
        Math.Round(
            (input * price.InputUsdPerMTok + output * price.OutputUsdPerMTok +
             cacheRead * price.CacheReadUsdPerMTok + cacheWrite * price.CacheWriteUsdPerMTok) / 1_000_000m,
            6, MidpointRounding.AwayFromZero);
}
