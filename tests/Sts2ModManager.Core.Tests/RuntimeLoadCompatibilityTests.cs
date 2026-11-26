using ModTheSpire2.Bootstrap;

internal static class RuntimeLoadCompatibilityTests
{
    public static void PreservesUnavailableWorkshopSelections()
    {
        var previous = new RuntimeSavedSelection[]
        {
            new("ModTheSpire2", 1, true), new("remote", 2, false), new("local", 1, true)
        };
        var visible = new RuntimeSavedSelection[] { previous[0], new("local", 1, false) };
        var merged = RuntimeSelectionPolicy.Merge(visible, previous);
        if (!merged.SequenceEqual(new[] { previous[0], previous[1], visible[1] }))
            throw new Exception("Temporary Steam discovery failure deleted or reordered saved selections");
        var reordered = RuntimeSelectionPolicy.Merge(new[] { previous[2], previous[1], previous[0] }, previous);
        if (!reordered.SequenceEqual(new[] { previous[0], previous[2], previous[1] }))
            throw new Exception("Visible user order was not respected after bootstrap pinning");
    }

    public static void RestoresSourceSpecificSelectionOnce()
    {
        var settings = new FinalSettings();
        RuntimeModAccess.RememberSelection(settings, new RuntimeSavedSelection[]
        {
            new("same", 1, false), new("offline", 2, false), new("same", 2, true)
        });
        // Native finalization copies the first ID match's flag to both sources.
        settings.ModList = new() { new() { Id = "same", Source = Source.Local, IsEnabled = false },
            new() { Id = "same", Source = Source.Workshop, IsEnabled = false } };
        RuntimeModAccess.RestoreNativeSelection(settings);
        if (settings.ModList.Count != 3 || settings.ModList[0].IsEnabled || !settings.ModList[2].IsEnabled || settings.ModList[1].Id != "offline")
            throw new Exception("Native finalization lost a source-specific flag or unavailable mod");
        settings.ModList[2].IsEnabled = false;
        RuntimeModAccess.RestoreNativeSelection(settings);
        if (settings.ModList[2].IsEnabled) throw new Exception("Later in-game selection was overwritten");
    }

    private enum Source { Local = 1, Workshop = 2 }
    private sealed class SavedItem { public string Id { get; set; } = ""; public Source Source { get; set; } public bool IsEnabled { get; set; } }
    private sealed class FinalSettings { public List<SavedItem> ModList { get; set; } = new(); }

    public static void SynchronizesNativeStates()
    {
        var disabled = new ModFixture { state = State.None };
        var enabled = new ModFixture { state = State.Disabled };
        var duplicate = new ModFixture { state = State.DisabledDuplicate };
        var loaded = new ModFixture { state = State.Loaded };
        var failed = new ModFixture { state = State.Failed };
        StartupLoadGate.Configure(new[]
        {
            Option("disabled", disabled, false), Option("enabled", enabled, true),
            Option("duplicate", duplicate, true), Option("loaded", loaded, false), Option("failed", failed, true)
        });
        if (disabled.state != State.Disabled) throw new Exception("Unchecked mod still has the native loadable state");
        if (enabled.state != State.None) throw new Exception("Enabled mod retains disabled state");
        if (duplicate.state != State.None) throw new Exception("Duplicate selection was not reset for native arbitration");
        if (loaded.state != State.Loaded || failed.state != State.Failed)
            throw new Exception("Already loaded/failed mod state was overwritten");
    }

    internal static RuntimeModOption Option(string id, ModFixture mod, bool enabled = true) =>
        new(id, id, 2, (int)mod.state, enabled, mod);

    public static void UsesNativeDependencyOrder()
    {
        var dependent = Option("addon", new ModFixture());
        var prerequisite = Option("library", new ModFixture());
        NativeManager._mods = new() { dependent.RuntimeMod, prerequisite.RuntimeMod };
        var original = NativeManager._mods;
        using var enumerator = original.GetEnumerator();
        enumerator.MoveNext();
        NativeManager.Calls.Clear();
        NativeManager._circularDependencies["stale"] = "old selection";
        var result = RuntimeModAccess.PrepareLoadOrder(typeof(NativeManager), new[] { dependent, prerequisite });
        if (!NativeManager.Calls.SequenceEqual(new[] { "duplicates", "dependencies" }))
            throw new Exception("New native duplicate and dependency rules were not called in order");
        if (!result.SequenceEqual(new[] { prerequisite, dependent }))
            throw new Exception("Replay ignored native dependency ordering");
        if (ReferenceEquals(original, NativeManager._mods) || !enumerator.MoveNext())
            throw new Exception("Original native loader enumeration was modified");
        if (NativeManager._circularDependencies.Count != 0) throw new Exception("Stale dependency cycle errors were retained");
    }

