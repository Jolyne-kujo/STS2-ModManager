using ModTheSpire2.Bootstrap;

internal static class RuntimePersistenceTests
{
    public static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("sts2-persistence-tests-").FullName;
        try
        {
            var path = Path.Combine(directory, "settings.save");
            const string correct = """
                {"mod_settings":{"mods_enabled":true,"mod_list":[
                    {"id":"local","source":"mods_directory","is_enabled":true},
                    {"id":"remote","source":"steam_workshop","is_enabled":false}]}}
                """;
            File.WriteAllText(path, correct.Replace("\"is_enabled\":true", "\"is_enabled\":false"));
            var game = new SaveManagerFixture(path);
            game._settingsSaveManager.Write = () => File.WriteAllText(Path.Combine(directory, "missing", "settings.save"), correct);
            // The real game catches disk-write exceptions and returns normally.
            // Exercise our production persistence boundary, not a throwing save delegate.
            var rejected = false;
            try { RuntimeModAccess.PersistSettings(game); }
            catch (Exception exception) when (exception.GetBaseException() is IOException) { rejected = true; }
            if (!rejected) throw new Exception("Silent game-save failure accepted stale disk selection");

            game._settingsSaveManager.Write = () => File.WriteAllText(path, correct);
            RuntimeModAccess.PersistSettings(game);
            if (File.ReadAllText(path) != correct) throw new Exception("Successful save did not reach disk before returning");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class SaveManagerFixture(string path)
    {
        public SettingsSaveManagerFixture _settingsSaveManager = new(path);
        public void SaveSettings()
        {
            try { _settingsSaveManager.SaveSettings(); }
            catch (Exception) { /* Matches the game's exception-swallowing wrapper. */ }
        }
    }

    private sealed class SettingsSaveManagerFixture(string path)
    {
        public Action Write { get; set; } = () => File.WriteAllText(path, "{}");
        public void SaveSettings() => Write();
    }
}
