namespace Sts2ModManager.Core;

public sealed class ModSelectionService
{
    public IReadOnlyList<ModEntry> Merge(
        IReadOnlyList<ModEntry> discovered,
        IReadOnlyList<SavedModState> saved)
    {
        var byKey = discovered.ToDictionary(mod => mod.Key, StringComparer.OrdinalIgnoreCase);
        var byId = discovered
            .GroupBy(mod => mod.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ModEntry>(discovered.Count);

        foreach (var savedState in saved)
        {
            if (!byKey.TryGetValue(savedState.Key, out var mod) &&
                !byId.TryGetValue(savedState.Id, out mod))
            {
                continue;
            }

            if (used.Add(mod.Key))
            {
                result.Add(mod with
                {
                    Source = string.IsNullOrWhiteSpace(savedState.Source) ? mod.Source : savedState.Source,
                    IsEnabled = mod.IsBootstrap || savedState.IsEnabled
                });
            }
        }

        foreach (var mod in discovered)
        {
            if (used.Add(mod.Key))
            {
                result.Add(mod);
            }
        }

        return result;
    }
}
