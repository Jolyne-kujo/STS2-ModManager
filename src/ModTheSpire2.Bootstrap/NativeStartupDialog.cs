using System.Runtime.InteropServices;

namespace ModTheSpire2.Bootstrap;

internal static class NativeStartupDialog
{
    private const uint WsOverlappedWindow = 0x00CF0000;
    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsVScroll = 0x00200000;
    private const uint WsTabStop = 0x00010000;
    private const uint WsExClientEdge = 0x00000200;
    private const uint WsExTopmost = 0x00000008;
    private const int SwShow = 5;

    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;
    private const uint WmCommand = 0x0111;
    private const uint WmNotify = 0x004E;
    private const uint WmMouseWheel = 0x020A;
    private const uint BnClicked = 0;

    private const uint LvmFirst = 0x1000;
    private const uint LvmSetExtendedListViewStyle = LvmFirst + 54;
    private const uint LvmInsertColumnW = LvmFirst + 97;
    private const uint LvmInsertItemW = LvmFirst + 77;
    private const uint LvmDeleteAllItems = LvmFirst + 9;
    private const uint LvmSetItemState = LvmFirst + 43;
    private const uint LvmGetItemState = LvmFirst + 44;
    private const uint LvmGetNextItem = LvmFirst + 12;

    private const uint LvsReport = 0x0001;
    private const uint LvsSingleSelect = 0x0004;
    private const uint LvsShowSelAlways = 0x0008;
    private const uint LvsExGridLines = 0x00000001;
    private const uint LvsExFullRowSelect = 0x00000020;
    private const uint LvsExCheckBoxes = 0x00000004;
    private const uint LvifText = 0x0001;
    private const uint LvisStateImageMask = 0xF000;
    private const uint LvniSelected = 0x0002;
    private const int LvnItemChanged = -101;

    private const uint EsMultiline = 0x0004;
    private const uint EsAutoVScroll = 0x0040;
    private const uint EsReadOnly = 0x0800;
    private const uint EmSetSel = 0x00B1;
    private const uint EmScrollCaret = 0x00B7;

    private const int ListControl = 2001;
    private const int DetailsControl = 2002;
    private const int ButtonUp = 1001;
    private const int ButtonDown = 1002;
    private const int ButtonToggle = 1003;
    private const int ButtonApply = 1004;
    private const int ButtonCancel = 1005;

    private static readonly object Sync = new();
    private static readonly Dictionary<IntPtr, DialogState> States = new();
    private static readonly WndProcDelegate WindowProc = WndProc;
    private static readonly string WindowClassName = "ModTheSpire2StartupSelector";

    public static IReadOnlyList<RuntimeModOption>? Show(IReadOnlyList<RuntimeModOption> options)
    {
        // The selector is intentionally modal on the game loading thread. Its own
        // Win32 message loop keeps the window responsive while ModManager is paused.
        return Run(options);
    }

    private static IReadOnlyList<RuntimeModOption>? Run(IReadOnlyList<RuntimeModOption> options)
    {
        try
        {
            RuntimeDiagnostics.Info("Startup selector message loop started.");
            InitCommonControls();
            RuntimeDiagnostics.Info("Common controls initialized.");
            var instance = GetModuleHandle(null);
            var windowClass = new WndClassEx
            {
                Size = (uint)Marshal.SizeOf<WndClassEx>(),
                Style = 0,
                WindowProc = WindowProc,
                Instance = instance,
                ClassName = WindowClassName
            };
            RegisterClassEx(ref windowClass);
            RuntimeDiagnostics.Info("Startup selector window class registered.");

            var hwnd = CreateWindowEx(
                WsExTopmost,
                WindowClassName,
                "ModTheSpire 2 - 启动前模组管理",
                WsOverlappedWindow,
                0,
                0,
                1200,
                700,
                IntPtr.Zero,
                IntPtr.Zero,
                instance,
                IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
            {
                RuntimeDiagnostics.Error("Startup selector CreateWindowEx returned NULL. Win32 error=" + Marshal.GetLastWin32Error());
                return null;
            }

            RuntimeDiagnostics.Info("Startup selector window created.");

            var state = new DialogState(options);
            lock (Sync)
            {
                States[hwnd] = state;
            }

            BuildControls(hwnd, state);
            RuntimeDiagnostics.Info("Startup selector controls created.");
            ShowWindow(hwnd, SwShow);
            UpdateWindow(hwnd);
            SetForegroundWindow(hwnd);

            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                RouteMouseWheel(state, ref message);
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }

            return state.Result;
        }
        catch (Exception ex)
        {
            RuntimeDiagnostics.Error("Native startup dialog failed.", ex);
            return null;
        }
    }

