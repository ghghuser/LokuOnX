using System.Windows;
using System.Windows.Media;

namespace Recorder.UI;

/// <summary>メーターの色（緑）。波形・メーターで共通。</summary>
public static class MeterColors
{
    public static readonly Brush Green = Frozen(new SolidColorBrush(Color.FromRgb(0x3D, 0xCB, 0x5C)));
    public static readonly Brush Red = Frozen(new SolidColorBrush(Color.FromRgb(0xE0, 0x3C, 0x3C)));
    public static readonly Brush Blue = Frozen(new SolidColorBrush(Color.FromRgb(0x7D, 0xCB, 0xFF)));   // 波形（明るめの青）
    /// <summary>波形の色（RGB 96,196,255）。全体波形・個別波形で共通。</summary>
    public static readonly Brush WaveBlue = Frozen(new SolidColorBrush(Color.FromRgb(96, 196, 255)));
    public static readonly Brush Yellow = Frozen(new SolidColorBrush(Color.FromRgb(0xF2, 0xC9, 0x4C)));
    /// <summary>ミュート中の色（ゲインの数値・スライダー・メーター）。</summary>
    public static readonly Brush Muted = Frozen(new SolidColorBrush(Color.FromRgb(0xE5, 0x6B, 0x4A)));
    public static readonly Brush MutedBg = Frozen(new SolidColorBrush(Color.FromArgb(0x55, 0xE5, 0x6B, 0x4A)));

    private static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }
}

/// <summary>
/// 1本のレベルメーター（横棒）。音量に合わせて 緑（〜-18 dB）→ 黄（-18〜-6 dB）→ 赤（-6 dB〜）に色が変わる棒＋ピークホールドの白い線＋右端のクリップ表示（普段は灰色の四角、クリップで赤）。
/// ピーク（0.0〜1.0超）を Update で渡す。-60〜0 dB を 0〜1 に割り当てる（LevelScale の目盛りと同じ）。ミュート中は棒を出さず、色を変える。
/// </summary>
public sealed class LevelBar : FrameworkElement
{
    public const double ClipWidth = 10;
    public const double Gap = 4;

    /// <summary>黄色・赤に変わる位置（-60〜0 dB を 0〜1 にしたもの）。-18 dB と -6 dB。</summary>
    private const double YellowFrom = (60 - 18) / 60.0;
    private const double RedFrom = (60 - 6) / 60.0;

    private static readonly Brush Back = Frozen(new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80)));
    private static readonly Brush ClipOff = Frozen(new SolidColorBrush(Color.FromArgb(0x70, 0x80, 0x80, 0x80)));
    private static readonly Brush HoldLine = Frozen(new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)));
    private static readonly Pen HoldEdge = FrozenPen(new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)), 0.5));

    private double _level;
    private double _hold;
    private DateTime _holdAt = DateTime.MinValue;
    private DateTime _clipUntil = DateTime.MinValue;
    private bool _muted;

    public LevelBar() => Height = 9;

    /// <summary>角の丸み（小さいほど四角に近い）。</summary>
    private const double Radius = 1.2;

    /// <summary>ミュート中か。棒は出さず、背景と右端が橙になる。</summary>
    public bool Muted
    {
        get => _muted;
        set
        {
            if (_muted == value) return;
            _muted = value;
            if (value) Reset();
            InvalidateVisual();
        }
    }

    private static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }

    private static Pen FrozenPen(Pen p)
    {
        p.Freeze();
        return p;
    }

    /// <summary>dB（-60〜0）を 0〜1 にする。</summary>
    public static double ToLevel(float peak) => peak <= 0 ? 0 : Math.Clamp((20 * Math.Log10(peak) + 60) / 60, 0, 1);

    /// <summary>新しいピークを渡す（50msごと）。棒はゆっくり下がり、白い線は最大値に約1秒とどまってから下がる。</summary>
    public void Update(float peak)
    {
        var now = DateTime.Now;
        var level = _muted ? 0 : ToLevel(peak);
        _level = Math.Max(level, _level - 0.03);
        if (level >= _hold) { _hold = level; _holdAt = now; }
        else if ((now - _holdAt).TotalSeconds > 1.0) _hold = Math.Max(_level, _hold - 0.012);
        if (!_muted && peak >= 0.99) _clipUntil = now.AddSeconds(1.5);
        InvalidateVisual();
    }

    /// <summary>棒・白い線・クリップ表示を空にする。</summary>
    public void Reset()
    {
        _level = _hold = 0;
        _clipUntil = DateTime.MinValue;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= ClipWidth + Gap || h <= 0) return;
        double barW = w - ClipWidth - Gap;
        double r = Math.Min(Radius, h / 2);
        dc.DrawRoundedRectangle(_muted ? MeterColors.MutedBg : ((TryFindResource("MeterBg") ?? TryFindResource("LaneBg")) as Brush ?? Back), null, new Rect(0, 0, barW, h), r, r);
        if (!_muted && _level > 0.001)
        {
            // 棒の角は丸めたまま、中を 緑・黄・赤 の帯で塗り分ける
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, Math.Max(2, barW * _level), h), r, r));
            dc.DrawRectangle(MeterColors.Green, null, new Rect(0, 0, barW * Math.Min(_level, YellowFrom) + 2, h));
            if (_level > YellowFrom)
                dc.DrawRectangle(MeterColors.Yellow, null,
                    new Rect(barW * YellowFrom, 0, barW * (Math.Min(_level, RedFrom) - YellowFrom), h));
            if (_level > RedFrom)
                dc.DrawRectangle(MeterColors.Red, null, new Rect(barW * RedFrom, 0, barW * (_level - RedFrom), h));
            dc.Pop();
        }
        if (!_muted && _hold > 0.02)
        {
            double x = Math.Min(barW - 2, barW * _hold);
            dc.DrawRectangle(HoldLine, HoldEdge, new Rect(Math.Max(0, x - 1), 0, 2, h));
        }
        bool clip = DateTime.Now < _clipUntil;
        dc.DrawRoundedRectangle(clip ? MeterColors.Red : _muted ? MeterColors.Muted : ClipOff, null,
            new Rect(barW + Gap, 0, ClipWidth, h), r, r);
    }
}
