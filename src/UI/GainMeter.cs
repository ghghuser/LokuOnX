using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Recorder.UI;

/// <summary>
/// ゲインとレベルメーターの融合。メーターの棒の上に、ゲインのつまみ（縦長の丸い板）を重ねる。
/// つまみをドラッグ、またはメーターをクリックでゲインを決める。0 dB に戻すのは、メーターではなく「数字（○○ dB）」のダブルクリック（AttachReset。11-4確認後に変更）。
/// bars が2のときは、2本のメーター（L・R）にまたがる1つのつまみ（ゲインは左右に分けない）。
/// </summary>
public sealed class GainMeter : Grid
{
    private readonly List<LevelBar> _bars = new();
    private readonly Border _thumb = new();
    private double _min = -30, _max = 20, _value;
    private bool _drag;
    private bool _muted;

    /// <summary>ゲインが変わった。</summary>
    public event EventHandler? ValueChanged;

    public GainMeter(int bars = 1, double barHeight = 12)
    {
        Background = Brushes.Transparent;   // 棒のすき間でもクリックできるように
        Cursor = Cursors.Hand;
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < Math.Max(1, bars); i++)
        {
            var b = new LevelBar { Height = barHeight };
            if (i < bars - 1) b.Margin = new Thickness(0, 0, 0, 1);
            stack.Children.Add(b);
            _bars.Add(b);
        }
        Children.Add(stack);
        _thumb.Width = 8;
        _thumb.Height = bars * barHeight + (bars - 1) + 8;
        _thumb.CornerRadius = new CornerRadius(4);
        _thumb.BorderThickness = new Thickness(1.5);
        _thumb.BorderBrush = new SolidColorBrush(Color.FromArgb(0xB0, 0x3A, 0x33, 0x20));
        _thumb.HorizontalAlignment = HorizontalAlignment.Left;
        _thumb.VerticalAlignment = VerticalAlignment.Center;
        _thumb.IsHitTestVisible = false;
        _thumb.SetResourceReference(Border.BackgroundProperty, "Pastel");
        SetZIndex(_thumb, 5);
        Children.Add(_thumb);
        SizeChanged += (_, _) => Place();
    }

    /// <summary>1本目のメーター（デバイスの行では唯一のメーター）。</summary>
    public LevelBar Bar => _bars[0];

    public IReadOnlyList<LevelBar> Bars => _bars;

    public double Minimum { get => _min; set { _min = value; Place(); } }

    public double Maximum { get => _max; set { _max = value; Place(); } }

    /// <summary>ゲイン（dB）。0.5 刻み。</summary>
    public double Value
    {
        get => _value;
        set
        {
            var v = Math.Clamp(Math.Round(value * 2) / 2, _min, _max);
            if (v == _value) return;
            _value = v;
            Place();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>ミュート中か。メーターは棒を出さず、つまみも橙になる。</summary>
    public bool Muted
    {
        get => _muted;
        set
        {
            if (_muted == value) return;
            _muted = value;
            foreach (var b in _bars) b.Muted = value;
            if (value) _thumb.Background = MeterColors.Muted;
            else _thumb.SetResourceReference(Border.BackgroundProperty, "Pastel");
        }
    }

    public void Reset() { foreach (var b in _bars) b.Reset(); }

    /// <summary>数字の表示（「+2.0 dB」など）をダブルクリックすると 0 dB に戻す。メーター自体のダブルクリックでは戻さない。</summary>
    public void AttachReset(TextBlock text)
    {
        text.Background = Brushes.Transparent;   // 文字のすき間でも反応するように
        text.Cursor = Cursors.Hand;
        text.ToolTip = "ダブルクリックで 0 dB に戻します";
        text.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount < 2) return;
            Value = 0;
            e.Handled = true;
        };
    }

    private double BarWidth => Math.Max(1, ActualWidth - LevelBar.ClipWidth - LevelBar.Gap);

    private void Place()
    {
        if (ActualWidth <= 0 || _max <= _min) return;
        double x = (_value - _min) / (_max - _min) * BarWidth - _thumb.Width / 2;
        _thumb.Margin = new Thickness(x, 0, 0, 0);
    }

    private void SetFrom(Point p)
    {
        double f = Math.Clamp(p.X / BarWidth, 0, 1);
        Value = _min + f * (_max - _min);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _drag = true;
        CaptureMouse();
        SetFrom(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag) SetFrom(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_drag) return;
        _drag = false;
        ReleaseMouseCapture();
    }
}
