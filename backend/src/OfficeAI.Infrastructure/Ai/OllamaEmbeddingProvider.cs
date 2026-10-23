using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OfficeAI.Application.Knowledge;

namespace OfficeAI.Infrastructure.Ai;

/// <summary>
/// Knowledge embeddings from Ollama's /api/embed on this machine. Off unless <see cref="OllamaOptions.EmbeddingModel"/> is set
/// (e.g. bge-m3, which handles Thai; pull it first with "ollama pull bge-m3"). Free: runs on our own hardware.
/// </summary>
public sealed class OllamaEmbeddingProvider(IHttpClientFactory http, IOptions<OllamaOptions> options) : IEmbeddingProvider
{
    public const string HttpClientName = "ai-ollama-embed";

    // The chat endpoint is OpenAI-style (/v1); the embedding endpoint is Ollama's own, at the server root.
    private string Root => (options.Value.BaseUrl ?? "").TrimEnd('/') is var u && u.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? u[..^3] : (options.Value.BaseUrl ?? "").TrimEnd('/');

    public bool IsAvailable => Root.Length > 0 && !string.IsNullOrWhiteSpace(options.Value.EmbeddingModel);
    public string Model => options.Value.EmbeddingModel?.Trim() ?? "";

    public async Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        if (!IsAvailable) throw new InvalidOperationException("Embeddings are not configured.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(120));
        using var res = await http.CreateClient(HttpClientName).PostAsJsonAsync($"{Root}/api/embed", new { model = Model, input = texts }, timeout.Token);
        var body = await res.Content.ReadAsStringAsync(timeout.Token);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"Ollama embed returned {(int)res.StatusCode}: {(body.Length > 200 ? body[..200] : body)}");
        var vectors = JsonDocument.Parse(body).RootElement.GetProperty("embeddings").EnumerateArray()
            .Select(v => v.EnumerateArray().Select(x => x.GetSingle()).ToArray()).ToArray();
        if (vectors.Length != texts.Count) throw new HttpRequestException($"Ollama embed returned {vectors.Length} vectors for {texts.Count} texts.");
        return vectors;
    }
}