    private static void BuildControls(IntPtr hwnd, DialogState state)
    {
        CreateWindowEx(0, "STATIC", "勾选要加载的模组；使用上下按钮调整顺序。点击查看详情，鼠标移到左右栏可滚轮浏览。",
            WsChild | WsVisible, 20, 18, 1120, 30, hwnd, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        state.ListHandle = CreateWindowEx(
            WsExClientEdge,
            "SysListView32",
            string.Empty,
            WsChild | WsVisible | WsTabStop | LvsReport | LvsSingleSelect | LvsShowSelAlways,
            20,
            58,
            540,
            520,
            hwnd,
            new IntPtr(ListControl),
            IntPtr.Zero,
            IntPtr.Zero);

        SendMessage(state.ListHandle, LvmSetExtendedListViewStyle,
            new IntPtr(LvsExGridLines | LvsExFullRowSelect | LvsExCheckBoxes),
            new IntPtr(LvsExGridLines | LvsExFullRowSelect | LvsExCheckBoxes));
        AddColumn(state.ListHandle, "模组 / 来源", 510);

        CreateButton(hwnd, ButtonUp, "上移", 580, 80, 130);
        CreateButton(hwnd, ButtonDown, "下移", 580, 125, 130);
        CreateButton(hwnd, ButtonToggle, "启用 / 禁用", 580, 170, 130);

        CreateWindowEx(0, "STATIC", "模组详细信息",
            WsChild | WsVisible, 740, 58, 420, 28, hwnd, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        // Multiline edit wraps long lines and supplies native vertical scrolling and selection/copy.
        state.DetailsHandle = CreateWindowEx(WsExClientEdge, "EDIT", string.Empty,
            WsChild | WsVisible | WsVScroll | WsTabStop | EsMultiline | EsAutoVScroll | EsReadOnly,
            740, 90, 420, 488, hwnd,
            new IntPtr(DetailsControl), IntPtr.Zero, IntPtr.Zero);

        CreateButton(hwnd, ButtonApply, "应用并继续", 885, 595, 130);
        CreateButton(hwnd, ButtonCancel, "取消", 1035, 595, 130);

        RebuildList(state);
        UpdateDetails(state);
    }

    private static void CreateButton(IntPtr parent, int id, string text, int x, int y, int width)
    {
        CreateWindowEx(0, "BUTTON", text, WsChild | WsVisible, x, y, width, 36,
            parent, new IntPtr(id), IntPtr.Zero, IntPtr.Zero);
    }

    private static void AddColumn(IntPtr list, string text, int width)
    {
        var textPtr = Marshal.StringToHGlobalUni(text);
        try
        {
            var column = new LvColumn
            {
                Mask = 0x0001 | 0x0002 | 0x0004,
                Width = width,
                Text = textPtr,
                TextLength = text.Length
            };
            var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<LvColumn>());
            try
            {
                Marshal.StructureToPtr(column, ptr, false);
                SendMessage(list, LvmInsertColumnW, IntPtr.Zero, ptr);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(textPtr);
        }
    }

    private static void RebuildList(DialogState state)
    {
        SendMessage(state.ListHandle, LvmDeleteAllItems, IntPtr.Zero, IntPtr.Zero);
        for (var i = 0; i < state.Items.Count; i++)
        {
            var option = state.Items[i];
            var source = option.Source == 2 ? "Steam Workshop" : "mods";
            InsertItem(state.ListHandle, i, $"{i + 1:00}. {option.Name} [{option.Id}] ({source})", option.IsEnabled);
        }

        if (state.Items.Count > 0)
        {
            state.SelectedIndex = Math.Clamp(state.SelectedIndex, 0, state.Items.Count - 1);
            SelectItem(state.ListHandle, state.SelectedIndex);
        }
    }

    private static void InsertItem(IntPtr list, int index, string text, bool enabled)
    {
        var textPtr = Marshal.StringToHGlobalUni(text);
        try
        {
            var item = new LvItem
            {
                Mask = LvifText,
                Item = index,
                Text = textPtr,
                TextLength = text.Length + 1
            };
            var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<LvItem>());
            try
            {
                Marshal.StructureToPtr(item, ptr, false);
                SendMessage(list, LvmInsertItemW, IntPtr.Zero, ptr);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            SetChecked(list, index, enabled);
        }
        finally
        {
            Marshal.FreeHGlobal(textPtr);
        }
    }

    private static void SelectItem(IntPtr list, int index)
    {
        var item = new LvItem { State = 0x0002, StateMask = 0x0002 };
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<LvItem>());
        try
        {
            Marshal.StructureToPtr(item, ptr, false);
            SendMessage(list, LvmSetItemState, new IntPtr(index), ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static void SetChecked(IntPtr list, int index, bool checkedState)
    {
        var item = new LvItem
        {
            State = (uint)((checkedState ? 2 : 1) << 12),
            StateMask = LvisStateImageMask
        };
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<LvItem>());
        try
        {
            Marshal.StructureToPtr(item, ptr, false);
            SendMessage(list, LvmSetItemState, new IntPtr(index), ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static bool IsChecked(IntPtr list, int index)
    {
        var state = SendMessage(list, LvmGetItemState, new IntPtr(index), new IntPtr(LvisStateImageMask)).ToInt64();
        return (state & LvisStateImageMask) == (2 << 12);
    }

    private static int SelectedIndex(IntPtr list)
    {
        return SendMessage(list, LvmGetNextItem, new IntPtr(-1), new IntPtr(LvniSelected)).ToInt32();
    }

    private static void SyncChecks(DialogState state)
    {
        for (var i = 0; i < state.Items.Count; i++)
        {
            if (!state.Items[i].IsBootstrap)
            {
                state.Items[i].IsEnabled = IsChecked(state.ListHandle, i);
            }
        }
    }

    private static void UpdateDetails(DialogState state)
    {
        if (state.SelectedIndex < 0 || state.SelectedIndex >= state.Items.Count) return;

        var option = state.Items[state.SelectedIndex];
        var source = option.Source == 2 ? "Steam Workshop" : "本地 mods 目录";
        var details = $"名称：{option.Name}\r\n" +
                      $"ID：{option.Id}\r\n" +
                      $"版本：{(string.IsNullOrWhiteSpace(option.Version) ? "未知" : option.Version)}\r\n" +
                      $"作者：{(string.IsNullOrWhiteSpace(option.Author) ? "未知" : option.Author)}\r\n" +
                      $"来源：{source}\r\n" +
                      $"状态：{(option.IsEnabled ? "已启用" : "未启用")}\r\n\r\n" +
                      (string.IsNullOrWhiteSpace(option.Description) ? "暂无描述。" : option.Description);
        SetWindowText(state.DetailsHandle, details.ReplaceLineEndings("\r\n"));
        // A newly selected mod always opens at the start of its description.
        SendMessage(state.DetailsHandle, EmSetSel, IntPtr.Zero, IntPtr.Zero);
        SendMessage(state.DetailsHandle, EmScrollCaret, IntPtr.Zero, IntPtr.Zero);
    }

    private static void RouteMouseWheel(DialogState state, ref Message message)
    {
        if (message.MessageId != WmMouseWheel) return;
        // WM_MOUSEWHEEL may target the focused control rather than the hovered one.
        // Retarget once before dispatch, preserving native scroll speed and fine wheel deltas.
        // The coordinates are signed screen coordinates (including monitors left of the primary).
        var coordinates = message.LParam.ToInt64();
        var x = unchecked((short)(coordinates & 0xFFFF));
        var y = unchecked((short)((coordinates >> 16) & 0xFFFF));
        if (ContainsPoint(state.ListHandle, x, y)) message.Window = state.ListHandle;
        else if (ContainsPoint(state.DetailsHandle, x, y)) message.Window = state.DetailsHandle;
    }

    private static bool ContainsPoint(IntPtr control, int x, int y)
    {
        return GetWindowRect(control, out var rect) &&
               x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom;
    }

    private static IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        DialogState? state;
        lock (Sync)
        {
            States.TryGetValue(hwnd, out state);
        }

        if (message == WmCommand && state is not null && (wParam.ToInt32() >> 16) == BnClicked)
        {
            var id = wParam.ToInt32() & 0xFFFF;
            state.SelectedIndex = SelectedIndex(state.ListHandle);
            switch (id)
            {
                case ButtonUp:
                    Move(state, -1);
                    break;
                case ButtonDown:
                    Move(state, 1);
                    break;
                case ButtonToggle:
                    Toggle(state);
                    break;
                case ButtonApply:
                    SyncChecks(state);
                    state.Result = state.Items.ToArray();
                    DestroyWindow(hwnd);
                    break;
                case ButtonCancel:
                    state.Result = null;
                    DestroyWindow(hwnd);
                    break;
            }

            return IntPtr.Zero;
        }

        if (message == WmNotify && state is not null && lParam != IntPtr.Zero)
        {
            var header = Marshal.PtrToStructure<NmHeader>(lParam);
            if (header.IdFrom == new IntPtr(ListControl) && header.Code == LvnItemChanged)
            {
                var selected = SelectedIndex(state.ListHandle);
                if (selected >= 0 && selected != state.SelectedIndex)
                {
                    state.SelectedIndex = selected;
                    UpdateDetails(state);
                }
            }

            return IntPtr.Zero;
        }

        if (message == WmClose && state is not null)
        {
            state.Result = null;
            DestroyWindow(hwnd);
            return IntPtr.Zero;
        }

        if (message == WmDestroy)
        {
            lock (Sync)
            {
                States.Remove(hwnd);
            }

            PostQuitMessage(0);
            return IntPtr.Zero;
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static void Move(DialogState state, int delta)
    {
        SyncChecks(state);
        var from = state.SelectedIndex;
        var to = from + delta;
        if (from < 0 || to < 0 || to >= state.Items.Count) return;
        (state.Items[from], state.Items[to]) = (state.Items[to], state.Items[from]);
        state.SelectedIndex = to;
        RebuildList(state);
        UpdateDetails(state);
    }

    private static void Toggle(DialogState state)
    {
        SyncChecks(state);
        if (state.SelectedIndex < 0 || state.SelectedIndex >= state.Items.Count) return;
        var option = state.Items[state.SelectedIndex];
        if (option.IsBootstrap) return;
        option.IsEnabled = !option.IsEnabled;
        RebuildList(state);
        UpdateDetails(state);
    }

    private sealed class DialogState
    {
        public DialogState(IReadOnlyList<RuntimeModOption> options)
        {
            Items = options.ToList();
        }

        public List<RuntimeModOption> Items { get; }
        public IntPtr ListHandle { get; set; }
        public IntPtr DetailsHandle { get; set; }
        public int SelectedIndex { get; set; }
        public IReadOnlyList<RuntimeModOption>? Result { get; set; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public WndProcDelegate WindowProc;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LvColumn
    {
        public uint Mask;
        public int Format;
        public int Width;
        public IntPtr Text;
        public int TextLength;
        public int SubItem;
        public int Image;
        public int Order;
        public int MinimumWidth;
        public int DefaultWidth;
        public int IdealWidth;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LvItem
    {
        public uint Mask;
        public int Item;
        public int SubItem;
        public uint State;
        public uint StateMask;
        public IntPtr Text;
        public int TextLength;
        public int Image;
        public IntPtr Param;
        public int Indent;
        public int GroupId;
        public uint Columns;
        public IntPtr ColumnData;
        public IntPtr ColumnFormat;
        public int Group;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NmHeader
    {
        public IntPtr HwndFrom;
        public IntPtr IdFrom;
        public int Code;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint MessageId;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Point;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InitCommonControlsData
    {
        public uint Size;
        public uint Classes;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    private static void InitCommonControls()
    {
        var init = new InitCommonControlsData
        {
            Size = (uint)Marshal.SizeOf<InitCommonControlsData>(),
            Classes = 0x00000001
        };
        InitCommonControlsEx(ref init);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WndClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string windowName,
        uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool UpdateWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowText(IntPtr hwnd, string text);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMessage(out Message message, IntPtr hwnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("comctl32.dll")]
    private static extern bool InitCommonControlsEx(ref InitCommonControlsData init);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
