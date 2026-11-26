using System.Collections;
using System.Reflection;

namespace ModTheSpire2.Bootstrap;

internal sealed class RuntimeModOption
{
    public RuntimeModOption(
        string id,
        string name,
        int source,
        int loadState,
        bool isEnabled,
        object runtimeMod,
        string author = "",
        string version = "",
        string description = "")
    {
        Id = id;
        Name = string.IsNullOrWhiteSpace(name) ? id : name;
        Source = source;
        LoadState = loadState;
        IsEnabled = isEnabled || IsBootstrap;
        RuntimeMod = runtimeMod;
        Author = author;
        Version = version;
        Description = description;
    }

    public string Id { get; }
    public string Name { get; }
    public int Source { get; }
    public int LoadState { get; set; }
    public bool IsEnabled { get; set; }
    public object RuntimeMod { get; }
    public string Author { get; }
    public string Version { get; }
    public string Description { get; }
    public bool IsBootstrap => Id.Equals("ModTheSpire2", StringComparison.OrdinalIgnoreCase);
    public string Key => Id + "::" + Source;
}

internal static class RuntimeModAccess
{
    private const string ModManagerName = "MegaCrit.Sts2.Core.Modding.ModManager";
    private const string SaveManagerName = "MegaCrit.Sts2.Core.Saves.SaveManager";
    private const string SettingsSaveModName = "MegaCrit.Sts2.Core.Modding.SettingsSaveMod";
    private const string ModSourceName = "MegaCrit.Sts2.Core.Modding.ModSource";
    private static object? _pendingSettings;
    private static IReadOnlyList<RuntimeSavedSelection>? _pendingSelection;

    public static IReadOnlyList<RuntimeModOption> ReadOptions()
    {
        var modManager = FindType(ModManagerName);
        if (modManager is null) return Array.Empty<RuntimeModOption>();

        var mods = GetMember(modManager, "Mods") as IEnumerable;
        if (mods is null) return Array.Empty<RuntimeModOption>();

        var savedStates = ReadSavedStates();
        var result = new List<RuntimeModOption>();
        foreach (var runtimeMod in mods)
        {
            var manifest = GetMember(runtimeMod, "manifest");
            var id = AsString(GetMember(manifest, "id"));
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (id.Equals("ModTheSpire2", StringComparison.OrdinalIgnoreCase) &&
                AsString(GetMember(runtimeMod, "state")) == "DisabledDuplicate") continue;

            var source = ToInt(GetMember(runtimeMod, "modSource"));
            var key = BuildKey(id, source);
            var state = ToInt(GetMember(runtimeMod, "state"));
            result.Add(new RuntimeModOption(
                id,
                AsString(GetMember(manifest, "name")) ?? id,
                source,
                state,
                AsString(GetMember(runtimeMod, "state")) != "DisabledDuplicate" &&
                    (savedStates.TryGetValue(key, out var saved) ? saved.IsEnabled : AsString(GetMember(runtimeMod, "state")) != "Disabled"),
                runtimeMod,
                AsString(GetMember(manifest, "author")) ?? string.Empty,
                AsString(GetMember(manifest, "version")) ?? string.Empty,
                AsString(GetMember(manifest, "description")) ?? string.Empty));
        }

        return RuntimeModOrderPolicy.Normalize(
            result,
            savedStates.ToDictionary(pair => pair.Key, pair => pair.Value.Order, StringComparer.OrdinalIgnoreCase));
    }

