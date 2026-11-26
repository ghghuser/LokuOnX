using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Gokko.UI;

/// <summary>画面の色（自動＝Windowsに合わせる／ライト／ダーク）。色のブラシは Application のリソースに置き、Theme.xaml のスタイルが DynamicResource で参照する。</summary>
public static class ThemeManager
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private static readonly List<WeakReference<Window>> Windows = new();

    public static bool IsDark { get; private set; }

    /// <summary>Theme.xaml を読み込む。失敗しても（標準の見た目で）動き続ける。</summary>
    public static void LoadStyles()
    {
        try
        {
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/Gokko4DJ;component/UI/Theme.xaml", UriKind.Relative),
            });
        }
        catch (Exception) { /* 標準の見た目のまま */ }
    }

    private static bool WindowsPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (Exception) { return false; }
    }

    private static Brush B(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    /// <summary>mode: auto／light／dark。</summary>
    public static void Apply(string mode)
    {
        IsDark = mode == "dark" || (mode != "light" && WindowsPrefersDark());
        var r = Application.Current.Resources;
        if (IsDark)
        {
            r["WinBg"] = B("#1B1E20"); r["PanelBg"] = B("#25292C"); r["Fg"] = B("#E8EAEC"); r["FgDim"] = B("#9BA3A9");
            r["Accent"] = B("#2FCB91"); r["AccentFg"] = B("#07211A"); r["CtrlBg"] = B("#2F3438"); r["CtrlHover"] = B("#3C4247"); r["Line"] = B("#41484D");
        }
        else
        {
            r["WinBg"] = B("#F2F5F4"); r["PanelBg"] = B("#FFFFFF"); r["Fg"] = B("#1E2427"); r["FgDim"] = B("#65717A");
            r["Accent"] = B("#17B284"); r["AccentFg"] = B("#FFFFFF"); r["CtrlBg"] = B("#E2E8E6"); r["CtrlHover"] = B("#D3DCD9"); r["Line"] = B("#C9D2CF");
        }
        foreach (var w in Windows.ToList())
        {
            if (w.TryGetTarget(out var win)) SetTitleBar(win);
        }
        Windows.RemoveAll(w => !w.TryGetTarget(out _));
    }

    /// <summary>ウィンドウに背景・文字色・タイトルバーの色を適用する。</summary>
    public static void Attach(Window w)
    {
        w.SetResourceReference(Control.BackgroundProperty, "WinBg");
        w.SetResourceReference(Control.ForegroundProperty, "Fg");
        Windows.Add(new WeakReference<Window>(w));
        w.SourceInitialized += (_, _) => SetTitleBar(w);
    }

    private static void SetTitleBar(Window w)
    {
        try
        {
            var h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) return;
            var on = IsDark ? 1 : 0;
            DwmSetWindowAttribute(h, 20, ref on, sizeof(int));   // DWMWA_USE_IMMERSIVE_DARK_MODE
        }
        catch (Exception) { }
    }
}
