using System.Text.Json;

namespace ModTheSpire2.Bootstrap;

internal static class WorkshopIconCache
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(6)
    };

    public static async Task<string?> GetAsync(string workshopId)
    {
        if (string.IsNullOrWhiteSpace(workshopId)) return null;

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SlayTheSpire2",
            "modtheSpire2-icons");
        Directory.CreateDirectory(directory);
        var cached = Path.Combine(directory, workshopId + ".img");
        if (File.Exists(cached) && new FileInfo(cached).Length > 0) return cached;

        try
        {
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["itemcount"] = "1",
                ["publishedfileids[0]"] = workshopId
            });
            using var response = await Client.PostAsync(
                "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/",
                form).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(responseStream).ConfigureAwait(false);
            var previewUrl = document.RootElement
                .GetProperty("response")
                .GetProperty("publishedfiledetails")[0]
                .GetProperty("preview_url")
                .GetString();
            if (string.IsNullOrWhiteSpace(previewUrl)) return null;

            var bytes = await Client.GetByteArrayAsync(previewUrl).ConfigureAwait(false);
            if (bytes.Length == 0) return null;
            await File.WriteAllBytesAsync(cached, bytes).ConfigureAwait(false);
            return cached;
        }
        catch (Exception exception)
        {
            RuntimeDiagnostics.Info("Workshop icon unavailable for " + workshopId + ": " + exception.Message);
            return null;
        }
    }
}