    public static void SaveSelection(IReadOnlyList<RuntimeModOption> orderedOptions)
    {
        var saveManagerType = FindType(SaveManagerName)
                               ?? throw new InvalidOperationException("StS2 SaveManager type was not found.");
        var saveManager = FindProperty(saveManagerType, "Instance")?.GetValue(null)
                          ?? throw new InvalidOperationException("SaveManager.Instance is null.");
        var settingsSave = GetMember(saveManager, "SettingsSave")
                           ?? throw new InvalidOperationException("SaveManager.SettingsSave is null.");
        var modSettings = GetMember(settingsSave, "ModSettings");
        if (modSettings is null)
        {
            var modSettingsType = FindType("MegaCrit.Sts2.Core.Modding.ModSettings")
                                  ?? throw new InvalidOperationException("ModSettings type was not found.");
            modSettings = Activator.CreateInstance(modSettingsType)
                          ?? throw new InvalidOperationException("ModSettings could not be created.");
            SetMember(settingsSave, "ModSettings", modSettings);
        }

        var settingsModType = FindType(SettingsSaveModName)
                              ?? throw new InvalidOperationException("SettingsSaveMod type was not found.");
        var listType = typeof(List<>).MakeGenericType(settingsModType);
        var selection = RuntimeSelectionPolicy.Merge(
            orderedOptions.Select(option => new RuntimeSavedSelection(option.Id, option.Source, option.IsEnabled || option.IsBootstrap)).ToArray(),
            ReadSettingsList(GetMember(modSettings, "ModList")));
        var list = CreateSettingsList(listType, selection);
        _pendingSettings = null;
        SetMember(modSettings, "ModList", list);
        SetMember(modSettings, "PlayerAgreedToModLoading", true);

        // ModManager caches the same settings object in a private static field. Keep both
        // the in-memory state and the on-disk save synchronized for this launch.
        var modManagerType = FindType(ModManagerName);
        FindField(modManagerType, "_settings")?.SetValue(null, modSettings);

        PersistSettings(saveManager);
        RememberSelection(modSettings, selection);
    }

    internal static void RememberSelection(object settings, IReadOnlyList<RuntimeSavedSelection> selection)
    {
        _pendingSettings = settings;
        _pendingSelection = selection.ToArray();
    }

    internal static void RestoreNativeSelection(object settings)
    {
        if (!ReferenceEquals(settings, _pendingSettings) || _pendingSelection is null || StartupLoadGate.IsReplaying) return;
        var snapshot = _pendingSelection;
        _pendingSettings = null; // One-shot: never override subsequent in-game user edits.
        _pendingSelection = null;
        try
        {
            var current = GetMember(settings, "ModList") as IList
                          ?? throw new InvalidOperationException("Finalized mod selection is unavailable.");
            var savedFlags = snapshot.ToDictionary(item => item.Key, item => item.IsEnabled, StringComparer.OrdinalIgnoreCase);
            var corrected = ReadSettingsList(current).Select(item => savedFlags.TryGetValue(item.Key, out var enabled)
                ? item with { IsEnabled = enabled } : item).ToArray();
            var merged = RuntimeSelectionPolicy.Merge(corrected, snapshot);
            SetMember(settings, "ModList", CreateSettingsList(current.GetType(), merged));
            RuntimeDiagnostics.Info("Preserved source-specific selections after native loader finalization.");
        }
        catch (Exception exception)
        {
            RuntimeDiagnostics.Error("Failed to preserve finalized mod selection.", exception);
        }
    }

    private static IReadOnlyList<RuntimeSavedSelection> ReadSettingsList(object? value) =>
        value is IEnumerable list ? list.Cast<object>().Select(item => new RuntimeSavedSelection(
            AsString(GetMember(item, "Id")) ?? "", ToInt(GetMember(item, "Source")), ToBool(GetMember(item, "IsEnabled"), true)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Id)).ToArray() : Array.Empty<RuntimeSavedSelection>();

    private static IList CreateSettingsList(Type listType, IReadOnlyList<RuntimeSavedSelection> selection)
    {
        var itemType = listType.GetGenericArguments().Single();
        var sourceType = FindProperty(itemType, "Source")?.PropertyType ?? FindField(itemType, "Source")?.FieldType
                         ?? throw new MissingMemberException(itemType.FullName, "Source");
        var list = (IList)Activator.CreateInstance(listType)!;
        foreach (var saved in selection)
        {
            var item = Activator.CreateInstance(itemType)!;
            SetMember(item, "Id", saved.Id);
            SetMember(item, "Source", Enum.ToObject(sourceType, saved.Source));
            SetMember(item, "IsEnabled", saved.IsEnabled);
            list.Add(item);
        }
        return list;
    }

