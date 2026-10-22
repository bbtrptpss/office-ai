using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using OfficeAI.Application.Agents;

namespace OfficeAI.Infrastructure.Ai;

/// <summary>
/// AI employees on the Claude Code CLI signed in on this PC (scripts/windows/claude-bridge.mjs), billed to that Claude plan rather than per token.
/// Claude Code runs with its own tools off, so the company's tools are described in the system prompt and Claude asks for them in
/// &lt;tool_call&gt; blocks; every turn sends the whole conversation, so no Claude Code session is kept.
/// </summary>
public sealed partial class ClaudeCodeProvider(IHttpClientFactory http, IOptions<ClaudeBridgeOptions> options) : IAiProvider
{
    public const string ProviderName = "claude-code";
    public const string HttpClientName = "ai-claude-code";

    /// <summary>The bridge stops Claude Code after 180 seconds; wait a little longer for its answer.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(200);

    public string Name => ProviderName;
    public bool IsSimulated => false;
    public bool IsFlatRate => true;
    public bool IsAvailable => !string.IsNullOrWhiteSpace(options.Value.Url) && (options.Value.Token?.Length ?? 0) >= 24;

    public async Task<string?> VerifyAsync(CancellationToken ct)
    {
        if (!IsAvailable) return "The Claude Code bridge is not configured (ClaudeBridge:Url and ClaudeBridge:Token).";
        using var message = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(options.Value.Url!), "/health"));
        return await HttpAi.CheckAsync(http, HttpClientName, message, "Claude Code bridge", ct);
    }

    /// <summary>Claude Code's model aliases; the plan decides which of them the account may use.</summary>
    public Task<IReadOnlyList<string>> RemoteModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["haiku", "opus", "sonnet"]);

    public async Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct)
    {
        if (!IsAvailable) throw new AiProviderException("The Claude Code bridge is not configured.", retryable: false);
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(options.Value.Url!), "/ask"))
        {
            Content = JsonContent.Create(new { prompt = BuildPrompt(request), system = BuildSystem(request), model = request.ModelId }),
        };
        message.Headers.Add("x-bridge-token", options.Value.Token);
        var json = await HttpAi.SendAsync(http, HttpClientName, message, Timeout, "Claude Code bridge", ct);
        var answer = json.Deserialize<BridgeAnswer>(JsonOptions)
            ?? throw new AiProviderException("The Claude Code bridge returned nothing.", retryable: true);
        if (answer.Error is { } error) throw new AiProviderException($"Claude Code: {error}", retryable: !error.Contains("invalid", StringComparison.OrdinalIgnoreCase));
        return ParseReply(answer.Reply ?? "", new AiUsage(answer.InputTokens, answer.OutputTokens, answer.CacheReadTokens, answer.CacheWriteTokens));
    }

    private sealed record BridgeAnswer(string? Reply, string? Error, long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { NumberHandling = JsonNumberHandling.AllowReadingFromString };

    internal static string BuildSystem(AiRequest request)
    {
        var sb = new StringBuilder(request.SystemPrompt.TrimEnd()).AppendLine().AppendLine();
        if (request.Tools.Count == 0) return sb.Append("Answer in plain text.").ToString();
        sb.AppendLine("# Tools");
        sb.AppendLine("You act only through these tools. Each is described with a JSON Schema for its arguments:");
        foreach (var t in request.Tools)
            sb.AppendLine(new JsonObject { ["name"] = t.Name, ["description"] = t.Description, ["parameters"] = JsonNode.Parse(t.InputSchema.GetRawText()) }.ToJsonString());
        sb.AppendLine();
        sb.AppendLine("To call tools, end your reply with one or more blocks in exactly this form (one JSON object per block):");
        sb.AppendLine("<tool_call>");
        sb.AppendLine("{\"name\": \"tool_name\", \"arguments\": {\"argument\": \"value\"}}");
        sb.AppendLine("</tool_call>");
        sb.AppendLine("A short note may come before the blocks. Then stop: the results come back in the next message. "
                      + "Never write tool results yourself. When you are done and need no more tools, reply with no <tool_call> block.");
        return sb.ToString();
    }

    /// <summary>The conversation so far as one transcript; Claude Code sees it as a single new message each turn.</summary>
    internal static string BuildPrompt(AiRequest request)
    {
        var sb = new StringBuilder();
        foreach (var m in request.Messages)
        {
            foreach (var c in m.Content)
            {
                switch (c)
                {
                    case AiText text when m.Role == AiRole.User:
                        sb.AppendLine("[user]").AppendLine(text.Text).AppendLine();
                        break;
                    case AiText text:
                        sb.AppendLine("[assistant]").AppendLine(text.Text).AppendLine();
                        break;
                    case AiToolCall call:
                        sb.AppendLine("[assistant]").AppendLine("<tool_call>")
                            .AppendLine(new JsonObject { ["name"] = call.Name, ["arguments"] = JsonNode.Parse(call.Input.GetRawText()) }.ToJsonString())
                            .AppendLine("</tool_call>").AppendLine();
                        break;
                    case AiToolResult result:
                        sb.AppendLine($"[tool result {result.ToolCallId}{(result.IsError ? " ERROR" : "")}]").AppendLine(result.Content).AppendLine();
                        break;
                }
            }
        }
        sb.Append("Continue as the assistant.");
        return sb.ToString();
    }

    [GeneratedRegex(@"<tool_call>\s*(.*?)\s*</tool_call>", RegexOptions.Singleline)]
    private static partial Regex ToolCallBlock();

    internal static AiResponse ParseReply(string reply, AiUsage usage)
    {
        var content = new List<AiContent>();
        var text = ToolCallBlock().Replace(reply, "").Trim();
        if (text.Length > 0) content.Add(new AiText(text));
        foreach (Match m in ToolCallBlock().Matches(reply))
        {
            string? name = null;
            var args = JsonSerializer.SerializeToElement(new { });
            try
            {
                using var doc = JsonDocument.Parse(m.Groups[1].Value);
                if (doc.RootElement.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String) name = n.GetString();
                if (doc.RootElement.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object) args = a.Clone();
            }
            catch (JsonException)
            {
                // malformed block: reported to the model below as a tool it cannot find
            }
            content.Add(new AiToolCall($"cc_{Guid.NewGuid():N}", name ?? "invalid_tool_call", args));
        }
        var stop = content.OfType<AiToolCall>().Any() ? AiStopReason.ToolUse : AiStopReason.EndTurn;
        return new AiResponse(content, stop, usage, stop == AiStopReason.ToolUse ? "tool_calls" : "end_turn");
    }
}
