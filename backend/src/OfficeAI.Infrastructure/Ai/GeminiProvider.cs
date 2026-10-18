using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using OfficeAI.Application.Agents;

namespace OfficeAI.Infrastructure.Ai;

public class GeminiOptions
{
    /// <summary>API key. Falls back to the GEMINI_API_KEY environment variable. Never stored in the database.</summary>
    public string? ApiKey { get; set; }
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
    public int TimeoutSeconds { get; set; } = 300;
}

/// <summary>
/// Google Gemini via the generateContent REST API. Disabled until an API key is configured.
/// Gemini function calls may carry a thought signature that must be sent back with the call; it travels as an opaque block.
/// </summary>
public sealed class GeminiProvider(IHttpClientFactory http, IOptions<GeminiOptions> options, IAiCredentials? credentials = null) : IAiProvider
{
    public const string ProviderName = "gemini";
    public const string HttpClientName = "ai-gemini";

    private readonly string? _envKey = OpenAiProvider.Key(options.Value.ApiKey, "GEMINI_API_KEY");
    private string? CurrentKey => credentials?.GetKey(ProviderName) ?? _envKey;

    public string Name => ProviderName;
    public bool IsSimulated => false;
    public bool IsAvailable => CurrentKey is not null;

    public async Task<string?> VerifyAsync(CancellationToken ct)
    {
        if (CurrentKey is not { } key) return "No API key.";
        using var message = new HttpRequestMessage(HttpMethod.Get, $"{options.Value.BaseUrl.TrimEnd('/')}/models?pageSize=1");
        message.Headers.Add("x-goog-api-key", key);
        return await HttpAi.CheckAsync(http, HttpClientName, message, "Gemini", ct);
    }

    /// <summary>Models that support generateContent, without the "models/" prefix.</summary>
    public async Task<IReadOnlyList<string>> RemoteModelsAsync(CancellationToken ct)
    {
        if (CurrentKey is not { } key) return [];
        var ids = new List<string>();
        string? page = null;
        for (var i = 0; i < 10; i++)
        {
            var url = $"{options.Value.BaseUrl.TrimEnd('/')}/models?pageSize=200" + (page is null ? "" : $"&pageToken={Uri.EscapeDataString(page)}");
            using var message = new HttpRequestMessage(HttpMethod.Get, url);
            message.Headers.Add("x-goog-api-key", key);
            var json = await HttpAi.SendAsync(http, HttpClientName, message, TimeSpan.FromSeconds(15), "Gemini", ct);
            if (json.TryGetProperty("models", out var models))
                foreach (var m in models.EnumerateArray())
                    if (m.TryGetProperty("supportedGenerationMethods", out var methods) && methods.EnumerateArray().Any(x => x.GetString() == "generateContent"))
                        ids.Add(m.GetProperty("name").GetString()!.Replace("models/", ""));
            page = json.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
            if (string.IsNullOrEmpty(page)) break;
        }
        return ids.Order().ToList();
    }