    internal static void PersistSettings(object saveManager)
    {
        // The public SaveManager wrapper catches and logs all write errors. Invoke
        // the same synchronous settings writer directly so failed saves reach the
        // selector's retry handler instead of silently continuing or exiting.
        var settingsManager = GetMember(saveManager, "_settingsSaveManager")
                              ?? throw new InvalidOperationException("SaveManager._settingsSaveManager was not found.");
        var saveSettings = FindMethod(settingsManager.GetType(), "SaveSettings", Type.EmptyTypes)
                           ?? throw new InvalidOperationException("SettingsSaveManager.SaveSettings() was not found.");
        saveSettings.Invoke(settingsManager, null);
    }

    public static void SynchronizeLoadState(RuntimeModOption option)
    {
        var stateField = FindField(option.RuntimeMod.GetType(), "state")
                         ?? throw new InvalidOperationException("ModLoadState field was not found.");
        var current = stateField.GetValue(option.RuntimeMod)?.ToString();
        // Re-evaluate duplicates after the selection changes, but never reinitialize
        // a mod whose code has already run or whose earlier initialization failed.
        if (current is not ("None" or "Disabled" or "DisabledDuplicate")) return;
        var next = Enum.Parse(stateField.FieldType, option.IsEnabled || option.IsBootstrap ? "None" : "Disabled");
        stateField.SetValue(option.RuntimeMod, next);
        option.LoadState = Convert.ToInt32(next);
    }

    internal static IReadOnlyList<RuntimeModOption> PrepareLoadOrder(Type? modManager, IReadOnlyList<RuntimeModOption> options)
    {
        if (modManager is null) return options;
        var settings = GetMember(modManager, "_settings")
                       ?? throw new InvalidOperationException("ModManager settings are unavailable.");
        var savedList = GetMember(settings, "ModList")
                        ?? throw new InvalidOperationException("ModManager saved order is unavailable.");
        var removeDisabled = FindMethod(modManager, "RemoveDisabledMods", Type.EmptyTypes)
                             ?? throw new MissingMethodException(modManager.FullName, "RemoveDisabledMods");
        var sort = FindMethod(modManager, "SortModList", new[] { savedList.GetType() });
        // Initialize holds a live enumerator over the original list. Replace its
        // field instead of mutating that list; the loader tail then persists the
        // canonical order, including deterministic disabled entries.
        var modsField = FindField(modManager, "_mods")
                        ?? throw new MissingFieldException(modManager.FullName, "_mods");
        var original = modsField.GetValue(null) as IEnumerable
                       ?? throw new InvalidOperationException("Native mod collection is unavailable.");
        var replacement = Activator.CreateInstance(modsField.FieldType) as IList
                          ?? throw new InvalidOperationException("Native mod collection could not be created.");
        var selectedObjects = new HashSet<object>(options.Select(option => option.RuntimeMod), ReferenceEqualityComparer.Instance);
        foreach (var option in options) replacement.Add(option.RuntimeMod);
        foreach (var mod in original)
            if (mod is not null && !selectedObjects.Contains(mod)) replacement.Add(mod);
        modsField.SetValue(null, replacement);
        // Old game versions have no dependency sorter. Keep their user-selected order.
        var initializedStates = options.Select(option => (option.RuntimeMod, Field: FindField(option.RuntimeMod.GetType(), "state")))
            .Where(pair => pair.Field?.GetValue(pair.RuntimeMod)?.ToString() is "Loaded" or "Failed" or "AddedAtRuntime")
            .Select(pair => (pair.RuntimeMod, pair.Field, Value: pair.Field!.GetValue(pair.RuntimeMod))).ToArray();
        try { removeDisabled.Invoke(null, null); }
        finally
        {
            foreach (var saved in initializedStates) saved.Field!.SetValue(saved.RuntimeMod, saved.Value);
        }
        if (sort is null) return options;
        if (GetMember(modManager, "_circularDependencies") is IDictionary cycles) cycles.Clear();
        sort.Invoke(null, new[] { savedList });
        if (GetMember(modManager, "Mods") is not IEnumerable runtimeOrder)
            throw new InvalidOperationException("Native sorted mod list is unavailable.");
        var byObject = options.ToDictionary(option => option.RuntimeMod, ReferenceEqualityComparer.Instance);
        // The native sorter puts already-loaded mods at the end. Bootstrap is
        // already loaded here, but must stay first when Initialize rewrites ModList.
        var canonical = runtimeOrder.Cast<object>().OrderByDescending(mod =>
            byObject.TryGetValue(mod, out var option) && option.IsBootstrap ? 2 :
            AsString(GetMember(GetMember(mod, "manifest"), "id"))?.Equals("ModTheSpire2", StringComparison.OrdinalIgnoreCase) == true ? 1 : 0).ToArray();
        var result = new List<RuntimeModOption>();
        foreach (var mod in canonical)
            if (mod is not null && byObject.TryGetValue(mod, out var option)) result.Add(option);
        if (result.Count != options.Count)
            throw new InvalidOperationException("Native mod list changed while preparing the startup selection.");
        var finalList = (IList)Activator.CreateInstance(modsField.FieldType)!;
        foreach (var mod in canonical) finalList.Add(mod);
        modsField.SetValue(null, finalList);
        return result;
    }

