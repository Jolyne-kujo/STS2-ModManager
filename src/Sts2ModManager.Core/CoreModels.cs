namespace Sts2ModManager.Core;

public sealed record ModEntry(
    string Id,
    string Name,
    string Author,
    string Version,
    string Description,
    string DirectoryPath,
    string ManifestPath,
    string Source,
    bool IsEnabled,
    IReadOnlyList<string> Dependencies)
{
    public string Key => $"{Id}::{Source}";

    public bool IsBootstrap => Id.Equals("ModTheSpire2", StringComparison.OrdinalIgnoreCase);

    public ModEntry WithEnabled(bool enabled) => this with { IsEnabled = IsBootstrap || enabled };
}

public enum ScanIssueSeverity
{
    Warning,
    Error
}

public sealed record ScanIssue(
    ScanIssueSeverity Severity,
    string Path,
    string Message);

public sealed record ModScanResult(
    IReadOnlyList<ModEntry> Mods,
    IReadOnlyList<ScanIssue> Issues);

public sealed record SavedModState(
    string Id,
    string Source,
    bool IsEnabled)
{
    public string Key => $"{Id}::{Source}";
}