    private static class NativeManager
    {
        public static List<object> _mods = new();
        public static IReadOnlyList<object> Mods => _mods;
        public static readonly List<string> Calls = new();
        public static readonly Settings _settings = new();
        public static readonly Dictionary<string, string> _circularDependencies = new();
        public static bool SimulateNativeStateOverwrite;
        private static void RemoveDisabledMods()
        {
            Calls.Add("duplicates");
            if (SimulateNativeStateOverwrite)
                foreach (ModFixture mod in _mods) mod.state = State.Disabled;
        }
        private static void SortModList(List<string> saved)
        {
            if (!ReferenceEquals(saved, _settings.ModList)) throw new Exception("Wrong settings order supplied");
            Calls.Add("dependencies");
            _mods = _mods.Where(mod => ((ModFixture)mod).state == State.None).Reverse()
                .Concat(_mods.Where(mod => ((ModFixture)mod).state != State.None)).ToList();
        }
    }

    public static void KeepsBootstrapFirstAfterNativeSort()
    {
        var bootstrap = Option("ModTheSpire2", new ModFixture { state = State.Loaded });
        var pending = Option("addon", new ModFixture());
        NativeManager._mods = new() { bootstrap.RuntimeMod, pending.RuntimeMod };
        var result = RuntimeModAccess.PrepareLoadOrder(typeof(NativeManager), new[] { bootstrap, pending });
        if (!ReferenceEquals(result[0], bootstrap) || !ReferenceEquals(NativeManager.Mods[0], bootstrap.RuntimeMod))
            throw new Exception("Native sort moved the already-loaded bootstrap behind other mods for next startup");
    }

    public static void KeepsHiddenBootstrapDuplicatesTogether()
    {
        var bootstrap = Option("ModTheSpire2", new ModFixture { state = State.Loaded });
        var pending = Option("addon", new ModFixture());
        var hidden = new ModFixture { state = State.DisabledDuplicate };
        hidden.manifest.id = "ModTheSpire2";
        NativeManager._mods = new() { bootstrap.RuntimeMod, pending.RuntimeMod, hidden };
        RuntimeModAccess.PrepareLoadOrder(typeof(NativeManager), new[] { bootstrap, pending });
        if (!ReferenceEquals(NativeManager.Mods[0], bootstrap.RuntimeMod) || !ReferenceEquals(NativeManager.Mods[1], hidden))
            throw new Exception("Hidden duplicate overwrites bootstrap priority by ID on next startup");
    }

    public static void NativeFilteringRetainsInitializedStates()
    {
        var loaded = Option("loaded", new ModFixture { state = State.Loaded }, false);
        var failed = Option("failed", new ModFixture { state = State.Failed }, false);
        NativeManager._mods = new() { loaded.RuntimeMod, failed.RuntimeMod };
        NativeManager.SimulateNativeStateOverwrite = true;
        try
        {
            RuntimeModAccess.PrepareLoadOrder(typeof(NativeManager), new[] { loaded, failed });
            if (((ModFixture)loaded.RuntimeMod).state != State.Loaded || ((ModFixture)failed.RuntimeMod).state != State.Failed)
                throw new Exception("Native duplicate filtering reset already initialized states");
        }
        finally { NativeManager.SimulateNativeStateOverwrite = false; }
    }

    private sealed class Settings { public List<string> ModList { get; } = new(); }

    // Values deliberately differ: compatibility code must use enum names, not old integers.
    internal enum State { None = 0, Loaded = 11, Failed = 12, Disabled = 13, DisabledDuplicate = 14 }
    internal sealed class ModFixture { public State state; public Manifest manifest = new(); }
    internal sealed class Manifest { public string id = ""; }
}