    internal static Type? ModManagerType => FindType(ModManagerName);

    public static string GetKey(object runtimeMod)
    {
        var manifest = GetMember(runtimeMod, "manifest");
        return BuildKey(AsString(GetMember(manifest, "id")) ?? string.Empty, ToInt(GetMember(runtimeMod, "modSource")));
    }

    public static string BuildKey(string id, int source) => id + "::" + source;

    private sealed record SavedRuntimeState(bool IsEnabled, int Order);

    private static Dictionary<string, SavedRuntimeState> ReadSavedStates()
    {
        var values = new Dictionary<string, SavedRuntimeState>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var saveManagerType = FindType(SaveManagerName);
            var saveManager = saveManagerType is null ? null : FindProperty(saveManagerType, "Instance")?.GetValue(null);
            var settingsSave = GetMember(saveManager, "SettingsSave");
            var modSettings = GetMember(settingsSave, "ModSettings");
            if (GetMember(modSettings, "ModList") is not IEnumerable list) return values;

            var index = 0;
            foreach (var item in list)
            {
                var id = AsString(GetMember(item, "Id"));
                if (string.IsNullOrWhiteSpace(id)) continue;
                values[BuildKey(id, ToInt(GetMember(item, "Source")))] =
                    new SavedRuntimeState(ToBool(GetMember(item, "IsEnabled"), true), index);
                index++;
            }
        }
        catch
        {
            // Default enabled state is safe when the save object is not ready yet.
        }

        return values;
    }

    private static object? GetMember(object? instance, string name)
    {
        if (instance is null) return null;
        var type = instance is Type staticType ? staticType : instance.GetType();
        var property = FindProperty(type, name);
        if (property is not null) return property.GetValue(instance is Type ? null : instance);
        var field = FindField(type, name);
        return field?.GetValue(instance is Type ? null : instance);
    }

    private static void SetMember(object instance, string name, object? value)
    {
        var type = instance.GetType();
        var property = FindProperty(type, name);
        if (property is not null && property.CanWrite)
        {
            property.SetValue(instance, value);
            return;
        }

        FindField(type, name)?.SetValue(instance, value);
    }

    private static Type? FindType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(fullName, throwOnError: false, ignoreCase: false);
            if (type is not null) return type;
        }

        return null;
    }

    private static PropertyInfo? FindProperty(Type? type, string name)
    {
        return type?.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
    }

    private static FieldInfo? FindField(Type? type, string name)
    {
        return type?.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
    }

    private static MethodInfo? FindMethod(Type? type, string name, Type[] parameters)
    {
        return type?.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance,
            binder: null, types: parameters, modifiers: null);
    }

    private static string? AsString(object? value) => value switch
    {
        null => null,
        string text => text,
        _ => value.ToString()
    };

    private static int ToInt(object? value) => value switch
    {
        null => 0,
        int number => number,
        long number => (int)number,
        Enum enumValue => Convert.ToInt32(enumValue),
        _ when int.TryParse(value.ToString(), out var parsed) => parsed,
        _ => 0
    };

    private static bool ToBool(object? value, bool fallback) => value switch
    {
        bool flag => flag,
        _ when bool.TryParse(value?.ToString(), out var parsed) => parsed,
        _ => fallback
    };
}
