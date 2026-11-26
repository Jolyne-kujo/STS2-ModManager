using System.Reflection;
using HarmonyLib;

namespace ModTheSpire2.Bootstrap;

internal static class StartupLoadGate
{
    private static readonly object Sync = new();
    private static readonly HashSet<string> HandledKeys = new(StringComparer.OrdinalIgnoreCase);
    private static List<RuntimeModOption> _ordered = new();
    private static bool _configured;
    private static bool _replayStarted;
    private static bool _replaying;
    internal static bool IsReplaying => _replaying;

    public static void Configure(IReadOnlyList<RuntimeModOption> ordered)
    {
        lock (Sync)
        {
            _ordered = ordered.ToList();
            _configured = true;
            _replayStarted = false;
            _replaying = false;
            HandledKeys.Clear();

            foreach (var option in _ordered)
            {
                RuntimeModAccess.SynchronizeLoadState(option);
            }

            var bootstrap = _ordered.FirstOrDefault(option => option.IsBootstrap);
            if (bootstrap is not null)
            {
                // The current TryLoadMod invocation is already loading the bootstrap.
                HandledKeys.Add(bootstrap.Key);
            }
        }

        RuntimeDiagnostics.Info("Startup load gate configured. order=" +
                                string.Join(" > ", ordered.Select(option => option.Key)));
    }

    public static bool Prefix(object __0)
    {
        lock (Sync)
        {
            if (!_configured || _replaying)
            {
                return true;
            }

            var key = RuntimeModAccess.GetKey(__0);
            if (!_replayStarted)
            {
                _replayStarted = true;
                return !ReplayRemaining();
            }

            if (HandledKeys.Contains(key))
            {
                return false;
            }

            // Workshop installations may be detected after initial startup. Those
            // entries were never part of the selection and belong to the native loader.
            return !_ordered.Any(option => option.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static bool ReplayRemaining()
    {
        var tryLoadMethod = AccessTools.Method(
            AccessTools.TypeByName("MegaCrit.Sts2.Core.Modding.ModManager"),
            "TryLoadMod");
        if (tryLoadMethod is null)
        {
            RuntimeDiagnostics.Error("Could not find ModManager.TryLoadMod.");
            _configured = false;
            return false;
        }

        try
        {
            _ordered = RuntimeModAccess.PrepareLoadOrder(RuntimeModAccess.ModManagerType, _ordered).ToList();
            RuntimeDiagnostics.Info("Effective native replay order=" + string.Join(" > ", _ordered.Select(option => option.Key)));
        }
        catch (Exception exception)
        {
            _configured = false;
            RuntimeDiagnostics.Error("Native load-order preparation failed; returning control to the game loader.", exception);
            return false;
        }

        _replaying = true;
        try
        {
            foreach (var option in _ordered)
            {
                if (HandledKeys.Contains(option.Key)) continue;
                HandledKeys.Add(option.Key);
                // Disabled entries still pass through the native method so the new
                // loader can emit OnModDetected without initializing their code.
                try
                {
                    tryLoadMethod.Invoke(null, new[] { option.RuntimeMod });
                }
                catch (TargetInvocationException exception)
                {
                    RuntimeDiagnostics.Error("Mod load replay failed for " + option.Key, exception.InnerException ?? exception);
                }
            }
        }
        finally
        {
            _replaying = false;
        }

        RuntimeDiagnostics.Info("Startup load gate finished replaying selected mods.");
        return true;
    }
}

[HarmonyPatch]
internal static class ModTheSpire2TryLoadModPatch
{
    private static MethodBase? TargetMethod()
    {
        var modManager = AccessTools.TypeByName("MegaCrit.Sts2.Core.Modding.ModManager");
        return modManager is null ? null : AccessTools.Method(modManager, "TryLoadMod");
    }

    private static bool Prefix(object __0) => StartupLoadGate.Prefix(__0);
}

[HarmonyPatch]
internal static class ModTheSpire2FinalSelectionPatch
{
    private static MethodBase? TargetMethod() => AccessTools.PropertySetter(
        AccessTools.TypeByName("MegaCrit.Sts2.Core.Modding.ModSettings"), "ModList");

    private static void Postfix(object __instance) => RuntimeModAccess.RestoreNativeSelection(__instance);
}
