using System.Text.Json;
using System.Text.Json.Nodes;

namespace OfficeAI.Application.Agents;

/// <summary>
/// Stores a run's conversation as JSON so an interrupted run can continue where it stopped.
/// The format is explicit (not polymorphic serialization) so stored checkpoints keep working when types change.
/// </summary>
public static class ConversationCodec
{
    public static string Encode(IReadOnlyList<AiMessage> messages)
    {
        var array = new JsonArray();
        foreach (var m in messages)
        {
            var content = new JsonArray();
            foreach (var c in m.Content)
            {
                content.Add(c switch
                {
                    AiText t => new JsonObject { ["type"] = "text", ["text"] = t.Text },
                    AiToolCall call => new JsonObject { ["type"] = "call", ["id"] = call.Id, ["name"] = call.Name, ["input"] = JsonNode.Parse(call.Input.GetRawText()) },
                    AiToolResult r => new JsonObject { ["type"] = "result", ["id"] = r.ToolCallId, ["content"] = r.Content, ["isError"] = r.IsError },
                    AiOpaque o => new JsonObject { ["type"] = "opaque", ["provider"] = o.Provider, ["payload"] = JsonNode.Parse(o.Payload.GetRawText()) },
                    _ => throw new InvalidOperationException($"Unknown content {c.GetType().Name}"),
                });
            }
            array.Add(new JsonObject { ["role"] = m.Role == AiRole.User ? "user" : "assistant", ["content"] = content });
        }
        return array.ToJsonString();
    }

    public static List<AiMessage> Decode(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var messages = new List<AiMessage>();
        foreach (var m in doc.RootElement.EnumerateArray())
        {
            var content = new List<AiContent>();
            foreach (var c in m.GetProperty("content").EnumerateArray())
            {
                content.Add(c.GetProperty("type").GetString() switch
                {
                    "text" => new AiText(c.GetProperty("text").GetString()!),
                    "call" => new AiToolCall(c.GetProperty("id").GetString()!, c.GetProperty("name").GetString()!, c.GetProperty("input").Clone()),
                    "result" => new AiToolResult(c.GetProperty("id").GetString()!, c.GetProperty("content").GetString()!, c.GetProperty("isError").GetBoolean()),
                    "opaque" => new AiOpaque(c.GetProperty("provider").GetString()!, c.GetProperty("payload").Clone()),
                    var other => throw new InvalidOperationException($"Unknown content type '{other}'"),
                });
            }
            messages.Add(new AiMessage(m.GetProperty("role").GetString() == "user" ? AiRole.User : AiRole.Assistant, content));
        }
        return messages;
    }
}
