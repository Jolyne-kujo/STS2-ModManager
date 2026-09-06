using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModTheSpire2.Bootstrap;

public sealed record BootstrapModEntry(string Id, string Source, bool IsEnabled);

public sealed record BootstrapLaunchProfile(
    string SettingsPath,
    IReadOnlyList<BootstrapModEntry> Mods);

public static class BootstrapProfileReader
{
    public const string FileName = "modtheSpire2.launch.json";

    public static BootstrapLaunchProfile Read(string profilePath)
    {
        var root = JsonNode.Parse(File.ReadAllText(profilePath))?.AsObject()
                   ?? throw new InvalidDataException("Launch profile root must be a JSON object.");
        var settingsPath = root["settings_path"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(settingsPath))
        {
            throw new InvalidDataException("Launch profile is missing settings_path.");
        }

        var mods = new List<BootstrapModEntry>();
        if (root["mods"] is JsonArray modArray)
        {
            foreach (var node in modArray.OfType<JsonObject>())
            {
                var id = node["id"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(id)) continue;

                mods.Add(new BootstrapModEntry(
                    id,
                    node["source"]?.GetValue<string>() ?? "mods_directory",
                    node["is_enabled"]?.GetValue<bool>() ?? true));
            }
        }

        return new BootstrapLaunchProfile(Path.GetFullPath(settingsPath), mods);
    }

    public static string? FindLatest(string appDataRoot)
    {
        var root = Path.Combine(appDataRoot, "SlayTheSpire2");
        if (!Directory.Exists(root)) return null;

        return Directory.EnumerateFiles(root, FileName, SearchOption.AllDirectories)
            .Select(path => new { Path = path, LastWrite = File.GetLastWriteTimeUtc(path) })
            .Where(candidate => HasExistingSettings(candidate.Path))
            .OrderByDescending(candidate => candidate.LastWrite)
            .Select(candidate => candidate.Path)
            .FirstOrDefault();
    }

    private static bool HasExistingSettings(string profilePath)
    {
        try
        {
            var settingsPath = JsonNode.Parse(File.ReadAllText(profilePath))?["settings_path"]?.GetValue<string>();
            return !string.IsNullOrWhiteSpace(settingsPath) && File.Exists(settingsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return false;
        }
    }
}
