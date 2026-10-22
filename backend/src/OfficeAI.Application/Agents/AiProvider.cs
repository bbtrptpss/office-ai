using System.Text.Json;
using OfficeAI.Domain.Agents;

namespace OfficeAI.Application.Agents;

// Provider-neutral request/response shapes. Business logic only ever sees these;
// each adapter translates them to its own SDK.

public enum AiRole
{
    User = 1,
    Assistant = 2,
}

public abstract record AiContent;
public sealed record AiText(string Text) : AiContent;
public sealed record AiToolCall(string Id, string Name, JsonElement Input) : AiContent;
public sealed record AiToolResult(string ToolCallId, string Content, bool IsError) : AiContent;

/// <summary>Opaque provider-specific block (e.g. a thinking block) that must be echoed back unchanged.</summary>
public sealed record AiOpaque(string Provider, JsonElement Payload) : AiContent;

public sealed record AiMessage(AiRole Role, IReadOnlyList<AiContent> Content);

public sealed record AiToolDefinition(string Name, string Description, JsonElement InputSchema);

public sealed record AiRequest(
    string ModelId,
    string SystemPrompt,
    IReadOnlyList<AiMessage> Messages,
    IReadOnlyList<AiToolDefinition> Tools,
    int MaxTokens,
    Effort Effort);

public enum AiStopReason
{
    EndTurn = 1,
    ToolUse = 2,
    MaxTokens = 3,
    Refusal = 4,
    Other = 5,
}

public sealed record AiUsage(long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens);

public sealed record AiResponse(IReadOnlyList<AiContent> Content, AiStopReason StopReason, AiUsage Usage, string? RawStopReason = null);

public interface IAiProvider
{
    /// <summary>Stable key used in configuration and records, e.g. "anthropic".</summary>
    string Name { get; }

    /// <summary>True only for the deterministic development stand-in.</summary>
    bool IsSimulated { get; }

    /// <summary>Configured and allowed to run (e.g. an API key is present).</summary>
    bool IsAvailable { get; }

    /// <summary>Not billed per token (our own hardware such as Ollama, or a flat-rate plan such as Claude Code): its models may be listed with zero prices.</summary>
    bool IsFlatRate => false;

    Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct);

    /// <summary>Checks the configured key with a free call (no tokens). Returns null when it works, otherwise the problem.</summary>
    Task<string?> VerifyAsync(CancellationToken ct) => Task.FromResult<string?>(IsAvailable ? null : "Not configured.");

    /// <summary>Model ids the configured key can use for chat/generation (a free listing call). Empty when not supported.</summary>
    Task<IReadOnlyList<string>> RemoteModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
}

/// <summary>Provider keys entered by people on the AI settings page (encrypted at rest). Take precedence over the server environment.</summary>
public interface IAiCredentials
{
    string? GetKey(string provider);
    string? Last4(string provider);

    /// <summary>Stores (or with null removes) a key. Takes effect without a restart.</summary>
    Task SetAsync(string provider, string? key, CancellationToken ct);
}

/// <summary>Thrown by adapters for provider failures; <see cref="Retryable"/> marks rate limits, 5xx and network errors.</summary>
public sealed class AiProviderException(string message, bool retryable, Exception? inner = null, bool rateLimited = false, TimeSpan? retryAfter = null)
    : Exception(message, inner)
{
    public bool Retryable { get; } = retryable;

    /// <summary>The provider's quota was exceeded; waiting usually fixes it.</summary>
    public bool RateLimited { get; } = rateLimited;

    /// <summary>How long the provider asked us to wait, when it said.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>Shared retry policy: quick retries for transient errors, patient ones for rate limits (per-minute quotas).</summary>
public static class AiRetry
{
    public const int MaxTransientRetries = 2;
    public const int MaxRateLimitRetries = 5;
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(60);

    /// <summary>A quota that resets daily (or a wait longer than we are willing to sit out): retrying now cannot help.</summary>
    public static bool IsLongTermQuota(AiProviderException ex) =>
        ex.RateLimited && (ex.RetryAfter > MaxWait
            || ex.Message.Contains("per day", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("PerDay", StringComparison.Ordinal)
            || ex.Message.Contains("daily", StringComparison.OrdinalIgnoreCase));

    /// <summary>Null when the call should not be retried again; otherwise how long to wait first.</summary>
    public static TimeSpan? Delay(AiProviderException ex, int attempt)
    {
        if (!ex.Retryable) return null;
        if (ex.RateLimited)
        {
            if (attempt >= MaxRateLimitRetries || IsLongTermQuota(ex)) return null;
            var wait = ex.RetryAfter ?? TimeSpan.FromSeconds(10 * Math.Pow(2, attempt));
            return wait > MaxWait ? MaxWait : wait < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : wait;
        }
        return attempt < MaxTransientRetries ? TimeSpan.FromSeconds(2 * (attempt + 1)) : null;
    }
}

public sealed class AiProviderRegistry(IEnumerable<IAiProvider> providers)
{
    private readonly Dictionary<string, IAiProvider> _byName = providers.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<IAiProvider> All => _byName.Values;

    public IAiProvider? Find(string name) => _byName.GetValueOrDefault(name);
}
