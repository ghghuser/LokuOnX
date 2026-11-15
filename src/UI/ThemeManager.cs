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
    private static readonly string[] SurfaceKeys = { "WindowBg", "PanelBg", "PanelHover", "InputBg", "LaneBg", "WaveBg", "WaveBgOff" };
    private static string _mode = "auto";
    private static string _effect = "auto";
    private static int _opacity = 69;

    /// <summary>背景効果と濃さ（Settings.BackdropEffect / BackdropOpacity）。変えたら Refresh で開いているウィンドウへ反映。</summary>
    public static void Configure(string effect, int opacity)
    {
        _effect = effect;
        _opacity = Math.Clamp(opacity, 30, 100);
    }

    /// <summary>開いているすべてのウィンドウへ、背景効果と濃さを反映する。</summary>
    public static void Refresh()
    {
        var app = Application.Current;
        if (app == null) return;
        foreach (Window w in app.Windows) Style(w, IsDark(_mode), IsSoft(_mode));
    }

    /// <summary>mode は "auto" / "light" / "dark" / "soft"（ニューモーフィズム風・ライト）/ "softdark"（同・ダーク）。</summary>
    public static void Apply(string mode)
    {
        _mode = mode;
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
        _ => new[] { "Light", "ComboControls", "TabControls", "LightControls" },
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

    /// <summary>実際に使う背景効果の番号（DWMWA_SYSTEMBACKDROP_TYPE: 1＝なし、2＝Mica、3＝アクリル）。</summary>
    internal static int BackdropType(string effect, bool dark, bool soft) => effect switch
    {
        "mica" => 2,
        "acrylic" => 3,
        "none" => 1,
        _ => dark && !soft ? 3 : 1,   // auto: 従来どおりダークだけアクリル
    };

    /// <summary>
    /// タイトルバーの色と、背景効果（Windows 11 22H2 以降の Mica／アクリル）を合わせる。
    /// 効果が使えない環境（Windows 10 など）では、不透明のまま。
    /// </summary>
    private static void Style(Window w, bool dark, bool soft)
    {
        try
        {
            var handle = new WindowInteropHelper(w).Handle;
            if (handle == IntPtr.Zero) return;
            var on = dark ? 1 : 0;
            DwmSetWindowAttribute(handle, 20, ref on, sizeof(int));   // DWMWA_USE_IMMERSIVE_DARK_MODE
            var type = BackdropType(_effect, dark, soft);
            var translucent = type != 1;
            var ok = false;
            if (HwndSource.FromHwnd(handle) is { CompositionTarget: { } target })
            {
                var m = new Margins { Left = translucent ? -1 : 0, Right = translucent ? -1 : 0, Top = translucent ? -1 : 0, Bottom = translucent ? -1 : 0 };
                var hr = DwmSetWindowAttribute(handle, 38, ref type, sizeof(int));
                if (hr == 0)
                {
                    target.BackgroundColor = translucent ? Colors.Transparent : SystemColors.WindowColor;
                    DwmExtendFrameIntoClientArea(handle, ref m);
                    ok = translucent;
                }
            }
            // 効果が付いたウィンドウだけ、テーマの背景色に濃さ（不透明度）をかける（付かなければ不透明のまま）
            var res = Application.Current.Resources;
            // 背景だけでなく、不透明の面（パネル・入力欄・波形欄など）にも同じ濃さをかけ、透け方をそろえる。
            // もともと半透明の面（ダークの多く）はそのまま。ポップアップ（PopupBg）は読みやすさのため不透明のまま
            foreach (var key in SurfaceKeys)
            {
                res.Remove(key);   // テーマ側の値に戻してから読む
                if (ok && res[key] is SolidColorBrush b && (key == "WindowBg" || b.Color.A == 0xFF))
                {
                    var c = b.Color;
                    var a = key == "WindowBg" ? c.A : (byte)255;
                    res[key] = new SolidColorBrush(Color.FromArgb((byte)Math.Round(a * _opacity / 100.0), c.R, c.G, c.B));
                }
            }
        }
        catch (Exception) { }
    }
}
