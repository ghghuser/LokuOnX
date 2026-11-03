using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
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
        foreach (Window w in app.Windows) TitleBar(w, dark);
    }

    /// <summary>ウィンドウを開くとき（SourceInitialized）に、タイトルバーの色も合わせる。</summary>
    public static void Attach(Window w, string mode)
    {
        var dark = mode == "dark" || (mode == "auto" && SystemUsesDark());
        w.SourceInitialized += (_, _) => TitleBar(w, dark);
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

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private static void TitleBar(Window w, bool dark)
    {
        try
        {
            var handle = new WindowInteropHelper(w).Handle;
            if (handle == IntPtr.Zero) return;
            var on = dark ? 1 : 0;
            DwmSetWindowAttribute(handle, 20, ref on, sizeof(int));   // DWMWA_USE_IMMERSIVE_DARK_MODE
        }
        catch (Exception) { }
    }
}
