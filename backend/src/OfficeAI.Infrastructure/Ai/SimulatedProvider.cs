using System.Text.Json;
using Microsoft.Extensions.Options;
using OfficeAI.Application.Agents;

namespace OfficeAI.Infrastructure.Ai;

public class SimulatedOptions
{
    /// <summary>Allow the deterministic stand-in provider. Intended for development and automated tests only.</summary>
    public bool Enabled { get; set; }
}

/// <summary>
/// Deterministic stand-in for a model, so the whole run pipeline can be exercised without an API key.
/// It follows a fixed script — read the task, hand in a clearly labelled placeholder deliverable,
/// request review — and every run it serves is marked IsSimulated.
/// </summary>
public sealed class SimulatedProvider(IOptions<SimulatedOptions> options) : IAiProvider
{
    public string Name => AgentDefaults.SimulatedProvider;
    public bool IsSimulated => true;
    public bool IsAvailable => options.Value.Enabled;

    public Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct)
    {
        if (!IsAvailable) throw new AiProviderException("The simulated provider is disabled.", retryable: false);

        var toolNames = request.Tools.Select(t => t.Name).ToHashSet();
        var used = request.Messages.SelectMany(m => m.Content).OfType<AiToolCall>().Select(c => c.Name).ToHashSet();
        var lastResults = request.Messages.LastOrDefault()?.Content.OfType<AiToolResult>().ToList() ?? [];
        var inputTokens = request.SystemPrompt.Length / 4 + request.Messages.Sum(m => m.Content.Sum(Size)) / 4;

        if (lastResults.Any(r => r.IsError))
            return Done($"[SIMULATED] Stopping: a tool reported an error ({lastResults.First(r => r.IsError).Content}).", inputTokens);

        string? Next() => new[] { "task_read_context", "deliverable_submit", "task_request_review" }
            .FirstOrDefault(n => toolNames.Contains(n) && !used.Contains(n));

        var next = Next();
        if (next is null) return Done("[SIMULATED] Finished. This run used the simulated provider; no real AI did this work.", inputTokens);

        var input = next switch
        {
            "deliverable_submit" => JsonSerializer.SerializeToElement(new
            {
                title = "[SIMULATED] Placeholder deliverable",
                content = "# Simulated output\n\nThis deliverable was produced by the development stand-in provider. It is **not** real work and must not be accepted.",
            }),
            "task_request_review" => JsonSerializer.SerializeToElement(new { note = "[SIMULATED] Ready for review." }),
            _ => JsonSerializer.SerializeToElement(new { }),
        };
        var callId = $"sim_{used.Count + 1}";
        return Task.FromResult(new AiResponse(
            [new AiText($"[SIMULATED] Calling {next}."), new AiToolCall(callId, next, input)],
            AiStopReason.ToolUse,
            new AiUsage(inputTokens, 40, 0, 0)));
    }

    private static Task<AiResponse> Done(string text, long inputTokens) =>
        Task.FromResult(new AiResponse([new AiText(text)], AiStopReason.EndTurn, new AiUsage(inputTokens, 30, 0, 0)));

    private static int Size(AiContent c) => c switch
    {
        AiText t => t.Text.Length,
        AiToolResult r => r.Content.Length,
        AiToolCall call => call.Input.GetRawText().Length,
        _ => 0,
    };
}
