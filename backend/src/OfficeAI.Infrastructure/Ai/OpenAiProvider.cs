using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using OfficeAI.Application.Agents;

namespace OfficeAI.Infrastructure.Ai;

public class OpenAiOptions
{
    /// <summary>API key. Falls back to the OPENAI_API_KEY environment variable. Never stored in the database.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Any OpenAI-compatible Chat Completions endpoint.</summary>
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public int TimeoutSeconds { get; set; } = 300;
}

/// <summary>
/// OpenAI (or a compatible endpoint) via the Chat Completions API over plain HTTP. Disabled until an API key is configured.
/// Models and prices are added by the CEO in AI settings.
/// </summary>
public sealed class OpenAiProvider(IHttpClientFactory http, IOptions<OpenAiOptions> options, IAiCredentials? credentials = null) : IAiProvider
{
    public const string ProviderName = "openai";
    public const string HttpClientName = "ai-openai";

    private readonly string? _envKey = Key(options.Value.ApiKey, "OPENAI_API_KEY");
    private string? CurrentKey => credentials?.GetKey(ProviderName) ?? _envKey;

    /// <summary>Server configuration, then the environment variable. A key saved on the AI settings page takes precedence over both.</summary>
    internal static string? Key(string? configured, string envVar) =>
        !string.IsNullOrWhiteSpace(configured) ? configured : Environment.GetEnvironmentVariable(envVar) is { Length: > 0 } env ? env : null;

    public string Name => ProviderName;
    public bool IsSimulated => false;
    public bool IsAvailable => CurrentKey is not null;

    public async Task<string?> VerifyAsync(CancellationToken ct)
    {
        if (CurrentKey is not { } key) return "No API key.";
        using var message = new HttpRequestMessage(HttpMethod.Get, options.Value.BaseUrl.TrimEnd('/') + "/models");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return await HttpAi.CheckAsync(http, HttpClientName, message, "OpenAI", ct);
    }

    public async Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct)
    {
        if (CurrentKey is not { } _key) throw new AiProviderException("OpenAI API key is not configured.", retryable: false);

        var body = BuildRequest(request);
        using var message = new HttpRequestMessage(HttpMethod.Post, options.Value.BaseUrl.TrimEnd('/') + "/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
        var json = await HttpAi.SendAsync(http, HttpClientName, message, TimeSpan.FromSeconds(options.Value.TimeoutSeconds), "OpenAI", ct);
        return ParseResponse(json);
    }

    internal static JsonObject BuildRequest(AiRequest request)
    {
        var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt } };
        foreach (var m in request.Messages)
        {
            if (m.Role == AiRole.Assistant)
            {
                var text = string.Concat(m.Content.OfType<AiText>().Select(t => t.Text));
                var calls = m.Content.OfType<AiToolCall>().Select(c => (JsonNode)new JsonObject
                {
                    ["id"] = c.Id, ["type"] = "function",
                    ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.Input.GetRawText() },
                }).ToArray();
                var msg = new JsonObject { ["role"] = "assistant", ["content"] = text.Length > 0 ? text : null };
                if (calls.Length > 0) msg["tool_calls"] = new JsonArray(calls);
                messages.Add(msg);
                continue;
            }
            // Tool results are separate "tool" messages; any user text follows them.
            foreach (var r in m.Content.OfType<AiToolResult>())
                messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = r.ToolCallId, ["content"] = r.IsError ? $"ERROR: {r.Content}" : r.Content });
            var userText = string.Join("\n\n", m.Content.OfType<AiText>().Select(t => t.Text));
            if (userText.Length > 0) messages.Add(new JsonObject { ["role"] = "user", ["content"] = userText });
        }

        var body = new JsonObject
        {
            ["model"] = request.ModelId,
            ["messages"] = messages,
            ["max_completion_tokens"] = request.MaxTokens,
        };
        if (request.Tools.Count > 0)
            body["tools"] = new JsonArray(request.Tools.Select(t => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = t.Name, ["description"] = t.Description, ["parameters"] = JsonNode.Parse(t.InputSchema.GetRawText()),
                },
            }).ToArray());
        return body;
    }

    internal static AiResponse ParseResponse(JsonElement json)
    {
        var choice = json.GetProperty("choices")[0];
        var message = choice.GetProperty("message");
        var content = new List<AiContent>();
        if (message.TryGetProperty("content", out var text) && text.ValueKind == JsonValueKind.String && text.GetString() is { Length: > 0 } t)
            content.Add(new AiText(t));
        if (message.TryGetProperty("refusal", out var refusal) && refusal.ValueKind == JsonValueKind.String)
            content.Add(new AiText(refusal.GetString()!));
        if (message.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array)
            foreach (var call in calls.EnumerateArray())
            {
                var fn = call.GetProperty("function");
                content.Add(new AiToolCall(call.GetProperty("id").GetString()!, fn.GetProperty("name").GetString()!, HttpAi.ParseArguments(fn.GetProperty("arguments").GetString())));
            }

        var stop = choice.TryGetProperty("finish_reason", out var f) ? f.GetString() : null;
        var reason = stop switch
        {
            "stop" => AiStopReason.EndTurn,
            "tool_calls" or "function_call" => AiStopReason.ToolUse,
            "length" => AiStopReason.MaxTokens,
            "content_filter" => AiStopReason.Refusal,
            _ => content.OfType<AiToolCall>().Any() ? AiStopReason.ToolUse : AiStopReason.Other,
        };
        if (message.TryGetProperty("refusal", out var r2) && r2.ValueKind == JsonValueKind.String) reason = AiStopReason.Refusal;

        // prompt_tokens includes cached tokens; split them so each is priced at its own rate.
        long prompt = 0, completion = 0, cached = 0;
        if (json.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
        {
            prompt = u.TryGetProperty("prompt_tokens", out var p) ? p.GetInt64() : 0;
            completion = u.TryGetProperty("completion_tokens", out var c) ? c.GetInt64() : 0;
            if (u.TryGetProperty("prompt_tokens_details", out var d) && d.ValueKind == JsonValueKind.Object && d.TryGetProperty("cached_tokens", out var ctk))
                cached = ctk.GetInt64();
        }
        return new AiResponse(content, reason, new AiUsage(prompt - cached, completion, cached, 0), stop);
    }
}

