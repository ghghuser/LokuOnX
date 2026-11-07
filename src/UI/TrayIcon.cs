using System.Runtime.InteropServices;

namespace Recorder.UI;

/// <summary>トレイの右クリックメニューの1項目（Id が 0 のものは区切り線）。</summary>
public sealed record TrayMenuItem(int Id, string Text, bool Enabled = true)
{
    public static TrayMenuItem Separator => new(0, "");
}

/// <summary>
/// タスクトレイのアイコン（Windows API の Shell_NotifyIcon。PROJECT.md §3-6, §3-8）。
/// ダブルクリックで画面を開く、右クリックでメニュー。エクスプローラーが再起動してもアイコンを出し直す。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const int CallbackMessage = 0x8001;   // WM_APP + 1
    private const uint NifMessage = 1, NifIcon = 2, NifTip = 4;
    private const uint NimAdd = 0, NimModify = 1, NimDelete = 2;
    private const int WmLButtonDblClk = 0x0203, WmRButtonUp = 0x0205;
    private const uint TpmReturnCmd = 0x0100, TpmRightButton = 0x0002;
    private const uint MfString = 0, MfSeparator = 0x800, MfGrayed = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, uint count);

    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")] private static extern uint RegisterWindowMessage(string text);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string? text);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hWnd, IntPtr rect);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private readonly MessageWindow _window;
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private IntPtr _icon;           // いま出しているアイコン
    private IntPtr _appIcon;        // アプリのアイコン
    private IntPtr _led;            // 録音中の赤いLED風アイコン（初めて必要になったときに作る）
    private IntPtr _amber;          // 待機中の黄色いリングのアイコン（同上）
    private bool _ownsIcon;
    private bool _recording;
    private bool _standby;
    private bool _visible;
    private string _tip = "";

    /// <summary>ダブルクリック（または「表示」）で画面を開く。</summary>
    public event Action? OpenRequested;

    /// <summary>右クリックされたとき、その時点のメニューを作って返す。</summary>
    public Func<IReadOnlyList<TrayMenuItem>>? MenuProvider { get; set; }

    /// <summary>メニューが選ばれた（項目の Id）。</summary>
    public event Action<int>? CommandSelected;

    public TrayIcon(MessageWindow window)
    {
        _window = window;
        _window.AddHook(OnMessage);
        LoadAppIcon();
        _appIcon = _icon;
    }

    /// <summary>exe に埋め込んだアプリのアイコンを取り出す（取れなければ Windows 標準のアイコン）。</summary>
    private void LoadAppIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            var small = new IntPtr[1];
            if (path != null && ExtractIconEx(path, 0, null, small, 1) > 0 && small[0] != IntPtr.Zero)
            {
                _icon = small[0];
                _ownsIcon = true;
                return;
            }
        }
        catch (Exception) { }
        _icon = LoadIcon(IntPtr.Zero, new IntPtr(32512));   // IDI_APPLICATION
    }

    /// <summary>録音中は、アイコンをLED風の大きい赤丸にする。録音中でなければ（待機中なら黄色いリング、それ以外は）アプリのアイコンへ戻す。</summary>
    public void SetRecording(bool recording)
    {
        if (recording == _recording) return;
        _recording = recording;
        UpdateIcon();
    }

    /// <summary>音を待っている待機中は、アイコンを黄色いリングにする（録音中の赤丸が優先）。</summary>
    public void SetStandby(bool standby)
    {
        if (standby == _standby) return;
        _standby = standby;
        UpdateIcon();
    }

    /// <summary>いまの状態（録音中＞待機中＞通常）に合うアイコンへ差し替える。作れなければ、そのままアプリのアイコン。</summary>
    private void UpdateIcon()
    {
        var next = _appIcon;
        if (_recording)
        {
            if (_led == IntPtr.Zero)
            {
                try { _led = LedIcon.Create(Math.Max(16, GetSystemMetrics(49))); }   // SM_CXSMICON（トレイの小アイコンの幅）
                catch (Exception) { _led = IntPtr.Zero; }
            }
            if (_led != IntPtr.Zero) next = _led;
            else _recording = false;
        }
        if (!_recording && _standby)
        {
            if (_amber == IntPtr.Zero)
            {
                try { _amber = LedIcon.CreateStandby(Math.Max(16, GetSystemMetrics(49))); }
                catch (Exception) { _amber = IntPtr.Zero; }
            }
            if (_amber != IntPtr.Zero) next = _amber;
        }
        if (next == _icon) return;
        _icon = next;
        if (!_visible) return;
        var d = Data(NifIcon);
        Shell_NotifyIcon(NimModify, ref d);
    }

    private NotifyIconData Data(uint flags) => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(),
        hWnd = _window.Handle,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = _tip,
        szInfo = "",
        szInfoTitle = "",
    };

    public bool Visible
    {
        get => _visible;
        set
        {
            if (value == _visible) return;
            var d = Data(NifMessage | NifIcon | NifTip);
            _visible = Shell_NotifyIcon(value ? NimAdd : NimDelete, ref d) ? value : _visible;
        }
    }

    /// <summary>マウスを乗せたときに出る文字（127文字まで）。</summary>
    public void SetTip(string text)
    {
        if (text.Length > 120) text = text[..120];
        if (text == _tip) return;
        _tip = text;
        if (!_visible) return;
        var d = Data(NifTip);
        Shell_NotifyIcon(NimModify, ref d);
    }

    private IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _taskbarCreated && _visible)
        {
            // エクスプローラーが再起動してトレイが作り直された。出し直す
            var d = Data(NifMessage | NifIcon | NifTip);
            Shell_NotifyIcon(NimAdd, ref d);
            return IntPtr.Zero;
        }
        if (msg != CallbackMessage) return IntPtr.Zero;
        handled = true;
        int mouse = lParam.ToInt32() & 0xFFFF;
        if (mouse == WmLButtonDblClk) OpenRequested?.Invoke();
        else if (mouse == WmRButtonUp) ShowMenu();
        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        var items = MenuProvider?.Invoke();
        if (items == null || items.Count == 0) return;
        var menu = CreatePopupMenu();
        try
        {
            foreach (var item in items)
            {
                if (item.Id == 0) AppendMenu(menu, MfSeparator, UIntPtr.Zero, null);
                else AppendMenu(menu, MfString | (item.Enabled ? 0u : MfGrayed), (UIntPtr)(uint)item.Id, item.Text);
            }
            GetCursorPos(out var p);
            SetForegroundWindow(_window.Handle);   // これをしないと、外をクリックしてもメニューが閉じない
            int cmd = TrackPopupMenu(menu, TpmReturnCmd | TpmRightButton, p.X, p.Y, 0, _window.Handle, IntPtr.Zero);
            PostMessage(_window.Handle, 0, IntPtr.Zero, IntPtr.Zero);   // WM_NULL
            if (cmd != 0) CommandSelected?.Invoke(cmd);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        Visible = false;
        if (_ownsIcon && _appIcon != IntPtr.Zero) DestroyIcon(_appIcon);
        if (_led != IntPtr.Zero) DestroyIcon(_led);
        if (_amber != IntPtr.Zero) DestroyIcon(_amber);
        _appIcon = _led = _amber = _icon = IntPtr.Zero;
    }
}
