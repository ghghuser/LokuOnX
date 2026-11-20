using Recorder.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Recorder.UI;

/// <summary>
/// ゲインとレベルメーターの融合。メーターの棒の上に、ゲインのつまみ（縦長の丸い板）を重ねる。
/// つまみをドラッグ、またはメーターをクリックでゲインを決める。0 dB に戻すのは、メーターではなく「数字（○○ dB）」のダブルクリック（AttachReset。11-4確認後に変更）。
/// bars が2のときは、2本のメーター（L・R）にまたがる1つのつまみ（左右一括）。
/// modes＝true（入力デバイスの行。M15）のときは、動かし方を3通りに切り替えられる（Mode）:
///   0 左右一括（つまみ1つ）／1 左右それぞれ（L・R の棒に1つずつ）／2 バランス（音量は固定で、つまみが左右にずらす。中央が0）。
/// modes のとき、メーターのダブルクリックでミュート（左右それぞれのときは、押した側の棒だけ）。もう一度で元のゲインに戻る。
/// </summary>
public sealed class GainMeter : Grid
{
    private readonly List<LevelBar> _bars = new();
    private readonly Border _thumb = new();
    private readonly Border _thumbR = new();   // 左右それぞれのときの、右の棒のつまみ
    private readonly Border _center = new();   // バランスのときの、中央の目印
    private readonly TextBlock _lblL = new(), _lblR = new();   // バランスのときの、左右の端の「L」「R」
    private const double GapNormal = 1, GapSplit = 3;   // 2本の棒の間（左右それぞれのときだけ、ほんのわずかに広げる）
    private readonly bool _modes;
    private readonly double _barHeight;
    private double _min = -30, _max = 20, _value, _valueR, _balance;
    private double _before, _beforeR;   // ミュートする前のゲイン（ミュートを解くときに戻す）
    private int _mode;
    private int _dragCh;
    private bool _drag;
    private bool _muted;

    /// <summary>ゲインが変わった。</summary>
    public event EventHandler? ValueChanged;

    public GainMeter(int bars = 1, double barHeight = 12, bool modes = false)
    {
        _modes = modes && bars == 2;
        _barHeight = barHeight;
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
        MinHeight = bars * barHeight + (bars - 1) * GapSplit + 8;   // つまみ・棒の間隔が変わっても、行の高さは変えない
        SetupThumb(_thumb);
        Children.Add(_thumb);
        SetupThumb(_thumbR);
        _thumbR.Visibility = Visibility.Collapsed;
        Children.Add(_thumbR);
        _center.Width = 1;
        _center.Height = bars * barHeight + (bars - 1);
        _center.HorizontalAlignment = HorizontalAlignment.Left;
        _center.VerticalAlignment = VerticalAlignment.Center;
        _center.IsHitTestVisible = false;
        _center.Opacity = 0.55;
        _center.Visibility = Visibility.Collapsed;
        _center.SetResourceReference(Border.BackgroundProperty, "WindowFg");
        SetZIndex(_center, 4);
        Children.Add(_center);
        foreach (var (t, left) in new[] { (_lblL, true), (_lblR, false) })
        {
            t.Text = left ? "L" : "R";
            t.FontSize = 9;
            t.FontWeight = FontWeights.SemiBold;
            t.Opacity = 0.85;
            t.IsHitTestVisible = false;
            t.VerticalAlignment = VerticalAlignment.Center;
            t.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            t.Margin = left ? new Thickness(3, 0, 0, 0) : new Thickness(0, 0, LevelBar.ClipWidth + LevelBar.Gap + 3, 0);
            t.Visibility = Visibility.Collapsed;
            t.SetResourceReference(TextBlock.ForegroundProperty, "WindowFg");
            SetZIndex(t, 6);
            Children.Add(t);
        }
        SizeChanged += (_, _) => Place();
    }

    private static void SetupThumb(Border t)
    {
        t.Width = 8;
        t.CornerRadius = new CornerRadius(4);
        t.BorderThickness = new Thickness(1.5);
        t.BorderBrush = new SolidColorBrush(Color.FromArgb(0xB0, 0x3A, 0x33, 0x20));
        t.HorizontalAlignment = HorizontalAlignment.Left;
        t.VerticalAlignment = VerticalAlignment.Center;
        t.IsHitTestVisible = false;
        t.SetResourceReference(Border.BackgroundProperty, "Pastel");
        SetZIndex(t, 5);
    }

