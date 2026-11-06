using System.Runtime.InteropServices;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>グローバルホットキー（どのアプリを使っていても効く。PROJECT.md §3-8）。登録と解除、押されたときの通知。</summary>
public sealed class HotkeyHost : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;   // 押しっぱなしで連続して呼ばれないようにする

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly MessageWindow _window;
    private readonly List<int> _ids = new();

    /// <summary>押されたホットキーの番号（Register に渡した id）。</summary>
    public event Action<int>? Pressed;

    public HotkeyHost(MessageWindow window)
    {
        _window = window;
        _window.AddHook(OnMessage);
    }

    private IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey) return IntPtr.Zero;
        handled = true;
        Pressed?.Invoke(wParam.ToInt32());
        return IntPtr.Zero;
    }

    /// <summary>登録する。ほかのアプリが同じキーを使っているなどで失敗したら false。</summary>
    public bool Register(int id, HotkeySpec spec)
    {
        if (!RegisterHotKey(_window.Handle, id, spec.Modifiers | ModNoRepeat, spec.VirtualKey)) return false;
        _ids.Add(id);
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _ids) UnregisterHotKey(_window.Handle, id);
        _ids.Clear();
    }

    public void Dispose() => UnregisterAll();
}
