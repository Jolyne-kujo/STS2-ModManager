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
                RuntimeModAccess.ResetDisabledState(option);
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
                ReplayRemaining();
                return false;
            }

            if (HandledKeys.Contains(key))
            {
                return false;
            }

            HandledKeys.Add(key);
            return false;
        }
    }

    private static void ReplayRemaining()
    {
        var tryLoadMethod = AccessTools.Method(
            AccessTools.TypeByName("MegaCrit.Sts2.Core.Modding.ModManager"),
            "TryLoadMod");
        if (tryLoadMethod is null)
        {
            RuntimeDiagnostics.Error("Could not find ModManager.TryLoadMod.");
            return;
        }

        _replaying = true;
        try
        {
            foreach (var option in _ordered)
            {
                if (HandledKeys.Contains(option.Key)) continue;
                HandledKeys.Add(option.Key);
                if (!option.IsEnabled && !option.IsBootstrap) continue;

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
