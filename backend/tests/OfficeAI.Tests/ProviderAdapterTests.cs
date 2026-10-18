using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OfficeAI.Application.Agents;
using OfficeAI.Domain.Agents;
using OfficeAI.Infrastructure.Ai;

namespace OfficeAI.Tests;

/// <summary>OpenAI and Gemini adapters against a fake HTTP endpoint: request shape, response mapping and error handling.</summary>
public class ProviderAdapterTests
{
    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static readonly JsonElement Schema = JsonSerializer.SerializeToElement(new
    {
        type = "object", properties = new { query = new { type = "string" } }, required = new[] { "query" }, additionalProperties = false,
    });

    /// <summary>A conversation that already went once through a tool call and its result.</summary>
    private static AiRequest Conversation(IEnumerable<AiContent>? extraAssistant = null) => new("model-x", "You are a test agent.",
    [
        new AiMessage(AiRole.User, [new AiText("Find the spec")]),
        new AiMessage(AiRole.Assistant, [new AiText("Searching."), new AiToolCall("call_1", "knowledge_search", JsonSerializer.SerializeToElement(new { query = "spec" })), .. extraAssistant ?? []]),
        new AiMessage(AiRole.User, [new AiToolResult("call_1", "[]", false)]),
    ],
    [new AiToolDefinition("knowledge_search", "Search knowledge", Schema)], 1000, Effort.Medium);

    // ---------------- OpenAI ----------------

    private static (OpenAiProvider, FakeHandler) OpenAi(HttpStatusCode status, string body, string? key = "sk-test")
    {
        var handler = new FakeHandler(status, body);
        return (new OpenAiProvider(new Factory(handler), Options.Create(new OpenAiOptions { ApiKey = key, BaseUrl = "https://openai.test/v1" })), handler);
    }

