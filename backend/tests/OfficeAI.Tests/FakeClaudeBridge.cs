using System.Collections.Concurrent;
using OfficeAI.Application.Communication;

namespace OfficeAI.Tests;

/// <summary>Stands in for the host's Claude Code bridge: echoes the prompt and remembers what it was asked.</summary>
public sealed class FakeClaudeBridge : IClaudeBridge
{
    public bool IsConfigured => true;
    public ConcurrentQueue<(string Prompt, string System, string? SessionId)> Calls { get; } = new();

    public Task<ClaudeBridgeReply> AskAsync(string prompt, string system, string? sessionId, CancellationToken ct)
    {
        Calls.Enqueue((prompt, system, sessionId));
        return Task.FromResult(new ClaudeBridgeReply($"echo: {prompt}", sessionId ?? Guid.NewGuid().ToString(), 0.01m));
    }
}
