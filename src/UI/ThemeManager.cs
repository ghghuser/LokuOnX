using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Recorder.UI;

/// <summary>
/// 画面の色（自動／ライト／ダーク／ソフト（ライト・ダーク））。ライトは Windows 標準の見た目のまま、ダークは色のリソースを差し替える（PROJECT.md §3-6）。
/// 読み込みに失敗したらライトのまま動く。
/// </summary>
public static class ThemeManager
{
    private static readonly List<ResourceDictionary> Current = new();

    /// <summary>mode は "auto" / "light" / "dark" / "soft"（ニューモーフィズム風・ライト）/ "softdark"（同・ダーク）。</summary>
    public static void Apply(string mode)
    {
        var dark = IsDark(mode);
        var soft = IsSoft(mode);
        var app = Application.Current;
        if (app == null) return;
        foreach (var d in Current) app.Resources.MergedDictionaries.Remove(d);
        Current.Clear();
        foreach (var name in Files(mode))
        {
            try
            {
                var d = new ResourceDictionary { Source = new Uri($"/LokuOnX;component/UI/Theme/{name}.xaml", UriKind.Relative) };
                app.Resources.MergedDictionaries.Add(d);
                Current.Add(d);
            }
            catch (Exception) { /* 色の読み込みに失敗してもライトの見た目で動く */ }
        }
        foreach (Window w in app.Windows) Style(w, dark, soft);
    }

    /// <summary>ウィンドウを開くとき（SourceInitialized）に、タイトルバーの色も合わせる。</summary>
    public static void Attach(Window w, string mode)
    {
        var dark = IsDark(mode);
        var soft = IsSoft(mode);
        w.SourceInitialized += (_, _) => Style(w, dark, soft);
    }

    private static bool IsDark(string mode) => mode is "dark" or "softdark" || (mode == "auto" && SystemUsesDark());

    private static bool IsSoft(string mode) => mode is "soft" or "softdark";

    /// <summary>読み込む色・部品の定義（後ろのものが前のものを上書きする）。コンボボックスとタブは全テーマ共通。</summary>
    private static string[] Files(string mode) => mode switch
    {
        "soft" => new[] { "Light", "Soft", "ComboControls", "TabControls", "SoftControls" },
        "softdark" => new[] { "Dark", "SoftDark", "ComboControls", "TabControls", "DarkControls", "DarkList", "SoftControlsDark" },
        _ when IsDark(mode) => new[] { "Dark", "ComboControls", "TabControls", "DarkControls", "DarkList" },
        _ => new[] { "Light", "ComboControls", "TabControls" },
    };

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
    private static void Style(Window w, bool dark, bool soft)
    {
        try
        {
            var handle = new WindowInteropHelper(w).Handle;
            if (handle == IntPtr.Zero) return;
            var on = dark ? 1 : 0;
            DwmSetWindowAttribute(handle, 20, ref on, sizeof(int));   // DWMWA_USE_IMMERSIVE_DARK_MODE
            var translucent = dark && !soft;   // ソフト（ダーク）は影の見え方のため、半透明にしない
            var ok = false;
            if (HwndSource.FromHwnd(handle) is { CompositionTarget: { } target })
            {
                var m = new Margins { Left = translucent ? -1 : 0, Right = translucent ? -1 : 0, Top = translucent ? -1 : 0, Bottom = translucent ? -1 : 0 };
                var backdrop = translucent ? 3 : 1;   // DWMWA_SYSTEMBACKDROP_TYPE: 3＝アクリル、1＝なし
                var hr = DwmSetWindowAttribute(handle, 38, ref backdrop, sizeof(int));
                if (hr == 0)
                {
                    target.BackgroundColor = translucent ? Colors.Transparent : SystemColors.WindowColor;
                    DwmExtendFrameIntoClientArea(handle, ref m);
                    ok = translucent;
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
