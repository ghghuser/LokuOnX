using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Recorder.UI;

/// <summary>LevelBar の下に出す dB の目盛り（-60 -48 -36 -24 -12 -6 0）。LevelBar と同じ左右の余白で、棒の位置にそろえる。</summary>
public sealed class LevelScale : FrameworkElement
{
    private static readonly int[] Ticks = { -60, -48, -36, -24, -12, -6, 0 };

    public LevelScale() => Height = 13;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double barW = w - LevelBar.ClipWidth - LevelBar.Gap;
        if (barW <= 20) return;
        var brush = TryFindResource("WindowFg") as Brush ?? Brushes.Gray;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var db in Ticks)
        {
            double x = barW * (db + 60) / 60.0;
            dc.DrawLine(new Pen(Brushes.Gray, 1), new Point(x, 0), new Point(x, 2.5));
            var text = new FormattedText(db.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Yu Gothic UI"), 9, brush, dpi);
            // 端は棒からはみ出さないように寄せる
            double left = Math.Clamp(x - text.Width / 2, 0, Math.Max(0, w - text.Width));
            dc.PushOpacity(0.6);
            dc.DrawText(text, new Point(left, 2));
            dc.Pop();
        }
    }
}
