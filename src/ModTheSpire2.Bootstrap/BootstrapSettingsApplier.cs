using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModTheSpire2.Bootstrap;

public static class BootstrapSettingsApplier
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static bool TryApply(BootstrapLaunchProfile profile, out string error)
    {
        error = string.Empty;
        try
        {
            if (profile.Mods.Count == 0)
            {
                return false;
            }

            var settingsPath = Path.GetFullPath(profile.SettingsPath);
            if (!File.Exists(settingsPath))
            {
                error = $"settings.save was not found: {settingsPath}";
                return false;
            }

            var root = JsonNode.Parse(File.ReadAllText(settingsPath))?.AsObject()
                       ?? throw new InvalidDataException("settings.save root must be a JSON object.");
            var modSettings = root["mod_settings"] as JsonObject ?? new JsonObject();
            var modList = new JsonArray();
            foreach (var mod in profile.Mods)
            {
                if (string.IsNullOrWhiteSpace(mod.Id)) continue;
                modList.Add(new JsonObject
                {
                    ["id"] = mod.Id,
                    ["source"] = string.IsNullOrWhiteSpace(mod.Source) ? "mods_directory" : mod.Source,
                    ["is_enabled"] = mod.IsEnabled
                });
            }

            modSettings["mod_list"] = modList;
            modSettings["mods_enabled"] = profile.Mods.Any(mod => mod.IsEnabled);
            root["mod_settings"] = modSettings;

            var updated = root.ToJsonString(JsonOptions);
            var current = File.ReadAllText(settingsPath);
            if (string.Equals(current, updated, StringComparison.Ordinal))
            {
                return true;
            }

            File.Copy(settingsPath, settingsPath + ".bootstrap.bak", overwrite: true);
            var tempPath = settingsPath + ".bootstrap.tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(tempPath, updated, new UTF8Encoding(false));
                File.Replace(tempPath, settingsPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException)
        {
            error = ex.Message;
            return false;
        }
    }
}
