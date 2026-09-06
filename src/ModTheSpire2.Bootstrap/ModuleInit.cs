using System.Runtime.CompilerServices;

namespace ModTheSpire2.Bootstrap;

public static class ModuleInit
{
    [ModuleInitializer]
    public static void Initialize()
    {
        try
        {
            var options = RuntimeModAccess.ReadOptions()
                .OrderByDescending(option => option.IsBootstrap)
                .ToList();
            if (options.Count == 0) return;

            RuntimeDiagnostics.Info("Bootstrap module initialized. Showing startup selector for " + options.Count + " mods.");
            var selected = NativeStartupDialog.Show(options);
            if (selected is null)
            {
                RuntimeDiagnostics.Info("Startup selector cancelled; preserving the existing mod configuration.");
                return;
            }

            RuntimeModAccess.SaveSelection(selected);
            StartupLoadGate.Configure(selected);
            RuntimeDiagnostics.Info("Startup selector applied.");
        }
        catch (Exception exception)
        {
            RuntimeDiagnostics.Error("Bootstrap initialization failed; allowing the game to continue.", exception);
        }
    }
}
