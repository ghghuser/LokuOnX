using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Recorder.UI;

/// <summary>
/// 画面の色（自動／ライト／ダーク）。ライトは Windows 標準の見た目のまま、ダークは色のリソースを差し替える（PROJECT.md §3-6）。
/// 読み込みに失敗したらライトのまま動く。
/// </summary>
public static class ThemeManager
{
    private static readonly List<ResourceDictionary> Current = new();

    /// <summary>mode は "auto" / "light" / "dark"。</summary>
    public static void Apply(string mode)
    {
        var dark = mode == "dark" || (mode == "auto" && SystemUsesDark());
        var app = Application.Current;
        if (app == null) return;
        foreach (var d in Current) app.Resources.MergedDictionaries.Remove(d);
        Current.Clear();
        foreach (var name in dark ? new[] { "Dark", "DarkControls" } : new[] { "Light" })
        {
            try
            {
                var d = new ResourceDictionary { Source = new Uri($"/LokuOnX;component/UI/Theme/{name}.xaml", UriKind.Relative) };
                app.Resources.MergedDictionaries.Add(d);
                Current.Add(d);
            }
            catch (Exception) { /* 色の読み込みに失敗してもライトの見た目で動く */ }
        }
        foreach (Window w in app.Windows) Style(w, dark);
    }

    /// <summary>ウィンドウを開くとき（SourceInitialized）に、タイトルバーの色も合わせる。</summary>
    public static void Attach(Window w, string mode)
    {
        var dark = mode == "dark" || (mode == "auto" && SystemUsesDark());
        w.SourceInitialized += (_, _) => Style(w, dark);
    }

    private static bool SystemUsesDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (Exception) { return false; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    /// <summary>
    /// タイトルバーの色と、ダークのときの半透明の背景（Windows 11 のアクリル）を合わせる。
    /// アクリルが使えない環境（Windows 10 など）では、不透明のダークのまま。
    /// </summary>
    private static void Style(Window w, bool dark)
    {
        try
        {
            var handle = new WindowInteropHelper(w).Handle;
            if (handle == IntPtr.Zero) return;
            var on = dark ? 1 : 0;
            DwmSetWindowAttribute(handle, 20, ref on, sizeof(int));   // DWMWA_USE_IMMERSIVE_DARK_MODE
            var ok = false;
            if (HwndSource.FromHwnd(handle) is { CompositionTarget: { } target })
            {
                var m = new Margins { Left = dark ? -1 : 0, Right = dark ? -1 : 0, Top = dark ? -1 : 0, Bottom = dark ? -1 : 0 };
                var backdrop = dark ? 3 : 1;   // DWMWA_SYSTEMBACKDROP_TYPE: 3＝アクリル、1＝なし
                var hr = DwmSetWindowAttribute(handle, 38, ref backdrop, sizeof(int));
                if (hr == 0)
                {
                    target.BackgroundColor = dark ? Colors.Transparent : SystemColors.WindowColor;
                    DwmExtendFrameIntoClientArea(handle, ref m);
                    ok = dark;
                }
            }
            // 半透明にできたウィンドウだけ、背景色を半透明にする（できなければ不透明のまま）
            var res = Application.Current.Resources;
            if (ok) res["WindowBg"] = new SolidColorBrush(Color.FromArgb(0xB0, 0x1E, 0x1F, 0x22));
            else res.Remove("WindowBg");
        }
        catch (Exception) { }
    }
}
