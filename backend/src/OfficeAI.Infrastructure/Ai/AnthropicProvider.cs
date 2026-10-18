using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;
using OfficeAI.Application.Agents;
using AiEffort = OfficeAI.Domain.Agents.Effort;

namespace OfficeAI.Infrastructure.Ai;

public class AnthropicOptions
{
    /// <summary>API key. Falls back to the ANTHROPIC_API_KEY environment variable. Never stored in the database.</summary>
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 300;
}

/// <summary>
/// Claude via the official Anthropic SDK. Disabled until an API key is configured, either on the AI settings page
/// (takes precedence, no restart needed) or in the server environment.
/// </summary>
public sealed class AnthropicProvider(IOptions<AnthropicOptions> options, IAiCredentials? credentials = null, IHttpClientFactory? http = null) : IAiProvider
{
    public const string HttpClientName = "ai-anthropic-check";

    private readonly string? _envKey = OpenAiProvider.Key(options.Value.ApiKey, "ANTHROPIC_API_KEY");
    private readonly Lock _lock = new();
    private (string Key, AnthropicClient Client)? _current;

    private string? CurrentKey => credentials?.GetKey(Name) ?? _envKey;

    /// <summary>One client per key; a new key (saved in settings) gets a new client.</summary>
    private AnthropicClient? Client()
    {
        if (CurrentKey is not { } key) return null;
        lock (_lock)
        {
            if (_current is not { } c || c.Key != key)
                _current = (key, new AnthropicClient { ApiKey = key, Timeout = TimeSpan.FromSeconds(options.Value.TimeoutSeconds) });
            return _current.Value.Client;
        }
    }

    public string Name => AgentDefaults.AnthropicProvider;
    public bool IsSimulated => false;
    public bool IsAvailable => CurrentKey is not null;

    public async Task<string?> VerifyAsync(CancellationToken ct)
    {
        if (CurrentKey is not { } key) return "No API key.";
        if (http is null) return null;
        using var message = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/models?limit=1");
        message.Headers.Add("x-api-key", key);
        message.Headers.Add("anthropic-version", "2023-06-01");
        return await HttpAi.CheckAsync(http, HttpClientName, message, "Anthropic", ct);
    }

    public async Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct)
    {
        var _client = Client() ?? throw new AiProviderException("Anthropic API key is not configured.", retryable: false);

        var parameters = new MessageCreateParams
        {
            Model = request.ModelId,
            MaxTokens = request.MaxTokens,
            System = request.SystemPrompt,
            OutputConfig = new OutputConfig { Effort = MapEffort(request.Effort) },
            Messages = request.Messages.Select(ToParam).ToList(),
            Tools = request.Tools.Select(t => new ToolUnion(ToTool(t))).ToList(),
        };

        Message message;
        try
        {
            message = await _client.Messages.Create(parameters, ct);
        }
        catch (AnthropicRateLimitException ex)
        {
            throw new AiProviderException("Anthropic rate limit reached.", retryable: true, ex);
        }
        catch (Anthropic5xxException ex)
        {
            throw new AiProviderException("Anthropic service error.", retryable: true, ex);
        }
        catch (AnthropicIOException ex)
        {
            throw new AiProviderException("Could not reach the Anthropic API.", retryable: true, ex);
        }
        catch (AnthropicApiException ex)
        {
            throw new AiProviderException($"Anthropic rejected the request: {ex.Message}", retryable: false, ex);
        }

        var content = new List<AiContent>();
        foreach (var block in message.Content)
        {
            if (block.TryPickText(out TextBlock? text))
                content.Add(new AiText(text.Text));
            else if (block.TryPickToolUse(out ToolUseBlock? toolUse))
                content.Add(new AiToolCall(toolUse.ID, toolUse.Name, JsonSerializer.SerializeToElement(toolUse.Input)));
            else if (block.TryPickThinking(out ThinkingBlock? thinking))
                content.Add(new AiOpaque(Name, JsonSerializer.SerializeToElement(new { type = "thinking", thinking = thinking.Thinking, signature = thinking.Signature })));
            else if (block.TryPickRedactedThinking(out RedactedThinkingBlock? redacted))
                content.Add(new AiOpaque(Name, JsonSerializer.SerializeToElement(new { type = "redacted_thinking", data = redacted.Data })));
        }

        string? stop = message.StopReason?.ToString();
        var reason = stop switch
        {
            "end_turn" or "stop_sequence" => AiStopReason.EndTurn,
            "tool_use" => AiStopReason.ToolUse,
            "max_tokens" => AiStopReason.MaxTokens,
            "refusal" => AiStopReason.Refusal,
            _ => content.OfType<AiToolCall>().Any() ? AiStopReason.ToolUse : AiStopReason.Other,
        };
        var usage = new AiUsage(
            message.Usage.InputTokens,
            message.Usage.OutputTokens,
            message.Usage.CacheReadInputTokens ?? 0,
            message.Usage.CacheCreationInputTokens ?? 0);
        return new AiResponse(content, reason, usage, stop);
    }

    private static Anthropic.Models.Messages.Effort MapEffort(AiEffort effort) => effort switch
    {
        AiEffort.Low => Anthropic.Models.Messages.Effort.Low,
        AiEffort.Medium => Anthropic.Models.Messages.Effort.Medium,
        AiEffort.High => Anthropic.Models.Messages.Effort.High,
        AiEffort.XHigh => Anthropic.Models.Messages.Effort.Xhigh,
        _ => Anthropic.Models.Messages.Effort.Max,
    };

    private static MessageParam ToParam(AiMessage m)
    {
        var blocks = new List<ContentBlockParam>();
        foreach (var c in m.Content)
        {
            switch (c)
            {
                case AiText t:
                    blocks.Add(new TextBlockParam { Text = t.Text });
                    break;
                case AiToolCall call:
                    blocks.Add(new ToolUseBlockParam { ID = call.Id, Name = call.Name, Input = ToDictionary(call.Input) });
                    break;
                case AiToolResult r:
                    blocks.Add(new ToolResultBlockParam { ToolUseID = r.ToolCallId, Content = r.Content, IsError = r.IsError });
                    break;
                case AiOpaque { Provider: AgentDefaults.AnthropicProvider } o when o.Payload.GetProperty("type").GetString() == "thinking":
                    // Thinking blocks must be echoed back unchanged, signature included.
                    blocks.Add(new ThinkingBlockParam { Thinking = o.Payload.GetProperty("thinking").GetString()!, Signature = o.Payload.GetProperty("signature").GetString()! });
                    break;
                case AiOpaque { Provider: AgentDefaults.AnthropicProvider } o:
                    blocks.Add(new RedactedThinkingBlockParam { Data = o.Payload.GetProperty("data").GetString()! });
                    break;
            }
        }
        return new MessageParam { Role = m.Role == AiRole.User ? Role.User : Role.Assistant, Content = blocks };
    }

    private static Tool ToTool(AiToolDefinition t)
    {
        var properties = t.InputSchema.TryGetProperty("properties", out var p)
            ? p.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone())
            : new Dictionary<string, JsonElement>();
        var required = t.InputSchema.TryGetProperty("required", out var r)
            ? r.EnumerateArray().Select(x => x.GetString()!).ToList()
            : [];
        return new Tool { Name = t.Name, Description = t.Description, InputSchema = new() { Properties = properties, Required = required } };
    }

    private static Dictionary<string, JsonElement> ToDictionary(JsonElement input) =>
        input.ValueKind == JsonValueKind.Object
            ? input.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone())
            : [];
}
