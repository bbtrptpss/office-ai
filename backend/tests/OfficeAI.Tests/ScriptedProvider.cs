using System.Collections.Concurrent;
using System.Text.Json;
using OfficeAI.Application.Agents;

namespace OfficeAI.Tests;

/// <summary>Test-only provider: each test registers its own behaviour under a unique model id.</summary>
public sealed class ScriptedProvider : IAiProvider
{
    public const string ProviderName = "scripted";
    private readonly ConcurrentDictionary<string, Func<AiRequest, CancellationToken, Task<AiResponse>>> _scripts = new();

    public string Name => ProviderName;
    public bool IsSimulated => false;
    public bool IsAvailable => true;

    public void Register(string modelId, Func<AiRequest, CancellationToken, Task<AiResponse>> script) => _scripts[modelId] = script;

    public Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct) =>
        _scripts.TryGetValue(request.ModelId, out var script)
            ? script(request, ct)
            : throw new AiProviderException($"No script for {request.ModelId}", retryable: false);

    // ---- helpers to build responses ----

    public static AiResponse Call(string tool, object input, long inTok = 100, long outTok = 20) =>
        new([new AiToolCall($"call_{Guid.NewGuid():N}", tool, JsonSerializer.SerializeToElement(input))], AiStopReason.ToolUse, new AiUsage(inTok, outTok, 0, 0));

    public static AiResponse Text(string text, long inTok = 100, long outTok = 20) =>
        new([new AiText(text)], AiStopReason.EndTurn, new AiUsage(inTok, outTok, 0, 0));

    /// <summary>Returns the given responses one per turn, then ends the turn.</summary>
    public static Func<AiRequest, CancellationToken, Task<AiResponse>> Sequence(params AiResponse[] responses)
    {
        var i = 0;
        return (_, _) => Task.FromResult(i < responses.Length ? responses[i++] : Text("done"));
    }
}

/// <summary>A tool that requires human approval; the runner must never execute it directly.</summary>
public sealed class ApprovalRequiredTool : IAgentTool
{
    public const string ToolId = "test.needs_approval";
    public static int Executions;

    public ToolDescriptor Descriptor { get; } = new(ToolId, "1.0", "Test tool that needs approval",
        JsonSerializer.SerializeToElement(new { type = "object", properties = new { }, additionalProperties = false }),
        TimeSpan.FromSeconds(5), 1000, RequiresApproval: true, HasSideEffects: true);

    public Task<ToolResult> ExecuteAsync(ToolContext ctx, JsonElement input, CancellationToken ct)
    {
        Interlocked.Increment(ref Executions);
        return Task.FromResult(ToolResult.Ok(new { executed = true }));
    }
}