/// <summary>Shared HTTP plumbing for the HTTP-based adapters: timeouts, error mapping and argument parsing.</summary>
internal static class HttpAi
{
    public static async Task<JsonElement> SendAsync(IHttpClientFactory http, string clientName, HttpRequestMessage message, TimeSpan timeout,
        string providerLabel, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        HttpResponseMessage response;
        try
        {
            response = await http.CreateClient(clientName).SendAsync(message, timeoutCts.Token);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new AiProviderException($"{providerLabel} did not answer in time.", retryable: true, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new AiProviderException($"Could not reach the {providerLabel} API.", retryable: true, ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode) return JsonDocument.Parse(text).RootElement.Clone();
            var detail = ErrorMessage(text) ?? response.ReasonPhrase;
            var status = (int)response.StatusCode;
            if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new AiProviderException($"{providerLabel} rate limit reached.", retryable: true);
            if (status >= 500) throw new AiProviderException($"{providerLabel} service error ({status}).", retryable: true);
            throw new AiProviderException($"{providerLabel} rejected the request ({status}): {detail}", retryable: false);
        }
    }

    /// <summary>A free "list models" call used to check a key: null when it works, otherwise a short explanation.</summary>
    public static async Task<string?> CheckAsync(IHttpClientFactory http, string clientName, HttpRequestMessage message, string providerLabel, CancellationToken ct)
    {
        try
        {
            await SendAsync(http, clientName, message, TimeSpan.FromSeconds(15), providerLabel, ct);
            return null;
        }
        catch (AiProviderException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Both OpenAI and Gemini report errors as {"error": {"message": ...}}.</summary>
    private static string? ErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object && e.TryGetProperty("message", out var m) ? m.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Tool arguments arrive as a JSON string; malformed JSON becomes an empty object (the tool validator then reports it).</summary>
    public static JsonElement ParseArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)) return JsonSerializer.SerializeToElement(new { });
        try
        {
            using var doc = JsonDocument.Parse(arguments);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(new { });
        }
    }
}
