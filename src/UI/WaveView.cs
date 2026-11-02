using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Recorder.UI;

/// <summary>
/// 録音中に右から左へ流れる波形（ピークの推移）。デバイスが複数のときは、デバイスごとに1段ずつ。
/// 値は 0.0〜1.0 のピーク（dBで見やすく表示）。クリップ（0.99以上）は赤。
/// </summary>
public sealed class WaveView : FrameworkElement
{
    private const int ColumnWidth = 2;
    private const int MaxColumns = 3000;

    private static readonly Brush Green = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0x9E, 0x4F)));
    private static readonly Brush Red = Freeze(new SolidColorBrush(Color.FromRgb(0xD3, 0x33, 0x33)));
    private static readonly Brush Line = Freeze(new SolidColorBrush(Color.FromArgb(0x50, 0x80, 0x80, 0x80)));
    private static readonly Pen GreenPen = FreezePen(new Pen(Green, ColumnWidth - 0.5));
    private static readonly Pen RedPen = FreezePen(new Pen(Red, ColumnWidth - 0.5));
    private static readonly Pen LinePen = FreezePen(new Pen(Line, 1));

    private readonly List<List<float>> _rows = new();
    private List<string> _labels = new();

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }

    private static Pen FreezePen(Pen p)
    {
        p.Freeze();
        return p;
    }

    /// <summary>段の数と名前を決める（録音を始めるとき）。いまの波形は消える。</summary>
    public void SetRows(IReadOnlyList<string> labels)
    {
        _labels = labels.ToList();
        _rows.Clear();
        foreach (var _ in labels) _rows.Add(new List<float>());
        InvalidateVisual();
    }

    /// <summary>各段のピークを1つずつ足す（1回＝波形の1列）。</summary>
    public void Push(float[] peaks)
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            var list = _rows[i];
            list.Add(i < peaks.Length ? peaks[i] : 0f);
            if (list.Count > MaxColumns) list.RemoveRange(0, list.Count - MaxColumns);
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0 || _rows.Count == 0) return;
        double rowH = h / _rows.Count;
        int cols = (int)(w / ColumnWidth);
        for (int r = 0; r < _rows.Count; r++)
        {
            double top = r * rowH, cy = top + rowH / 2;
            dc.DrawLine(LinePen, new Point(0, cy), new Point(w, cy));
            if (r > 0) dc.DrawLine(LinePen, new Point(0, top), new Point(w, top));
            var list = _rows[r];
            int n = Math.Min(cols, list.Count);
            for (int k = 0; k < n; k++)
            {
                float p = list[list.Count - 1 - k];
                double level = p <= 0 ? 0 : Math.Clamp((20 * Math.Log10(p) + 60) / 60, 0, 1);
                double half = Math.Max(0.5, level * (rowH / 2 - 1));
                double x = w - k * ColumnWidth - ColumnWidth / 2.0;
                dc.DrawLine(p >= 0.99 ? RedPen : GreenPen, new Point(x, cy - half), new Point(x, cy + half));
            }
            if (_rows.Count > 1 && r < _labels.Count)
            {
                var text = new FormattedText(_labels[r], CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface("Yu Gothic UI"), 11, Brushes.Gray, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                text.MaxTextWidth = Math.Max(10, w - 8);
                text.MaxTextHeight = 16;
                text.Trimming = TextTrimming.CharacterEllipsis;
                dc.DrawText(text, new Point(4, top + 1));
            }
        }
    }
}
