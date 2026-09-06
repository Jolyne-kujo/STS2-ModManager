namespace ModTheSpire2.Bootstrap;

internal static class RuntimeModOrderPolicy
{
    public static IReadOnlyList<RuntimeModOption> Normalize(
        IReadOnlyList<RuntimeModOption> runtimeMods,
        IReadOnlyDictionary<string, int> savedOrder)
    {
        var enabled = runtimeMods
            .Where(mod => mod.IsEnabled)
            .OrderBy(mod => savedOrder.TryGetValue(mod.Key, out var index) ? index : int.MaxValue)
            .ThenBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(mod => mod.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(mod => mod.Source);

        var disabled = runtimeMods
            .Where(mod => !mod.IsEnabled)
            .OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(mod => mod.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(mod => mod.Source);

        return enabled.Concat(disabled).ToArray();
    }
}
