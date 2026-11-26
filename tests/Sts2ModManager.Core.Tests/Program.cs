using System.Text.Json.Nodes;
using ModTheSpire2.Bootstrap;
using Sts2ModManager.Core;

if (args.Length == 3 && args[0] == "--startup-exit-fixture")
{
    StartupSessionTests.RunExitFixture(args[1], args[2]);
    return 42;
}

var failures = new List<string>();

Run("scanner reads StS2 manifest metadata and reports malformed manifests", () =>
{
    var root = CreateTempDirectory();
    try
    {
        var validDirectory = Directory.CreateDirectory(Path.Combine(root, "BetterMod")).FullName;
        File.WriteAllText(
            Path.Combine(validDirectory, "BetterMod.json"),
            """
            {
              "id": "better_mod",
              "name": "Better Mod",
              "author": "Mod Author",
              "version": "1.2.3",
              "description": "Adds a better run.",
              "dependencies": ["BaseLib"]
            }
            """);

        var brokenDirectory = Directory.CreateDirectory(Path.Combine(root, "BrokenMod")).FullName;
        File.WriteAllText(Path.Combine(brokenDirectory, "BrokenMod.json"), "{ invalid json");

        var result = new ModScanner().Scan(root);

        Assert(result.Mods.Count == 1, "one valid mod should be discovered");
        var mod = result.Mods[0];
        Assert(mod.Id == "better_mod", "manifest id should be read");
        Assert(mod.Name == "Better Mod", "manifest name should be read");
        Assert(mod.Author == "Mod Author", "manifest author should be read");
        Assert(mod.Version == "1.2.3", "manifest version should be read");
        Assert(mod.Dependencies.SequenceEqual(new[] { "BaseLib" }), "dependencies should be read");
        Assert(result.Issues.Any(issue => issue.Severity == ScanIssueSeverity.Error), "malformed manifest should be reported");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
});

Run("selection merge preserves saved order and enabled state while appending new mods", () =>
{
    var discovered = new[]
    {
        TestMod("first", "First"),
        TestMod("second", "Second"),
        TestMod("third", "Third")
    };
    var saved = new[]
    {
        new SavedModState("second", "mods_directory", false),
        new SavedModState("first", "mods_directory", true)
    };

    var merged = new ModSelectionService().Merge(discovered, saved);

    Assert(merged.Select(mod => mod.Id).SequenceEqual(new[] { "second", "first", "third" }), "saved order should win and new mods should append");
    Assert(!merged[0].IsEnabled, "saved disabled state should be retained");
    Assert(merged[2].IsEnabled, "new mods should default to enabled");
});

Run("selection merge preserves the saved mod source when the package is rediscovered", () =>
{
    var discovered = new[] { TestMod("workshop_mod", "Workshop Mod") };
    var saved = new[] { new SavedModState("workshop_mod", "workshop", true) };

    var merged = new ModSelectionService().Merge(discovered, saved);

    Assert(merged[0].Source == "workshop", "saved Workshop source should not be downgraded to a local source");
});

Run("settings writer preserves unrelated settings and writes StS2 mod fields", () =>
{
    var root = CreateTempDirectory();
    try
    {
        var path = Path.Combine(root, "settings.save");
        File.WriteAllText(
            path,
            """
            {
              "volume": 0.7,
              "unknown_settings": {"keep": [1, 2, 3]},
              "mod_settings": {
                "mods_enabled": false,
                "mod_list": [{"id":"old_mod","is_enabled":true,"source":"mods_directory"}]
              }
            }
            """);

        var mods = new[]
        {
            TestMod("base_lib", "BaseLib", enabled: false),
            TestMod("new_mod", "New Mod", enabled: true)
        };
        var store = new SettingsSaveStore();
        store.Save(path, mods);

        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert(document["volume"]!.GetValue<double>() == 0.7, "unrelated settings should be preserved");
        Assert(document["unknown_settings"]!["keep"]!.AsArray().Count == 3, "unknown nested settings should be preserved");
        var modSettings = document["mod_settings"]!.AsObject();
        Assert(modSettings["mods_enabled"]!.GetValue<bool>(), "mods_enabled should reflect selected mods");
        var modList = modSettings["mod_list"]!.AsArray();
        Assert(modList.Count == 2, "mod_list should contain the visible mods");
        Assert(modList[0]!["id"]!.GetValue<string>() == "base_lib", "mod order should be persisted");
        Assert(!modList[0]!["is_enabled"]!.GetValue<bool>(), "disabled state should be persisted");
        Assert(modList[1]!["source"]!.GetValue<string>() == "mods_directory", "source should be persisted");
        Assert(File.Exists(path + ".bak"), "existing settings should be backed up");

        var states = store.Read(path);
        Assert(states.Select(state => state.Id).SequenceEqual(new[] { "base_lib", "new_mod" }), "saved state should round-trip in order");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
});

Run("settings path resolver prefers the requested Steam account", () =>
{
    var root = CreateTempDirectory();
    try
    {
        var first = Directory.CreateDirectory(Path.Combine(root, "SlayTheSpire2", "steam", "111")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(root, "SlayTheSpire2", "steam", "222")).FullName;
        File.WriteAllText(Path.Combine(first, "settings.save"), "{}");
        File.WriteAllText(Path.Combine(second, "settings.save"), "{}");

        var resolved = new SettingsPathResolver().FindExisting(root, "222");

        Assert(resolved == Path.Combine(second, "settings.save"), "requested Steam account should be selected");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
});

Run("game launcher starts StS2 through Steam from the selected game directory", () =>
{
    var root = CreateTempDirectory();
    try
    {
        var executable = Path.Combine(root, "SlayTheSpire2.exe");
        File.WriteAllBytes(executable, Array.Empty<byte>());

        var startInfo = new GameLauncher().BuildStartInfo(root);

        Assert(startInfo.FileName == "steam://rungameid/2868840", "launcher should target the StS2 Steam URI");
        Assert(startInfo.Arguments.Length == 0, "Steam URI should be passed as the process file name");
        Assert(startInfo.WorkingDirectory == root, "launcher should use the game directory as working directory");
        Assert(startInfo.UseShellExecute, "launcher should ask Windows to open the Steam URI");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
});

Run("mod order operation moves an item to the requested insertion point", () =>
{
    var movedToEnd = ModOrderService.MoveToInsertionIndex(
        new[] { "first", "second", "third", "fourth" },
        fromIndex: 0,
        insertionIndex: 4);
    var movedBeforeSecond = ModOrderService.MoveToInsertionIndex(
        new[] { "first", "second", "third", "fourth" },
        fromIndex: 3,
        insertionIndex: 1);

    Assert(movedToEnd.SequenceEqual(new[] { "second", "third", "fourth", "first" }), "moving to the end should append the item");
    Assert(movedBeforeSecond.SequenceEqual(new[] { "first", "fourth", "second", "third" }), "moving before an item should preserve the remaining order");
});

Run("runtime startup order preserves enabled order and sorts disabled mods deterministically", () =>
{
    var mods = new[]
    {
        new RuntimeModOption("disabled_z", "Zeta", 2, 0, false, new object()),
        new RuntimeModOption("enabled_second", "Second", 2, 0, true, new object()),
        new RuntimeModOption("disabled_a", "Alpha", 2, 0, false, new object()),
        new RuntimeModOption("enabled_first", "First", 2, 0, true, new object())
    };
    var savedOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["enabled_first::2"] = 0,
        ["enabled_second::2"] = 1,
        ["disabled_z::2"] = 2,
        ["disabled_a::2"] = 3
    };

    var ordered = RuntimeModOrderPolicy.Normalize(mods, savedOrder);

    Assert(ordered.Select(mod => mod.Id).SequenceEqual(new[]
    {
        "enabled_first", "enabled_second", "disabled_a", "disabled_z"
    }), "enabled mods should retain saved order and disabled mods should use deterministic name order");
});

Run("bootstrap is inert when loaded outside the StS2 runtime", () =>
{
    var options = RuntimeModAccess.ReadOptions();
    Assert(options.Count == 0, "the bootstrap should not throw when StS2 types are unavailable");
});

Run("startup profile is written beside the selected settings file", () =>
{
    var root = CreateTempDirectory();
    try
    {
        var settingsPath = Path.Combine(root, "steam", "123", "settings.save");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, "{}");

        var mods = new[]
        {
            TestMod("first", "First", enabled: true),
            TestMod("second", "Second", enabled: false)
        };

        var profileStore = new StartupProfileStore();
        var profilePath = profileStore.Save(settingsPath, mods);
        var profile = JsonNode.Parse(File.ReadAllText(profilePath))!.AsObject();

        Assert(profilePath == Path.Combine(root, "steam", "123", StartupProfileStore.FileName), "profile should use the settings account directory");
        Assert(profile["settings_path"]!.GetValue<string>() == Path.GetFullPath(settingsPath), "profile should identify its settings file");
        var entries = profile["mods"]!.AsArray();
        Assert(entries.Count == 2, "profile should contain all selected mods");
        Assert(entries[0]! ["id"]!.GetValue<string>() == "first", "profile should preserve load order");
        Assert(!entries[1]! ["is_enabled"]!.GetValue<bool>(), "profile should preserve disabled state");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
});

Run("the bootstrap mod cannot be disabled by a saved profile or the UI", () =>
{
    var bootstrap = TestMod("ModTheSpire2", "ModTheSpire 2 Bootstrap", enabled: true);
    var saved = new[] { new SavedModState("ModTheSpire2", "mods_directory", false) };

    var merged = new ModSelectionService().Merge(new[] { bootstrap }, saved);
    Assert(merged[0].IsEnabled, "the bootstrap must remain enabled when an old profile disabled it");
    Assert(merged[0].WithEnabled(false).IsEnabled, "the UI must not be able to disable the bootstrap");
});

Run("bootstrap applies the launch profile before the game reads mod settings", () =>
{
    var root = CreateTempDirectory();
    try
    {
        var settingsPath = Path.Combine(root, "settings.save");
        File.WriteAllText(settingsPath, """
        {
          "volume": 0.5,
          "mod_settings": {
            "mods_enabled": false,
            "mod_list": [{"id":"old","source":"mods_directory","is_enabled":true}]
          }
        }
        """);

        var profile = new BootstrapLaunchProfile(
            settingsPath,
            new[]
            {
                new BootstrapModEntry("second", "mods_directory", false),
                new BootstrapModEntry("first", "workshop", true)
            });

        Assert(BootstrapSettingsApplier.TryApply(profile, out var error), $"bootstrap should apply profile: {error}");
        var document = JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject();
        var modSettings = document["mod_settings"]!.AsObject();
        var list = modSettings["mod_list"]!.AsArray();
        Assert(document["volume"]!.GetValue<double>() == 0.5, "bootstrap should preserve unrelated settings");
        Assert(list[0]! ["id"]!.GetValue<string>() == "second", "bootstrap should write profile order");
        Assert(!list[0]! ["is_enabled"]!.GetValue<bool>(), "bootstrap should write disabled state");
        Assert(list[1]! ["source"]!.GetValue<string>() == "workshop", "bootstrap should write source");
        Assert(modSettings["mods_enabled"]!.GetValue<bool>(), "bootstrap should recompute mods_enabled");
        Assert(File.Exists(settingsPath + ".bootstrap.bak"), "bootstrap should keep a recoverable backup");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
});

Run("Godot PCK writer emits a StS2-compatible version 3 pack directory", () =>
{
    var root = CreateTempDirectory();
    try
    {
        var pckPath = Path.Combine(root, "test.pck");
        GodotPckWriter.Write(
            pckPath,
            new Dictionary<string, byte[]>
            {
                ["project.godot"] = "config_version=5\n"u8.ToArray(),
                ["readme.txt"] = "bootstrap"u8.ToArray()
            });

        using var stream = File.OpenRead(pckPath);
        using var reader = new BinaryReader(stream);
        Assert(reader.ReadUInt32() == 0x43504447, "PCK should start with GDPC magic");
        Assert(reader.ReadUInt32() == 3, "PCK should use the StS2-compatible pack format");
        Assert(reader.ReadUInt32() == 4, "PCK should declare Godot 4 major version");
        _ = reader.ReadUInt32();
        _ = reader.ReadUInt32();
        Assert(reader.ReadUInt32() == 2, "PCK should use Godot's relative file-base flag");
        var fileBase = reader.ReadUInt64();
        var directoryOffset = reader.ReadUInt64();
        stream.Position = (long)directoryOffset;
        Assert(reader.ReadUInt32() == 2, "PCK directory should contain both files");
        Assert(fileBase % 32 == 0, "PCK file data should be aligned");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
});

Run("startup actions save before continuing or exiting, and retry failed saves", StartupSessionTests.Run);
Run("startup exit terminates the process without continuing game loading", StartupSessionTests.VerifyProcessExit);
Run("runtime persistence propagates actual disk failures and completes writes synchronously", RuntimePersistenceTests.Run);
Run("runtime loading synchronizes selected states without resetting initialized mods", RuntimeLoadCompatibilityTests.SynchronizesNativeStates);
Run("runtime replay respects native dependency and duplicate rules", RuntimeLoadCompatibilityTests.UsesNativeDependencyOrder);
Run("native dependency sorting keeps bootstrap first for the next startup", RuntimeLoadCompatibilityTests.KeepsBootstrapFirstAfterNativeSort);
Run("hidden bootstrap duplicates cannot overwrite startup priority", RuntimeLoadCompatibilityTests.KeepsHiddenBootstrapDuplicatesTogether);
Run("temporarily undiscovered workshop mods retain their saved flags and relative positions", RuntimeLoadCompatibilityTests.PreservesUnavailableWorkshopSelections);
Run("native finalization preserves source-specific flags without overriding later edits", RuntimeLoadCompatibilityTests.RestoresSourceSpecificSelectionOnce);
Run("native filtering cannot reset already initialized states", RuntimeLoadCompatibilityTests.NativeFilteringRetainsInitializedStates);

if (args.Contains("--native-ui"))
{
    Run("native selector shows full descriptions and scrolls the hovered pane", NativeDialogTests.Run);
    Run("native save-and-exit captures checkbox and order edits", NativeDialogTests.SaveAndExit);
    Run("native apply continues with checkbox and order edits", NativeDialogTests.Apply);
}

if (failures.Count > 0)
{
    Console.Error.WriteLine($"FAIL: {failures.Count} test(s)");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($" - {failure}");
    }

    return 1;
}

Console.WriteLine("PASS: all tests");
return 0;

void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
    }
}

static ModEntry TestMod(string id, string name, bool enabled = true) => new(
    id,
    name,
    "Test Author",
    "1.0.0",
    "",
    "",
    "",
    "mods_directory",
    enabled,
    Array.Empty<string>());

static string CreateTempDirectory() => Directory.CreateTempSubdirectory("sts2-mod-manager-tests-").FullName;

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
