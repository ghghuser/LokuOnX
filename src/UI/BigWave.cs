using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Recorder.UI;

/// <summary>
/// 全体波形（録音する音全体）。0.04秒ごとの最小値・最大値を最大1500区間（約60秒）ためて、1区間を横1pxの縦線で描く。右端が最新。
/// 小さい音も見えるよう、振幅を |v|^0.6 でかさ上げする。中央に軸線。クリップ（±0.99）は赤。最小化中は描画を省く。
/// </summary>
public sealed class BigWave : FrameworkElement
{
    private const int MaxColumns = 1500;
    private const double Power = 0.6;

    private readonly List<(float Min, float Max)> _cols = new();
    private DateTime _drawn = DateTime.MinValue;

    public BigWave() => Height = 86;

    public void Clear()
    {
        _cols.Clear();
        InvalidateVisual();
    }

    /// <summary>新しい区間を足す（古い順）。描画は約15回/秒に間引く。</summary>
    public void Push(IReadOnlyList<(float Min, float Max)> cols)
    {
        if (cols.Count == 0) return;
        _cols.AddRange(cols);
        if (_cols.Count > MaxColumns) _cols.RemoveRange(0, _cols.Count - MaxColumns);
        var now = DateTime.Now;
        if ((now - _drawn).TotalMilliseconds < 66) return;
        if (Window.GetWindow(this)?.WindowState == WindowState.Minimized) return;
        _drawn = now;
        InvalidateVisual();
    }

    private static double Scale(float v) => Math.Sign(v) * Math.Pow(Math.Abs(Math.Min(1f, Math.Max(-1f, v))), Power);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 4 || h <= 4) return;
        var bg = TryFindResource("LaneBg") as Brush ?? Brushes.Transparent;
        var border = TryFindResource("LaneBorder") as Brush ?? Brushes.Gray;
        dc.DrawRoundedRectangle(bg, new Pen(border, 1), new Rect(0.5, 0.5, w - 1, h - 1), 8, 8);
        double cy = h / 2, half = h / 2 - 4;
        var faint = new Pen(new SolidColorBrush(Color.FromArgb(0x28, 0x80, 0x80, 0x80)), 1);
        foreach (var a in new[] { 0.25, 0.5 })
        {
            double dy = Math.Pow(a, Power) * half;
            dc.DrawLine(faint, new Point(6, cy - dy), new Point(w - 6, cy - dy));
            dc.DrawLine(faint, new Point(6, cy + dy), new Point(w - 6, cy + dy));
        }
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0x80, 0x80, 0x80)), 1), new Point(6, cy), new Point(w - 6, cy));

        int visible = (int)Math.Min(_cols.Count, w - 12);
        var blue = new StreamGeometry();
        var red = new StreamGeometry();
        using (var gb = blue.Open())
        using (var gr = red.Open())
        {
            for (int k = 0; k < visible; k++)
            {
                var (mn, mx) = _cols[_cols.Count - 1 - k];
                if (mx < mn) continue;
                double x = Math.Floor(w - 6 - k) - 0.5;
                double y1 = cy - Scale(mx) * half, y2 = cy - Scale(mn) * half;
                if (y2 - y1 < 1) { y1 -= 0.5; y2 += 0.5; }
                var ctx = mx >= 0.99f || mn <= -0.99f ? gr : gb;
                ctx.BeginFigure(new Point(x, y1), false, false);
                ctx.LineTo(new Point(x, y2), true, false);
            }
        }
        blue.Freeze();
        red.Freeze();
        dc.DrawGeometry(null, new Pen(MeterColors.Blue, 1), blue);
        dc.DrawGeometry(null, new Pen(MeterColors.Red, 1), red);
    }
}
