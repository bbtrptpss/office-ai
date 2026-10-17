using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace OfficeAI.Application;

/// <summary>Domain traces and metrics ("OfficeAI"). Exported by OpenTelemetry when an OTLP endpoint is configured.</summary>
public static class Telemetry
{
    public const string Name = "OfficeAI";
    public static readonly ActivitySource Source = new(Name);
    public static readonly Meter Meter = new(Name);

    public static readonly Counter<long> RunsFinished = Meter.CreateCounter<long>("officeai.agent_runs.finished", description: "AI runs that ended, by status");
    public static readonly Counter<long> ToolExecutions = Meter.CreateCounter<long>("officeai.tool_executions", description: "Tool calls, by tool and status");
    public static readonly Counter<double> AiCostUsd = Meter.CreateCounter<double>("officeai.ai_cost", unit: "USD", description: "Actual AI model cost");
    public static readonly Counter<long> AiTokens = Meter.CreateCounter<long>("officeai.ai_tokens", description: "Tokens used, by direction");
    public static readonly Counter<long> EngineTicks = Meter.CreateCounter<long>("officeai.engine_ticks", description: "Workflow engine ticks, by outcome");
    public static readonly Counter<long> ApprovalsDecided = Meter.CreateCounter<long>("officeai.approvals.decided", description: "Approval decisions, by kind and outcome");
}
