using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sts2ModManager.Core;

public sealed class StartupProfileStore
{
    public const string FileName = "modtheSpire2.launch.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string Save(string settingsPath, IReadOnlyList<ModEntry> orderedMods)
    {
        var fullSettingsPath = Path.GetFullPath(settingsPath);
        var directory = Path.GetDirectoryName(fullSettingsPath)
                        ?? throw new InvalidDataException("The settings path has no parent directory.");
        Directory.CreateDirectory(directory);

        var profilePath = Path.Combine(directory, FileName);
        var profile = new JsonObject
        {
            ["schema_version"] = 1,
            ["settings_path"] = fullSettingsPath,
            ["updated_at_utc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["mods"] = new JsonArray(orderedMods.Select(mod => (JsonNode)new JsonObject
            {
                ["id"] = mod.Id,
                ["source"] = string.IsNullOrWhiteSpace(mod.Source) ? "mods_directory" : mod.Source,
                ["is_enabled"] = mod.IsEnabled
            }).ToArray())
        };

        var tempPath = profilePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(tempPath, profile.ToJsonString(JsonOptions), new UTF8Encoding(false));
            if (File.Exists(profilePath))
            {
                File.Replace(tempPath, profilePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, profilePath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        return profilePath;
    }
}
