using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ModTheSpire2.Bootstrap;

internal static class NativeDialogTests
{
    // These tests send messages only to controls owned by this test process.
    public static void Run() => CheckDialog(StartupAction.Exit);
    public static void SaveAndExit() => CheckDialog(StartupAction.SaveAndExit);
    public static void Apply() => CheckDialog(StartupAction.Continue);

    private static void CheckDialog(StartupAction action)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Native UI tests need Windows.");
        var description = string.Join("\n", Enumerable.Range(1, 240)
            .Select(i => $"第 {i} 行：完整的模组描述，包含中文和 English。{new string('x', 180)}")) + "\n描述结束-END";
        var mods = Enumerable.Range(0, 120).Select(i => new RuntimeModOption(
            $"mod-{i}", $"测试模组 {i}", 1, 0, true, new object(), "Test Author", "1.0", description)).ToArray();
        IntPtr window = IntPtr.Zero;
        var inspector = Task.Run(() =>
        {
            try
            {
                WaitUntil(() => (window = FindOwnWindow()) != IntPtr.Zero, "selector window did not open");
                var list = IntPtr.Zero;
                var details = IntPtr.Zero;
                WaitUntil(() => (list = GetDlgItem(window, 2001)) != IntPtr.Zero &&
                                (details = GetDlgItem(window, 2002)) != IntPtr.Zero, "selector controls missing");
                // Wait for initial content, as controls are populated after they are created.
                WaitUntil(() => ReadText(details).Contains("描述结束-END"), "long description was truncated");
                var className = new StringBuilder(80);
                GetClassName(details, className, className.Capacity);
                Check(className.ToString().Equals("Edit", StringComparison.OrdinalIgnoreCase),
                    "details must be a scrollable text control, not a static label");
                Check(GetDlgItem(window, 2003) == IntPtr.Zero, "old image panel should be removed");
                GetWindowRect(list, out var listRect);
                GetWindowRect(details, out var detailsRect);
                Check(detailsRect.Bottom >= listRect.Bottom - 8, "details must occupy the former image region");
                Check(ReadText(window).Contains("启动前模组管理"), "Unicode window text is truncated");
                var text = ReadText(details);
                Check(!text.Replace("\r\n", "").Contains('\n'), "LF-only description lines must be normalized");

                // Deliver the wheel to the opposite pane, with the pointer over the intended pane.
                // This reproduces Windows sending wheel input to the previously focused control.
                var initialListTop = SendMessage(list, 0x1027, 0, 0).ToInt32(); // LVM_GETTOPINDEX
                Wheel(list, detailsRect, -120);
                WaitUntil(() => FirstLine(details) > 0, "wheel over details did not scroll down");
                var detailTop = FirstLine(details);
                Check(SendMessage(list, 0x1027, 0, 0).ToInt32() == initialListTop, "wheel scrolled the wrong list");
                Wheel(list, detailsRect, 120);
                WaitUntil(() => FirstLine(details) < detailTop, "details did not scroll back up");

                detailTop = FirstLine(details);
                Wheel(details, listRect, -120);
                WaitUntil(() => SendMessage(list, 0x1027, 0, 0).ToInt32() > initialListTop, "list did not scroll down");
                var listTop = SendMessage(list, 0x1027, 0, 0).ToInt32();
                Check(FirstLine(details) == detailTop, "list wheel changed description scroll position");
                Wheel(details, listRect, 120);
                WaitUntil(() => SendMessage(list, 0x1027, 0, 0).ToInt32() < listTop, "list did not scroll back up");

                Wheel(list, detailsRect, -120);
                WaitUntil(() => FirstLine(details) > 0, "details did not scroll for selection test");
                var item = new LvState { State = 2, StateMask = 2 };
                var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<LvState>());
                try
                {
                    Marshal.StructureToPtr(item, pointer, false);
                    SendMessage(list, 0x102B, 1, pointer); // LVM_SETITEMSTATE (same process)
                }
                finally { Marshal.FreeHGlobal(pointer); }
                WaitUntil(() => ReadText(details).StartsWith("名称：测试模组 1\r\n"), "selecting another mod did not update details");
                Check(FirstLine(details) == 0, "new mod description should start at the top");
                SendMessage(details, 0x0102, 'Q', 0); // WM_CHAR must not edit read-only metadata
                Check(ReadText(details).StartsWith("名称："), "description must be read-only");
                Check(ReadText(GetDlgItem(window, 1005)) == "保存并退出", "exit button has the wrong label");
                SendMessage(GetDlgItem(window, 1003), 0x00F5, 0, 0); // disable selected test mod
                SendMessage(GetDlgItem(window, 1001), 0x00F5, 0, 0); // move it to the first row
            }
            finally
            {
                if (window != IntPtr.Zero)
                {
                    if (action == StartupAction.Exit) PostMessage(window, 0x0010, 0, 0);
                    else PostMessage(GetDlgItem(window, action == StartupAction.SaveAndExit ? 1005 : 1004), 0x00F5, 0, 0);
                }
            }
        });

        var selected = NativeStartupDialog.Show(mods);
        inspector.GetAwaiter().GetResult();
        Check(selected?.Action == action, "window returned the wrong startup action");
        if (action != StartupAction.Exit)
        {
            Check(selected!.Options[0].Id == "mod-1" && !selected.Options[0].IsEnabled,
                "save should capture the changed order and checkbox state");
        }
    }

    private static void Wheel(IntPtr recipient, Rect hovered, int delta)
    {
        var x = (hovered.Left + hovered.Right) / 2;
        var y = (hovered.Top + hovered.Bottom) / 2;
        PostMessage(recipient, 0x020A, new IntPtr(delta << 16), new IntPtr((y << 16) | (x & 0xFFFF)));
    }

    private static int FirstLine(IntPtr edit) => SendMessage(edit, 0x00CE, 0, 0).ToInt32();

    private static string ReadText(IntPtr window)
    {
        var text = new StringBuilder(GetWindowTextLength(window) + 1);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }

    private static IntPtr FindOwnWindow()
    {
        var found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var processId);
            if (processId != Environment.ProcessId) return true;
            var name = new StringBuilder(80);
            GetClassName(h, name, name.Capacity);
            if (name.ToString() != "ModTheSpire2StartupSelector") return true;
            found = h;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static void WaitUntil(Func<bool> condition, string error)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(5)) throw new InvalidOperationException(error);
            Thread.Sleep(20);
        }
    }

    private static void Check(bool value, string error)
    {
        if (!value) throw new InvalidOperationException(error);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct LvState
    {
        public uint Mask; public int Item, SubItem; public uint State, StateMask;
        public IntPtr Text; public int TextLength, Image; public IntPtr Param;
        public int Indent, GroupId; public uint Columns; public IntPtr ColumnData, ColumnFormat; public int Group;
    }
    private delegate bool EnumWindowCallback(IntPtr window, IntPtr param);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr param);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);
    [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr parent, int id);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