    public async Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct)
    {
        if (CurrentKey is not { } _key) throw new AiProviderException("Gemini API key is not configured.", retryable: false);
        var url = $"{options.Value.BaseUrl.TrimEnd('/')}/models/{Uri.EscapeDataString(request.ModelId)}:generateContent";
        using var message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(BuildRequest(request).ToJsonString(), Encoding.UTF8, "application/json"),
        };
        message.Headers.Add("x-goog-api-key", _key); // header, never the URL: keys must not end up in logs
        var json = await HttpAi.SendAsync(http, HttpClientName, message, TimeSpan.FromSeconds(options.Value.TimeoutSeconds), "Gemini", ct);
        return ParseResponse(json);
    }

    internal static JsonObject BuildRequest(AiRequest request)
    {
        // Gemini answers a function call by name, so remember which call id belongs to which tool.
        var callNames = request.Messages.SelectMany(m => m.Content).OfType<AiToolCall>().ToDictionary(c => c.Id, c => c.Name);
        var signatures = request.Messages.SelectMany(m => m.Content).OfType<AiOpaque>()
            .Where(o => o.Provider == ProviderName && o.Payload.TryGetProperty("callId", out _))
            .ToDictionary(o => o.Payload.GetProperty("callId").GetString()!, o => o.Payload.GetProperty("signature").GetString()!);

        var contents = new JsonArray();
        foreach (var m in request.Messages)
        {
            var parts = new JsonArray();
            foreach (var c in m.Content)
            {
                switch (c)
                {
                    case AiText t:
                        parts.Add(new JsonObject { ["text"] = t.Text });
                        break;
                    case AiToolCall call:
                        var part = new JsonObject { ["functionCall"] = new JsonObject { ["name"] = call.Name, ["args"] = JsonNode.Parse(call.Input.GetRawText()) } };
                        if (signatures.TryGetValue(call.Id, out var sig)) part["thoughtSignature"] = sig;
                        parts.Add(part);
                        break;
                    case AiToolResult r:
                        parts.Add(new JsonObject
                        {
                            ["functionResponse"] = new JsonObject
                            {
                                ["name"] = callNames.GetValueOrDefault(r.ToolCallId, "unknown"),
                                ["response"] = new JsonObject { [r.IsError ? "error" : "result"] = r.Content },
                            },
                        });
                        break;
                }
            }
            if (parts.Count > 0) contents.Add(new JsonObject { ["role"] = m.Role == AiRole.Assistant ? "model" : "user", ["parts"] = parts });
        }

        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = request.SystemPrompt }) },
            ["contents"] = contents,
            ["generationConfig"] = new JsonObject { ["maxOutputTokens"] = request.MaxTokens },
        };
        if (request.Tools.Count > 0)
            body["tools"] = new JsonArray(new JsonObject
            {
                ["functionDeclarations"] = new JsonArray(request.Tools.Select(t => (JsonNode)new JsonObject
                {
                    ["name"] = t.Name, ["description"] = t.Description, ["parametersJsonSchema"] = JsonNode.Parse(t.InputSchema.GetRawText()),
                }).ToArray()),
            });
        return body;
    }

    internal static AiResponse ParseResponse(JsonElement json)
    {
        var content = new List<AiContent>();
        string? stop = null;
        if (json.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0)
        {
            var candidate = candidates[0];
            stop = candidate.TryGetProperty("finishReason", out var f) ? f.GetString() : null;
            if (candidate.TryGetProperty("content", out var c) && c.TryGetProperty("parts", out var parts))
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True) continue; // thought summaries are not answers
                    if (part.TryGetProperty("text", out var text))
                        content.Add(new AiText(text.GetString() ?? ""));
                    else if (part.TryGetProperty("functionCall", out var call))
                    {
                        var id = call.TryGetProperty("id", out var cid) && cid.GetString() is { Length: > 0 } given ? given : $"gemini-{Guid.NewGuid():N}";
                        var args = call.TryGetProperty("args", out var a) ? a.Clone() : JsonSerializer.SerializeToElement(new { });
                        content.Add(new AiToolCall(id, call.GetProperty("name").GetString()!, args));
                        if (part.TryGetProperty("thoughtSignature", out var sig))
                            content.Add(new AiOpaque(ProviderName, JsonSerializer.SerializeToElement(new { callId = id, signature = sig.GetString() })));
                    }
                }
            }
        }
        else if (json.TryGetProperty("promptFeedback", out var feedback) && feedback.TryGetProperty("blockReason", out var block))
        {
            stop = block.GetString();
        }

        var reason = stop switch
        {
            _ when content.OfType<AiToolCall>().Any() => AiStopReason.ToolUse,
            "STOP" => AiStopReason.EndTurn,
            "MAX_TOKENS" => AiStopReason.MaxTokens,
            "SAFETY" or "RECITATION" or "BLOCKLIST" or "PROHIBITED_CONTENT" or "SPII" or "OTHER" => AiStopReason.Refusal,
            _ => AiStopReason.Other,
        };

        // promptTokenCount includes cached tokens; thinking tokens are billed as output.
        long prompt = 0, output = 0, cached = 0;
        if (json.TryGetProperty("usageMetadata", out var u))
        {
            prompt = u.TryGetProperty("promptTokenCount", out var p) ? p.GetInt64() : 0;
            output = (u.TryGetProperty("candidatesTokenCount", out var o) ? o.GetInt64() : 0) + (u.TryGetProperty("thoughtsTokenCount", out var th) ? th.GetInt64() : 0);
            cached = u.TryGetProperty("cachedContentTokenCount", out var cc) ? cc.GetInt64() : 0;
        }
        return new AiResponse(content, reason, new AiUsage(prompt - cached, output, cached, 0), stop);
    }
}