    /// <summary>1本目のメーター（デバイスの行では唯一のメーター）。</summary>
    public LevelBar Bar => _bars[0];

    public IReadOnlyList<LevelBar> Bars => _bars;

    /// <summary>動かし方を切り替えられるか（入力デバイスの行だけ）。</summary>
    public bool Modes => _modes;

    public double Minimum { get => _min; set { _min = value; Place(); } }

    public double Maximum { get => _max; set { _max = value; Place(); } }

    /// <summary>ゲイン（dB）。0.5 刻み。左右それぞれのときは左のゲイン、バランスのときは音量。</summary>
    public double Value
    {
        get => _value;
        set
        {
            var v = Math.Clamp(Math.Round(value * 2) / 2, _min, _max);
            if (v == _value) return;
            _value = v;
            Changed();
        }
    }

    /// <summary>右のゲイン（dB。左右それぞれのときだけ使う）。</summary>
    public double ValueR
    {
        get => _valueR;
        set
        {
            var v = Math.Clamp(Math.Round(value * 2) / 2, _min, _max);
            if (v == _valueR) return;
            _valueR = v;
            Changed();
        }
    }

    /// <summary>バランス（−1＝左いっぱい〜+1＝右いっぱい。バランスのときだけ使う）。</summary>
    public double Balance
    {
        get => _balance;
        set
        {
            var v = Math.Clamp(Math.Round(value * 20) / 20, -1, 1);
            if (v == _balance) return;
            _balance = v;
            Changed();
        }
    }

    /// <summary>動かし方（0 左右一括／1 左右それぞれ／2 バランス）。modes でないときは常に 0。</summary>
    public int Mode
    {
        get => _mode;
        set
        {
            int v = _modes ? Math.Clamp(value, 0, 2) : 0;
            if (v == _mode) return;
            if (v == 1) _valueR = _value;   // 左右それぞれにしたときは、いまの音量を左右にそのまま引き継ぐ
            _mode = v;
            if (v != 2) _balance = 0;
            Changed();
        }
    }

    /// <summary>左右それぞれのゲイン（dB）。</summary>
    public ChGain Channels => GainMath.Channels(_mode, _value, _valueR, _balance);

    /// <summary>左右どちらかがミュートか（文字の色を変えるのに使う）。</summary>
    public bool AnyMuted => GainMath.IsMuted(_value) || (_mode == 1 && GainMath.IsMuted(_valueR));

    /// <summary>保存してあった状態をそのまま戻す（読み込み時。mode が範囲外なら 0）。</summary>
    public void SetState(int mode, double value, double valueR, double balance)
    {
        _mode = _modes ? Math.Clamp(mode, 0, 2) : 0;
        _value = Math.Clamp(Math.Round(value * 2) / 2, _min, _max);
        _valueR = Math.Clamp(Math.Round(valueR * 2) / 2, _min, _max);
        _balance = _mode == 2 ? Math.Clamp(Math.Round(balance * 20) / 20, -1, 1) : 0;
        Place();
        UpdateMute();
    }

