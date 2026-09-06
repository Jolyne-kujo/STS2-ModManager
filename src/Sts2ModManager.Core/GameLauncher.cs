using System.Diagnostics;

namespace Sts2ModManager.Core;

public sealed class GameLauncher
{
    public const string SteamGameUri = "steam://rungameid/2868840";

    public ProcessStartInfo BuildStartInfo(string gameDirectory)
    {
        var fullDirectory = Path.GetFullPath(gameDirectory);
        var executable = Path.Combine(fullDirectory, "SlayTheSpire2.exe");
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("SlayTheSpire2.exe was not found in the selected directory.", executable);
        }

        return new ProcessStartInfo
        {
            // StS2's Windows build expects Steam to establish the game session.
            // Opening the URI also brings Steam up automatically when needed.
            FileName = SteamGameUri,
            WorkingDirectory = fullDirectory,
            UseShellExecute = true
        };
    }

    public Process Launch(string gameDirectory)
    {
        return Process.Start(BuildStartInfo(gameDirectory))
               ?? throw new InvalidOperationException("The game process could not be started.");
    }
}
