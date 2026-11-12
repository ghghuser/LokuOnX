using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Recorder.UI;

/// <summary>「FX」ボタンの見た目（入力の行・出力カードで共通）。効果が設定されているとき（any）は青紫に光らせて太字、バイパス中は光らせず薄く。</summary>
internal static class FxButtonLook
{
    public static void Apply(Button b, bool any, bool bypass, string subject, string idleTip)
    {
        if (any)
        {
            // 青紫に光っているような見た目（文字・枠を青紫にし、同じ色のぼかしの光を付ける）。バイパス中は光らせず薄くする
            b.Foreground = new SolidColorBrush(Color.FromRgb(0x9D, 0x8B, 0xFF));
            b.BorderBrush = new SolidColorBrush(Color.FromRgb(0x7C, 0x6C, 0xFF));
            b.Effect = bypass ? null : new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0x7C, 0x6C, 0xFF), BlurRadius = 9, ShadowDepth = 0, Opacity = 0.95 };
        }
        else
        {
            b.ClearValue(Control.ForegroundProperty);
            b.ClearValue(Control.BorderBrushProperty);
            b.ClearValue(UIElement.EffectProperty);
        }
        b.FontWeight = any ? FontWeights.Bold : FontWeights.Normal;
        b.Opacity = any && bypass ? 0.55 : 1.0;
        b.ToolTip = any
            ? (bypass ? subject + "（設定あり・いまはバイパス中）" : subject + "（設定あり）") + "。クリックで開きます"
            : idleTip;
    }
}
