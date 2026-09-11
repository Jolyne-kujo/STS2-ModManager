namespace ModTheSpire2.Bootstrap;

internal enum StartupAction
{
    Continue,
    SaveAndExit,
    Exit
}

internal sealed record StartupDialogResult(StartupAction Action, IReadOnlyList<RuntimeModOption> Options);

internal static class StartupSession
{
    public static void Run(
        IReadOnlyList<RuntimeModOption> options,
        Func<IReadOnlyList<RuntimeModOption>, StartupDialogResult?> show,
        Action<IReadOnlyList<RuntimeModOption>> save,
        Action<IReadOnlyList<RuntimeModOption>> load,
        Action exit,
        Action<Exception> reportSaveError)
    {
        while (true)
        {
            var result = show(options);
            // Null is reserved for a window creation failure, not a user-requested exit.
            if (result is null) return;
            if (result.Action == StartupAction.Exit)
            {
                exit();
                return;
            }

            options = result.Options;
            try
            {
                save(options);
            }
            catch (Exception exception)
            {
                // Keep the user's selection available for a retry; never load the game
                // or report a successful save-and-exit when persistence failed.
                reportSaveError(exception);
                continue;
            }

            if (result.Action == StartupAction.SaveAndExit) exit();
            else load(options);
            return;
        }
    }

    public static void ExitCurrentProcess()
    {
        RuntimeDiagnostics.Info("Startup selector requested exit; stopping before remaining mods initialize.");
        // SceneTree.Quit is deferred until the engine pumps again, which would allow
        // the native mod-loading loop to continue. Exit now, after synchronous saving.
        Environment.Exit(0);
    }
}