    private void Changed()
    {
        Place();
        UpdateMute();
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>ミュート中か。メーターは棒を出さず、つまみも橙になる。</summary>
    public bool Muted
    {
        get => _muted;
        set
        {
            if (_modes) return;   // 動かし方を切り替えられる行では、左右ごとに自分で決める（UpdateMute）
            if (_muted == value) return;
            _muted = value;
            foreach (var b in _bars) b.Muted = value;
            SetMutedLook(_thumb, value);
        }
    }

    private static void SetMutedLook(Border thumb, bool muted)
    {
        if (muted) thumb.Background = MeterColors.Muted;
        else thumb.SetResourceReference(Border.BackgroundProperty, "Pastel");
    }

    /// <summary>左右それぞれのミュートの見た目（棒とつまみ）を合わせる。</summary>
    private void UpdateMute()
    {
        if (!_modes) return;
        bool l = GainMath.IsMuted(_value);
        bool r = _mode == 1 ? GainMath.IsMuted(_valueR) : l;
        _bars[0].Muted = l;
        _bars[1].Muted = r;
        SetMutedLook(_thumb, l);
        SetMutedLook(_thumbR, r);
    }

    public void Reset() { foreach (var b in _bars) b.Reset(); }

    /// <summary>数字の表示（「+2.0 dB」など）をダブルクリックすると 0 dB（バランスは中央）に戻す。メーター自体のダブルクリックでは戻さない（modes のときは、そこはミュート）。</summary>
    public void AttachReset(TextBlock text)
    {
        text.Background = Brushes.Transparent;   // 文字のすき間でも反応するように
        text.Cursor = Cursors.Hand;
        text.ToolTip = Loc.Get("gainmeter.s01");
        text.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount < 2) return;
            Value = 0;
            if (_modes) { ValueR = 0; Balance = 0; }
            e.Handled = true;
        };
    }

    /// <summary>ミュートを入れる／解く（解くときは、ミュートする前のゲインに戻す。無ければ 0 dB）。ch＝0 左（または一括）／1 右（左右それぞれのとき）。</summary>
    private void ToggleMute(int ch)
    {
        if (ch == 1 && _mode == 1)
        {
            if (!GainMath.IsMuted(_valueR)) { _beforeR = _valueR; ValueR = _min; }
            else ValueR = _beforeR > _min ? _beforeR : 0;
        }
        else
        {
            if (!GainMath.IsMuted(_value)) { _before = _value; Value = _min; }
            else Value = _before > _min ? _before : 0;
        }
    }

    private double BarWidth => Math.Max(1, ActualWidth - LevelBar.ClipWidth - LevelBar.Gap);

    private void Place()
    {
        if (ActualWidth <= 0 || _max <= _min) return;
        double w = BarWidth;
        double gap = _mode == 1 && _bars.Count == 2 ? GapSplit : GapNormal;
        if (_bars.Count == 2) _bars[0].Margin = new Thickness(0, 0, 0, gap);
        double stack = _bars.Count * _barHeight + (_bars.Count - 1) * gap;
        double top = Math.Max(0, (ActualHeight - stack) / 2);
        double X(double v, double tw) => (v - _min) / (_max - _min) * w - tw / 2;
        _lblL.Visibility = _lblR.Visibility = _mode == 2 ? Visibility.Visible : Visibility.Collapsed;
        if (_mode == 1)
        {
            // 左右それぞれ: L・R の棒に1つずつ（背の低いつまみ。ほんのわずかに大きめ）
            double tw = 9, h = _barHeight + 6;
            _thumb.Width = _thumbR.Width = tw;
            _thumb.Height = _thumbR.Height = h;
            _thumb.VerticalAlignment = _thumbR.VerticalAlignment = VerticalAlignment.Top;
            _thumb.Margin = new Thickness(X(_value, tw), top - 3, 0, 0);
            _thumbR.Margin = new Thickness(X(_valueR, tw), top + _barHeight + gap - 3, 0, 0);
            _thumbR.Visibility = Visibility.Visible;
            _center.Visibility = Visibility.Collapsed;
            return;
        }
        _thumb.Width = 8;
        _thumb.Height = stack + 8;
        _thumb.VerticalAlignment = VerticalAlignment.Center;
        _thumbR.Visibility = Visibility.Collapsed;
        if (_mode == 2)
        {
            _thumb.Margin = new Thickness((_balance + 1) / 2 * w - _thumb.Width / 2, 0, 0, 0);
            _center.Margin = new Thickness(w / 2 - 0.5, 0, 0, 0);
            _center.Visibility = Visibility.Visible;
        }
        else
        {
            _thumb.Margin = new Thickness(X(_value, 8), 0, 0, 0);
            _center.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>左右それぞれのとき、押した位置が上半分なら左（0）、下半分なら右（1）。</summary>
    private int ChannelAt(Point p) => _mode == 1 && p.Y >= ActualHeight / 2 ? 1 : 0;

    private void SetFrom(Point p)
    {
        double f = Math.Clamp(p.X / BarWidth, 0, 1);
        if (_mode == 2)
        {
            double b = Math.Clamp(f * 2 - 1, -1, 1);
            Balance = Math.Abs(b) < 0.05 ? 0 : b;   // 中央付近はぴったり中央に吸い付く
        }
        else if (_mode == 1 && _dragCh == 1) ValueR = _min + f * (_max - _min);
        else Value = _min + f * (_max - _min);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var p = e.GetPosition(this);
        if (_modes && e.ClickCount >= 2)
        {
            ToggleMute(ChannelAt(p));   // どの動かし方でも、ダブルクリックでミュート
            e.Handled = true;
            return;
        }
        _drag = true;
        _dragCh = ChannelAt(p);
        CaptureMouse();
        SetFrom(p);
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
