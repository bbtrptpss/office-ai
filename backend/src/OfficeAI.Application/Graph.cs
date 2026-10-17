using OfficeAI.Domain.Common;

namespace OfficeAI.Application;

public static class Graph
{
    /// <summary>
    /// True if making <paramref name="newParent"/> the parent of <paramref name="node"/> would form a loop,
    /// given the current child → parent map.
    /// </summary>
    public static bool WouldCreateParentCycle(Guid node, Guid newParent, IReadOnlyDictionary<Guid, Guid?> parentOf)
    {
        var seen = new HashSet<Guid>();
        Guid? current = newParent;
        while (current is { } c)
        {
            if (c == node || !seen.Add(c)) return true;
            current = parentOf.TryGetValue(c, out var p) ? p : null;
        }
        return false;
    }

    /// <summary>True if adding edge from → to (from depends on to) creates a cycle in the dependency graph.</summary>
    public static bool WouldCreateDependencyCycle(Guid from, Guid to, ILookup<Guid, Guid> dependsOn)
    {
        // A cycle exists if 'from' is reachable starting at 'to'.
        var stack = new Stack<Guid>([to]);
        var seen = new HashSet<Guid>();
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n == from) return true;
            if (!seen.Add(n)) continue;
            foreach (var next in dependsOn[n]) stack.Push(next);
        }
        return false;
    }
}

internal static class Validate
{
    public static void Required(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainException("validation", $"{field} is required.");
    }
}
