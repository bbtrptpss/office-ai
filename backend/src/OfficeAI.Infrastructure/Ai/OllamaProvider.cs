using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;
using OfficeAI.Application.Agents;

namespace OfficeAI.Infrastructure.Ai;

public class OllamaOptions
{
    /// <summary>Ollama's OpenAI-compatible endpoint, e.g. http://host.docker.internal:11434/v1. Empty disables the provider.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Local models on a small GPU are slow; give them longer than the cloud providers.</summary>
    public int TimeoutSeconds { get; set; } = 900;
}

/// <summary>
/// Models served by Ollama on this machine (or the local network) through its OpenAI-compatible API. No key is needed;
/// one saved on the AI settings page is sent as a bearer token, for an Ollama behind an authenticating proxy.
/// Runs cost nothing, so its models may be listed with zero prices.
/// </summary>
public sealed class OllamaProvider(IHttpClientFactory http, IOptions<OllamaOptions> options, IAiCredentials? credentials = null) : IAiProvider
{
    public const string ProviderName = "ollama";
    public const string HttpClientName = "ai-ollama";

    private string BaseUrl => (options.Value.BaseUrl ?? "").TrimEnd('/');

    public string Name => ProviderName;
    public bool IsSimulated => false;
    public bool IsFlatRate => true;
    public bool IsAvailable => BaseUrl.Length > 0;

    public async Task<string?> VerifyAsync(CancellationToken ct)
    {
        if (!IsAvailable) return "Ollama is not configured (AiProviders:Ollama:BaseUrl).";
        using var message = Request(HttpMethod.Get, "/models");
        return await HttpAi.CheckAsync(http, HttpClientName, message, "Ollama", ct);
    }

    public async Task<IReadOnlyList<string>> RemoteModelsAsync(CancellationToken ct)
    {
        if (!IsAvailable) return [];
        using var message = Request(HttpMethod.Get, "/models");
        var json = await HttpAi.SendAsync(http, HttpClientName, message, TimeSpan.FromSeconds(15), "Ollama", ct);
        return json.GetProperty("data").EnumerateArray().Select(m => m.GetProperty("id").GetString()!).Order().ToList();
    }

    public async Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct)
    {
        if (!IsAvailable) throw new AiProviderException("Ollama is not configured.", retryable: false);

        var body = OpenAiProvider.BuildRequest(request);
        // Ollama reads the older max_tokens field.
        body.Remove("max_completion_tokens");
        body["max_tokens"] = request.MaxTokens;
        using var message = Request(HttpMethod.Post, "/chat/completions");
        message.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        var json = await HttpAi.SendAsync(http, HttpClientName, message, TimeSpan.FromSeconds(options.Value.TimeoutSeconds), "Ollama", ct);
        return OpenAiProvider.ParseResponse(json);
    }

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var message = new HttpRequestMessage(method, BaseUrl + path);
        if (credentials?.GetKey(ProviderName) is { } key) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return message;
    }
}
