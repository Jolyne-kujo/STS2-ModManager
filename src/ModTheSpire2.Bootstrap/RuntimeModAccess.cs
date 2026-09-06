using System.Collections;
using System.Reflection;

namespace ModTheSpire2.Bootstrap;

internal sealed class RuntimeModOption
{
    public RuntimeModOption(string id, string name, int source, int loadState, bool isEnabled, object runtimeMod)
    {
        Id = id;
        Name = string.IsNullOrWhiteSpace(name) ? id : name;
        Source = source;
        LoadState = loadState;
        IsEnabled = isEnabled || IsBootstrap;
        RuntimeMod = runtimeMod;
    }

    public string Id { get; }
    public string Name { get; }
    public int Source { get; }
    public int LoadState { get; set; }
    public bool IsEnabled { get; set; }
    public object RuntimeMod { get; }
    public bool IsBootstrap => Id.Equals("ModTheSpire2", StringComparison.OrdinalIgnoreCase);
    public string Key => Id + "::" + Source;
}

internal static class RuntimeModAccess
{
    private const string ModManagerName = "MegaCrit.Sts2.Core.Modding.ModManager";
    private const string SaveManagerName = "MegaCrit.Sts2.Core.Saves.SaveManager";
    private const string SettingsSaveModName = "MegaCrit.Sts2.Core.Modding.SettingsSaveMod";
    private const string ModSourceName = "MegaCrit.Sts2.Core.Modding.ModSource";

    public static IReadOnlyList<RuntimeModOption> ReadOptions()
    {
        var modManager = FindType(ModManagerName)
                         ?? throw new InvalidOperationException("StS2 ModManager type was not found.");
        var mods = GetMember(modManager, "Mods") as IEnumerable
                   ?? throw new InvalidOperationException("StS2 ModManager.M mods were not available.");

        var enabledByKey = ReadEnabledByKey();
        var result = new List<RuntimeModOption>();
        foreach (var runtimeMod in mods)
        {
            var manifest = GetMember(runtimeMod, "manifest");
            var id = AsString(GetMember(manifest, "id"));
            if (string.IsNullOrWhiteSpace(id)) continue;

            var source = ToInt(GetMember(runtimeMod, "modSource"));
            var key = BuildKey(id, source);
            var state = ToInt(GetMember(runtimeMod, "state"));
            result.Add(new RuntimeModOption(
                id,
                AsString(GetMember(manifest, "name")) ?? id,
                source,
                state,
                enabledByKey.GetValueOrDefault(key, state != 3),
                runtimeMod));
        }

        return result;
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
        var sourceType = FindType(ModSourceName)
                         ?? throw new InvalidOperationException("ModSource type was not found.");
        var listType = typeof(List<>).MakeGenericType(settingsModType);
        var list = (IList)(Activator.CreateInstance(listType)
                            ?? throw new InvalidOperationException("Mod settings list could not be created."));

        foreach (var option in orderedOptions)
        {
            var item = Activator.CreateInstance(settingsModType);
            if (item is null) continue;

            SetMember(item, "Id", option.Id);
            SetMember(item, "Source", Enum.ToObject(sourceType, option.Source));
            SetMember(item, "IsEnabled", option.IsEnabled || option.IsBootstrap);
            list.Add(item);
        }

        SetMember(modSettings, "ModList", list);
        SetMember(modSettings, "PlayerAgreedToModLoading", true);

        // ModManager caches the same settings object in a private static field. Keep both
        // the in-memory state and the on-disk save synchronized for this launch.
        var modManagerType = FindType(ModManagerName);
        FindField(modManagerType, "_settings")?.SetValue(null, modSettings);

        var saveSettings = FindMethod(saveManagerType, "SaveSettings", Type.EmptyTypes)
                           ?? throw new InvalidOperationException("SaveManager.SaveSettings() was not found.");
        saveSettings.Invoke(saveManager, null);
    }

    public static void ResetDisabledState(RuntimeModOption option)
    {
        // ModManager.RemoveDisabledMods marks disabled entries with ModLoadState.Disabled.
        // Re-enable them before the load gate invokes TryLoadMod for this launch.
        if (!option.IsEnabled || option.LoadState != 3) return;

        var stateField = FindField(option.RuntimeMod.GetType(), "state")
                         ?? throw new InvalidOperationException("ModLoadState field was not found.");
        stateField.SetValue(option.RuntimeMod, Enum.ToObject(stateField.FieldType, 0));
        option.LoadState = 0;
    }

    public static string GetKey(object runtimeMod)
    {
        var manifest = GetMember(runtimeMod, "manifest");
        return BuildKey(AsString(GetMember(manifest, "id")) ?? string.Empty, ToInt(GetMember(runtimeMod, "modSource")));
    }

    public static string BuildKey(string id, int source) => id + "::" + source;

    private static Dictionary<string, bool> ReadEnabledByKey()
    {
        var values = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var saveManagerType = FindType(SaveManagerName);
            var saveManager = saveManagerType is null ? null : FindProperty(saveManagerType, "Instance")?.GetValue(null);
            var settingsSave = GetMember(saveManager, "SettingsSave");
            var modSettings = GetMember(settingsSave, "ModSettings");
            if (GetMember(modSettings, "ModList") is not IEnumerable list) return values;

            foreach (var item in list)
            {
                var id = AsString(GetMember(item, "Id"));
                if (string.IsNullOrWhiteSpace(id)) continue;
                values[BuildKey(id, ToInt(GetMember(item, "Source")))] = ToBool(GetMember(item, "IsEnabled"), true);
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
