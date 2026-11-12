using System.Windows;
using System.Windows.Media;

namespace Recorder.UI;

/// <summary>エフェクト系のウィンドウ（入力・出力）の配色。そのウィンドウの中だけ、黄色の強調色を紫系に差し替える（メイン画面は変えない）。</summary>
internal static class EffectPalette
{
    public static void Apply(FrameworkElement w)
    {
        bool dark = w.TryFindResource("WindowBg") is SolidColorBrush bg && (bg.Color.R + bg.Color.G + bg.Color.B) / 3 < 128;
        Brush B(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        w.Resources["Pastel"] = B(dark ? "#A58CFF" : "#6C4FE0");
        w.Resources["PastelDim"] = B(dark ? "#3E3470" : "#E0D9FA");
        w.Resources["ToggleOn"] = B(dark ? "#9A86FF" : "#6C4FE0");
        w.Resources["ToggleKnobOn"] = B(dark ? "#1E1A3A" : "#FFFFFF");
        w.Resources["GroupFg"] = B(dark ? "#B9A6FF" : "#5A3FC8");
    }
}
