using System.Collections.Concurrent;
using OfficeAI.Application.Communication;

namespace OfficeAI.Tests;

/// <summary>Stands in for the host's Claude Code bridge: echoes the prompt (or answers with <see cref="Script"/>) and remembers what it was asked.</summary>
public sealed class FakeClaudeBridge : IClaudeBridge
{
    public bool IsConfigured => true;
    public ConcurrentQueue<(string Prompt, string System, string? SessionId)> Calls { get; } = new();
    /// <summary>When set, the reply to each prompt; reset it after the test.</summary>
    public Func<string, string>? Script { get; set; }

    public Task<ClaudeBridgeReply> AskAsync(string prompt, string system, string? sessionId, CancellationToken ct)
    {
        Calls.Enqueue((prompt, system, sessionId));
        return Task.FromResult(new ClaudeBridgeReply(Script?.Invoke(prompt) ?? $"echo: {prompt}", sessionId ?? Guid.NewGuid().ToString(), 0.01m));
    }
}
