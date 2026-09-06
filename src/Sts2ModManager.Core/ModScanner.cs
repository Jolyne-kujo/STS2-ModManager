using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sts2ModManager.Core;

public sealed class ModScanner
{
    private const string DefaultSource = "mods_directory";

    public ModScanResult Scan(string modsDirectory)
    {
        var mods = new List<ModEntry>();
        var issues = new List<ScanIssue>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(modsDirectory))
        {
            issues.Add(new ScanIssue(
                ScanIssueSeverity.Warning,
                modsDirectory,
                "Mods directory does not exist."));
            return new ModScanResult(mods, issues);
        }

        foreach (var directory in Directory.EnumerateDirectories(modsDirectory).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var manifestPath = SelectManifest(directory);
            if (manifestPath is null)
            {
                continue;
            }

            TryAddManifest(manifestPath, directory, mods, issues, seen);
        }

        foreach (var manifestPath in Directory.EnumerateFiles(modsDirectory, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            TryAddManifest(manifestPath, modsDirectory, mods, issues, seen);
        }

        return new ModScanResult(mods, issues);
    }

    private static string? SelectManifest(string directory)
    {
        var preferred = Path.Combine(directory, "mod_manifest.json");
        if (File.Exists(preferred))
        {
            return preferred;
        }

        var directoryManifest = Path.Combine(directory, $"{Path.GetFileName(directory)}.json");
        if (File.Exists(directoryManifest))
        {
            return directoryManifest;
        }

        return Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static void TryAddManifest(
        string manifestPath,
        string modDirectory,
        ICollection<ModEntry> mods,
        ICollection<ScanIssue> issues,
        ISet<string> seen)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject();
            if (root is null)
            {
                throw new JsonException("Manifest root must be a JSON object.");
            }

            var id = ReadString(root["id"]);
            if (string.IsNullOrWhiteSpace(id))
            {
                issues.Add(new ScanIssue(
                    ScanIssueSeverity.Error,
                    manifestPath,
                    "Manifest is missing the required 'id' field."));
                return;
            }

            var source = ReadString(root["source"]);
            if (string.IsNullOrWhiteSpace(source))
            {
                source = DefaultSource;
            }

            var key = $"{id}::{source}";
            if (!seen.Add(key))
            {
                issues.Add(new ScanIssue(
                    ScanIssueSeverity.Warning,
                    manifestPath,
                    $"Duplicate manifest '{key}' was ignored."));
                return;
            }

            mods.Add(new ModEntry(
                id,
                ReadString(root["name"]) ?? id,
                ReadAuthor(root),
                ReadString(root["version"]) ?? "unknown",
                ReadString(root["description"]) ?? string.Empty,
                modDirectory,
                manifestPath,
                source,
                true,
                ReadDependencies(root["dependencies"])));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            issues.Add(new ScanIssue(
                ScanIssueSeverity.Error,
                manifestPath,
                $"Manifest could not be read: {ex.Message}"));
        }
    }

    private static string ReadAuthor(JsonObject root)
    {
        var author = ReadString(root["author"]);
        if (!string.IsNullOrWhiteSpace(author))
        {
            return author;
        }

        var authors = ReadStringArray(root["authors"]);
        if (authors.Count == 0)
        {
            authors = ReadStringArray(root["author_list"]);
        }

        return authors.Count == 0 ? "Unknown" : string.Join(", ", authors);
    }

    private static IReadOnlyList<string> ReadDependencies(JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            return Array.Empty<string>();
        }

        var dependencies = new List<string>();
        foreach (var item in array)
        {
            var dependency = item is JsonObject obj ? ReadString(obj["id"]) : ReadString(item);
            if (!string.IsNullOrWhiteSpace(dependency))
            {
                dependencies.Add(dependency);
            }
        }

        return dependencies;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            return Array.Empty<string>();
        }

        return array
            .Select(ReadString)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();
    }

    private static string? ReadString(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text))
            {
                return text;
            }

            if (value.TryGetValue<int>(out var integer))
            {
                return integer.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (value.TryGetValue<double>(out var number))
            {
                return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        return null;
    }
}
