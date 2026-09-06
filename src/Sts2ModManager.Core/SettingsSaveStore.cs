using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sts2ModManager.Core;

public sealed class SettingsSaveStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public IReadOnlyList<SavedModState> Read(string path)
    {
        if (!File.Exists(path))
        {
            return Array.Empty<SavedModState>();
        }

        var root = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
                   ?? throw new InvalidDataException("settings.save root must be a JSON object.");
        var modSettings = root["mod_settings"] as JsonObject;
        var modList = modSettings?["mod_list"] as JsonArray;
        if (modList is null)
        {
            return Array.Empty<SavedModState>();
        }

        var states = new List<SavedModState>();
        foreach (var item in modList.OfType<JsonObject>())
        {
            var id = ReadString(item["id"]);
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            states.Add(new SavedModState(
                id,
                ReadSource(item["source"]),
                ReadBoolean(item["is_enabled"], defaultValue: true)));
        }

        return states;
    }

    public void Save(string path, IReadOnlyList<ModEntry> orderedMods)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        JsonObject root;
        if (File.Exists(path))
        {
            root = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
                   ?? throw new InvalidDataException("settings.save root must be a JSON object.");
            File.Copy(path, path + ".bak", overwrite: true);
        }
        else
        {
            root = new JsonObject();
        }

        var modSettings = root["mod_settings"] as JsonObject ?? new JsonObject();
        var modList = new JsonArray();
        foreach (var mod in orderedMods)
        {
            modList.Add(new JsonObject
            {
                ["id"] = mod.Id,
                ["is_enabled"] = mod.IsEnabled,
                ["source"] = string.IsNullOrWhiteSpace(mod.Source) ? "mods_directory" : mod.Source
            });
        }

        modSettings["mod_list"] = modList;
        modSettings["mods_enabled"] = orderedMods.Any(mod => mod.IsEnabled);
        root["mod_settings"] = modSettings;

        var tempPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(tempPath, root.ToJsonString(JsonOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (File.Exists(path))
            {
                File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static string ReadSource(JsonNode? node)
    {
        var source = ReadString(node);
        if (!string.IsNullOrWhiteSpace(source))
        {
            return source;
        }

        if (node is JsonValue value && value.TryGetValue<int>(out var numericSource))
        {
            return numericSource == 1 ? "workshop" : "mods_directory";
        }

        return "mods_directory";
    }

    private static bool ReadBoolean(JsonNode? node, bool defaultValue)
    {
        return node is JsonValue value && value.TryGetValue<bool>(out var boolean)
            ? boolean
            : defaultValue;
    }

    private static string? ReadString(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<string>(out var text))
        {
            return text;
        }

        return null;
    }
}
