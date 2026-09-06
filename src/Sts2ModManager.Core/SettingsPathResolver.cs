namespace Sts2ModManager.Core;

public sealed class SettingsPathResolver
{
    public string? FindExisting(string appDataRoot, string? preferredUserId = null)
    {
        var gameDataRoot = Path.Combine(appDataRoot, "SlayTheSpire2");
        if (!Directory.Exists(gameDataRoot))
        {
            return null;
        }

        IEnumerable<string> candidates;
        try
        {
            candidates = Directory.EnumerateFiles(gameDataRoot, "settings.save", SearchOption.AllDirectories)
                .Where(path => IsSupportedPlatformPath(path))
                .ToArray();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(preferredUserId))
        {
            var preferred = candidates.FirstOrDefault(path => ContainsPathSegment(path, preferredUserId));
            if (preferred is not null)
            {
                return preferred;
            }
        }

        return candidates
            .OrderByDescending(path => path.Contains("\\steam\\", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(path => File.GetLastWriteTimeUtc(path))
            .FirstOrDefault();
    }

    private static bool IsSupportedPlatformPath(string path)
    {
        return path.Contains("\\steam\\", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/steam/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("\\gog\\", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/gog/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("\\epic\\", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/epic/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("\\xbox\\", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/xbox/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("\\none\\", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/none/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsPathSegment(string path, string segment)
    {
        var normalizedPath = path.Replace('/', '\\').TrimEnd('\\');
        var marker = $"\\{segment.Replace('/', '\\').Trim('\\')}\\";
        return normalizedPath.Contains(marker, StringComparison.OrdinalIgnoreCase);
    }
}
