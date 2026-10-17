using System.Text.Json;
using System.Text.RegularExpressions;
using OfficeAI.Domain.Work;

namespace OfficeAI.Application.Workflows;

public sealed record PlanItem(
    string Key, string Title, string? Description, string? AcceptanceCriteria, List<string> RequiredSkills, List<string> DependsOn, TaskPriority Priority);

public static partial class PlanParser
{
    public const int MaxItems = 30;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$")]
    private static partial Regex KeyPattern();

    /// <summary>Parses and validates a proposed plan. Returns the items, or an error that the planner can act on.</summary>
    public static (List<PlanItem>? Items, string? Error) Parse(JsonElement tasks)
    {
        if (tasks.ValueKind != JsonValueKind.Array) return (null, "'tasks' must be an array.");
        var items = new List<PlanItem>();
        foreach (var t in tasks.EnumerateArray())
        {
            if (t.ValueKind != JsonValueKind.Object) return (null, "Each task must be an object.");
            var key = Str(t, "key")?.Trim().ToLowerInvariant();
            var title = Str(t, "title")?.Trim();
            if (key is null || !KeyPattern().IsMatch(key)) return (null, $"Task key '{key}' must be 1-40 lowercase letters, digits or dashes.");
            if (string.IsNullOrWhiteSpace(title) || title.Length > 200) return (null, $"Task '{key}' needs a title of at most 200 characters.");
            var priority = Enum.TryParse<TaskPriority>(Str(t, "priority"), true, out var p) && Enum.IsDefined(p) ? p : TaskPriority.Medium;
            items.Add(new PlanItem(key, title, Limit(Str(t, "description"), 4000), Limit(Str(t, "acceptanceCriteria"), 4000),
                Strings(t, "requiredSkills").Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).Distinct().Take(10).ToList(),
                Strings(t, "dependsOn").Select(s => s.Trim().ToLowerInvariant()).Distinct().ToList(),
                priority));
        }
        if (items.Count == 0) return (null, "The plan has no tasks.");
        if (items.Count > MaxItems) return (null, $"The plan has {items.Count} tasks; the limit is {MaxItems}. Group smaller steps together.");

        var keys = new HashSet<string>();
        foreach (var i in items)
            if (!keys.Add(i.Key)) return (null, $"Task key '{i.Key}' is used twice.");
        foreach (var i in items)
            foreach (var d in i.DependsOn)
            {
                if (d == i.Key) return (null, $"Task '{i.Key}' depends on itself.");
                if (!keys.Contains(d)) return (null, $"Task '{i.Key}' depends on unknown task '{d}'.");
            }

        // Reject cycles with a topological sort.
        var remaining = items.ToDictionary(i => i.Key, i => i.DependsOn.ToHashSet());
        while (remaining.Count > 0)
        {
            var ready = remaining.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key).ToList();
            if (ready.Count == 0) return (null, $"Dependencies form a loop between: {string.Join(", ", remaining.Keys)}.");
            foreach (var k in ready)
            {
                remaining.Remove(k);
                foreach (var deps in remaining.Values) deps.Remove(k);
            }
        }
        return (items, null);
    }

    public static string Serialize(List<PlanItem> items) => JsonSerializer.Serialize(items, Json);

    public static List<PlanItem> Deserialize(string json) => JsonSerializer.Deserialize<List<PlanItem>>(json, Json) ?? [];

    private static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? Limit(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];

    private static IEnumerable<string> Strings(JsonElement o, string name)
    {
        if (!o.TryGetProperty(name, out var v)) return [];
        return v.ValueKind switch
        {
            JsonValueKind.Array => v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!),
            JsonValueKind.String => v.GetString()!.Split(','),
            _ => [],
        };
    }
}
