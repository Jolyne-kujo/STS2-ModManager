using System.Text;

namespace ModTheSpire2.Bootstrap;

internal static class RuntimeDiagnostics
{
    private static readonly object Sync = new();

    private static string LogPath
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var directory = Path.Combine(appData, "SlayTheSpire2", "logs");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "modtheSpire2.log");
        }
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string message, Exception? exception = null)
    {
        Write("ERROR", exception is null ? message : message + Environment.NewLine + exception, exception);
    }

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (Sync)
            {
                var line = new StringBuilder()
                    .Append('[').Append(DateTime.Now.ToString("O")).Append("] [")
                    .Append(level).Append("] ").AppendLine(message)
                    .ToString();
                File.AppendAllText(LogPath, line, new UTF8Encoding(false));
            }
        }
        catch
        {
            // Diagnostics must never prevent StS2 from launching.
        }
    }
}
