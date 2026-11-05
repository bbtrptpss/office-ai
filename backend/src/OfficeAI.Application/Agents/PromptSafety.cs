using System.Text.RegularExpressions;

namespace OfficeAI.Application.Agents;

/// <summary>
/// Keeps text written by others from passing as part of a prompt's structure. It does not make a model obey: the real limits are tool
/// grants and human approvals. It stops a task or tool result from faking the end of its data block or another speaker's turn.
/// </summary>
public static partial class PromptSafety
{
    [GeneratedRegex(@"<(\s*/?\s*task_data)", RegexOptions.IgnoreCase)]
    private static partial Regex TaskDataTag();

    [GeneratedRegex(@"<(\s*/?\s*tool_call)", RegexOptions.IgnoreCase)]
    private static partial Regex ToolCallTag();

    [GeneratedRegex(@"^([ \t]*)\[(user|assistant|tool result)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex TurnLabel();

    /// <summary>For text placed inside a &lt;task_data&gt; block.</summary>
    public static string TaskData(string? text) => TaskDataTag().Replace(text ?? "", "&lt;$1");

    /// <summary>For user text and tool results in a conversation flattened into one transcript.</summary>
    public static string TranscriptData(string text) =>
        TurnLabel().Replace(ToolCallTag().Replace(text, "&lt;$1"), "$1(quoted) [$2");
}
