using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using OfficeAI.Application.Communication;

namespace OfficeAI.Infrastructure.Ai;

public class ClaudeBridgeOptions
{
    /// <summary>Where scripts/windows/claude-bridge.mjs listens, as seen from the API (in Docker: http://host.docker.internal:8765).</summary>
    public string? Url { get; set; }
    public string? Token { get; set; }
}

/// <summary>Calls the host's Claude Code bridge. The token never leaves the server side.</summary>
public sealed class HttpClaudeBridge(HttpClient http, IOptions<ClaudeBridgeOptions> options) : IClaudeBridge
{
    private sealed record Answer(string? Reply, [property: JsonPropertyName("sessionId")] string? SessionId, decimal CostUsd, string? Error);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.Url) && (options.Value.Token?.Length ?? 0) >= 24;

    public async Task<ClaudeBridgeReply> AskAsync(string prompt, string system, string? sessionId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(options.Value.Url!), "/ask"))
        {
            Content = JsonContent.Create(new { prompt, system, sessionId }),
        };
        req.Headers.Add("x-bridge-token", options.Value.Token);
        using var res = await http.SendAsync(req, ct);
        var answer = await res.Content.ReadFromJsonAsync<Answer>(ct);
        if (!res.IsSuccessStatusCode || answer?.Reply is null || answer.SessionId is null)
            throw new HttpRequestException(answer?.Error ?? $"Claude bridge returned {(int)res.StatusCode}.");
        return new ClaudeBridgeReply(answer.Reply, answer.SessionId, answer.CostUsd);
    }
}
