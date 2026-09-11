using System.Diagnostics;
using System.Reflection;
using ModTheSpire2.Bootstrap;

internal static class StartupSessionTests
{
    private static readonly RuntimeModOption[] Options =
    {
        new("a", "A", 1, 0, false, new object()),
        new("b", "B", 1, 0, true, new object())
    };

    public static void Run()
    {
        foreach (var action in Enum.GetValues<StartupAction>())
        {
            var calls = new List<string>();
            StartupSession.Run(Options, _ => new(action, Options),
                saved =>
                {
                    if (!ReferenceEquals(saved, Options)) throw new Exception("Wrong options saved");
                    calls.Add("save");
                }, _ => calls.Add("load"), () => calls.Add("exit"), _ => calls.Add("error"));
            var expected = action switch
            {
                StartupAction.Continue => new[] { "save", "load" },
                StartupAction.SaveAndExit => new[] { "save", "exit" },
                _ => new[] { "exit" }
            };
            if (!calls.SequenceEqual(expected)) throw new Exception($"Unexpected {action} sequence: {string.Join(",", calls)}");
        }

        var attempts = 0;
        var events = new List<string>();
        var edited = new RuntimeModOption[]
        {
            new("b", "B", 1, 0, false, new object()),
            new("a", "A", 1, 0, true, new object())
        };
        StartupSession.Run(Options, pending =>
        {
            if (!ReferenceEquals(pending, attempts == 0 ? Options : edited))
                throw new Exception("Edited selection lost on retry");
            return new(StartupAction.SaveAndExit, edited);
        }, saved =>
        {
            if (!ReferenceEquals(saved, edited)) throw new Exception("Original selection saved instead of edits");
            events.Add("save");
            if (attempts++ == 0) throw new IOException("disk unavailable");
        }, _ => events.Add("load"), () => events.Add("exit"), _ => events.Add("error"));
        if (!events.SequenceEqual(new[] { "save", "error", "save", "exit" }))
            throw new Exception("Save failure must reopen the selector without loading or exiting");
    }

    public static void RunExitFixture(string action, string path)
    {
        StartupSession.Run(Options, _ => new(Enum.Parse<StartupAction>(action), Options),
            _ => File.WriteAllText(Path.Combine(path, "saved"), "a:false,b:true"),
            _ => File.WriteAllText(Path.Combine(path, "loaded"), "unexpected"),
            StartupSession.ExitCurrentProcess,
            _ => File.WriteAllText(Path.Combine(path, "error"), "unexpected"));
        File.WriteAllText(Path.Combine(path, "returned"), "unexpected");
    }

    public static void VerifyProcessExit()
    {
        foreach (var action in new[] { StartupAction.Exit, StartupAction.SaveAndExit })
        {
            var directory = Directory.CreateTempSubdirectory("sts2-exit-tests-").FullName;
            try
            {
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                    start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
                start.ArgumentList.Add("--startup-exit-fixture");
                start.ArgumentList.Add(action.ToString());
                start.ArgumentList.Add(directory);
                using var child = Process.Start(start) ?? throw new Exception("Test subprocess did not start");
                if (!child.WaitForExit(10000))
                {
                    child.Kill();
                    throw new Exception("Exit request did not terminate the test subprocess");
                }
                if (child.ExitCode != 0 || File.Exists(Path.Combine(directory, "loaded")) || File.Exists(Path.Combine(directory, "returned")))
                    throw new Exception($"{action} unexpectedly continued past the exit request");
                var saved = Path.Combine(directory, "saved");
                if ((action == StartupAction.SaveAndExit) != File.Exists(saved))
                    throw new Exception($"{action} did not follow the requested save behavior");
                if (File.Exists(saved) && File.ReadAllText(saved) != "a:false,b:true")
                    throw new Exception("Saved data did not reach disk before process exit");
            }
            finally { Directory.Delete(directory, recursive: true); }
        }
    }
}
