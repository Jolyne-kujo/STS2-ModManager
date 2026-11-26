namespace ModTheSpire2.Bootstrap;

internal sealed record RuntimeSavedSelection(string Id, int Source, bool IsEnabled)
{
    public string Key => RuntimeModAccess.BuildKey(Id, Source);
}

internal static class RuntimeSelectionPolicy
{
    public static IReadOnlyList<RuntimeSavedSelection> Merge(
        IReadOnlyList<RuntimeSavedSelection> visible, IReadOnlyList<RuntimeSavedSelection> previous)
    {
        var keys = new HashSet<string>(visible.Select(item => item.Key), StringComparer.OrdinalIgnoreCase);
        var remaining = new Queue<RuntimeSavedSelection>(visible);
        var result = new List<RuntimeSavedSelection>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var old in previous)
        {
            if (!seen.Add(old.Key)) continue;
            if (!keys.Contains(old.Key)) result.Add(old);
            else if (remaining.Count > 0) result.Add(remaining.Dequeue());
        }
        result.AddRange(remaining);
        return result.OrderByDescending(item => item.Id.Equals("ModTheSpire2", StringComparison.OrdinalIgnoreCase)).ToArray();
    }
}