    [Fact]
    public async Task OpenAi_sends_chat_completions_with_tools_and_maps_the_answer()
    {
        var (provider, handler) = OpenAi(HttpStatusCode.OK, """
            {"choices":[{"finish_reason":"tool_calls","message":{"role":"assistant","content":null,
              "tool_calls":[{"id":"call_2","type":"function","function":{"name":"knowledge_search","arguments":"{\"query\":\"api\"}"}}]}}],
             "usage":{"prompt_tokens":1200,"completion_tokens":30,"prompt_tokens_details":{"cached_tokens":1000}}}
            """);
        var response = await provider.CompleteAsync(Conversation(), CancellationToken.None);

        Assert.Equal("https://openai.test/v1/chat/completions", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer sk-test", handler.Request.Headers.Authorization!.ToString());
        using var sent = JsonDocument.Parse(handler.RequestBody!);
        var messages = sent.RootElement.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("call_1", messages[2].GetProperty("tool_calls")[0].GetProperty("id").GetString());
        Assert.Equal("{\"query\":\"spec\"}", messages[2].GetProperty("tool_calls")[0].GetProperty("function").GetProperty("arguments").GetString());
        Assert.Equal("tool", messages[3].GetProperty("role").GetString());
        Assert.Equal("call_1", messages[3].GetProperty("tool_call_id").GetString());
        Assert.Equal("knowledge_search", sent.RootElement.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString());
        Assert.Equal(1000, sent.RootElement.GetProperty("max_completion_tokens").GetInt32());

        Assert.Equal(AiStopReason.ToolUse, response.StopReason);
        var call = Assert.IsType<AiToolCall>(Assert.Single(response.Content));
        Assert.Equal("api", call.Input.GetProperty("query").GetString());
        Assert.Equal(new AiUsage(200, 30, 1000, 0), response.Usage); // cached tokens are priced separately
    }

    [Fact]
    public async Task OpenAi_errors_are_retryable_only_when_transient()
    {
        var (limited, _) = OpenAi(HttpStatusCode.TooManyRequests, """{"error":{"message":"slow down"}}""");
        Assert.True((await Assert.ThrowsAsync<AiProviderException>(() => limited.CompleteAsync(Conversation(), CancellationToken.None))).Retryable);

        var (bad, _) = OpenAi(HttpStatusCode.BadRequest, """{"error":{"message":"model not found"}}""");
        var ex = await Assert.ThrowsAsync<AiProviderException>(() => bad.CompleteAsync(Conversation(), CancellationToken.None));
        Assert.False(ex.Retryable);
        Assert.Contains("model not found", ex.Message);

        var (down, _) = OpenAi(HttpStatusCode.ServiceUnavailable, "oops");
        Assert.True((await Assert.ThrowsAsync<AiProviderException>(() => down.CompleteAsync(Conversation(), CancellationToken.None))).Retryable);
    }

    [Fact]
    public void Providers_without_a_key_are_unavailable()
    {
        if (Environment.GetEnvironmentVariable("OPENAI_API_KEY") is null) Assert.False(OpenAi(HttpStatusCode.OK, "{}", key: null).Item1.IsAvailable);
        if (Environment.GetEnvironmentVariable("GEMINI_API_KEY") is null) Assert.False(Gemini(HttpStatusCode.OK, "{}", key: null).Item1.IsAvailable);
    }

    // ---------------- Gemini ----------------

    private static (GeminiProvider, FakeHandler) Gemini(HttpStatusCode status, string body, string? key = "g-test")
    {
        var handler = new FakeHandler(status, body);
        return (new GeminiProvider(new Factory(handler), Options.Create(new GeminiOptions { ApiKey = key, BaseUrl = "https://gemini.test/v1beta" })), handler);
    }

    [Fact]
    public async Task Gemini_answers_function_calls_by_name_keeps_signatures_and_hides_the_key()
    {
        var signature = new AiOpaque(GeminiProvider.ProviderName, JsonSerializer.SerializeToElement(new { callId = "call_1", signature = "sig-abc" }));
        var (provider, handler) = Gemini(HttpStatusCode.OK, """
            {"candidates":[{"finishReason":"STOP","content":{"role":"model","parts":[
               {"text":"thinking...","thought":true},
               {"functionCall":{"name":"knowledge_search","args":{"query":"db"}},"thoughtSignature":"sig-next"}]}}],
             "usageMetadata":{"promptTokenCount":500,"candidatesTokenCount":20,"thoughtsTokenCount":40,"cachedContentTokenCount":100}}
            """);
        var response = await provider.CompleteAsync(Conversation([signature]), CancellationToken.None);

        Assert.Equal("https://gemini.test/v1beta/models/model-x:generateContent", handler.Request!.RequestUri!.ToString());
        Assert.DoesNotContain("g-test", handler.Request.RequestUri.ToString());
        Assert.Equal("g-test", handler.Request.Headers.GetValues("x-goog-api-key").Single());
        using var sent = JsonDocument.Parse(handler.RequestBody!);
        var contents = sent.RootElement.GetProperty("contents");
        Assert.Equal("model", contents[1].GetProperty("role").GetString());
        var callPart = contents[1].GetProperty("parts")[1];
        Assert.Equal("knowledge_search", callPart.GetProperty("functionCall").GetProperty("name").GetString());
        Assert.Equal("sig-abc", callPart.GetProperty("thoughtSignature").GetString());
        var responsePart = contents[2].GetProperty("parts")[0].GetProperty("functionResponse");
        Assert.Equal("knowledge_search", responsePart.GetProperty("name").GetString());
        Assert.Equal("[]", responsePart.GetProperty("response").GetProperty("result").GetString());
        Assert.Equal("You are a test agent.", sent.RootElement.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());

        Assert.Equal(AiStopReason.ToolUse, response.StopReason);
        var call = Assert.Single(response.Content.OfType<AiToolCall>());
        Assert.Equal("db", call.Input.GetProperty("query").GetString());
        Assert.DoesNotContain(response.Content, c => c is AiText); // thought summaries are dropped
        var opaque = Assert.Single(response.Content.OfType<AiOpaque>());
        Assert.Equal(call.Id, opaque.Payload.GetProperty("callId").GetString());
        Assert.Equal(new AiUsage(400, 60, 100, 0), response.Usage);
    }

    [Fact]
    public async Task Gemini_safety_stop_is_a_refusal()
    {
        var (provider, _) = Gemini(HttpStatusCode.OK, """{"candidates":[{"finishReason":"SAFETY","content":{"parts":[]}}],"usageMetadata":{"promptTokenCount":10}}""");
        var response = await provider.CompleteAsync(Conversation(), CancellationToken.None);
        Assert.Equal(AiStopReason.Refusal, response.StopReason);
        Assert.Empty(response.Content);
    }

    [Fact]
    public async Task Gemini_lists_only_models_that_can_generate()
    {
        var (provider, handler) = Gemini(HttpStatusCode.OK, """
            {"models":[{"name":"models/gemini-x-flash","supportedGenerationMethods":["generateContent","countTokens"]},
                       {"name":"models/text-embedding-x","supportedGenerationMethods":["embedContent"]}]}
            """);
        Assert.Equal(["gemini-x-flash"], await provider.RemoteModelsAsync(CancellationToken.None));
        Assert.Equal("g-test", handler.Request!.Headers.GetValues("x-goog-api-key").Single());
    }
}
