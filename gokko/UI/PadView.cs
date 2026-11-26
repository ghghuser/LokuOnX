using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Gokko.Core;
using Recorder.Core;

namespace Gokko.UI;

/// <summary>パッド1つの見た目。色・名前・キー・ループの印・再生の進み（左から右へ薄い色が流れ、曲の終わりが右端）。操作は MainWindow が受ける。</summary>
public sealed class PadView : Border
{
    private static readonly Brush FillBrush = Frozen(Color.FromArgb(70, 255, 255, 255));
    private static readonly Brush DimText = Frozen(Color.FromArgb(150, 255, 255, 255));

    private readonly TextBlock _title = new() { TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(6, 14, 6, 14), MaxHeight = 60 };
    private readonly TextBlock _key = new() { Foreground = DimText, FontSize = 10, Margin = new Thickness(7, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
    private readonly Border _fill = new() { Background = FillBrush, HorizontalAlignment = HorizontalAlignment.Left, Width = 0, CornerRadius = new CornerRadius(8, 0, 0, 8), IsHitTestVisible = false };
    private readonly Border _loop = new() { CornerRadius = new CornerRadius(5), Padding = new Thickness(5, 0, 5, 1), Margin = new Thickness(0, 0, 5, 4), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Cursor = System.Windows.Input.Cursors.Hand };
    private readonly TextBlock _loopText = new() { Text = "↻", FontSize = 13, FontFamily = new FontFamily("Segoe UI Symbol") };
    private int _color;
    private bool _empty = true;
    private bool _lit;

    public int Id { get; }

    /// <summary>ループの印を押した。</summary>
    public event Action<PadView>? LoopClicked;

    public PadView(int id)
    {
        Id = id;
        CornerRadius = new CornerRadius(8);
        Margin = new Thickness(0);
        MinWidth = 56;
        MinHeight = 44;
        Cursor = System.Windows.Input.Cursors.Hand;
        AllowDrop = true;
        SnapsToDevicePixels = true;
        _loop.Child = _loopText;
        _loop.MouseLeftButtonDown += (_, e) => e.Handled = true;
        _loop.MouseLeftButtonUp += (_, e) => { e.Handled = true; LoopClicked?.Invoke(this); };
        var g = new Grid { ClipToBounds = true };
        g.Children.Add(_fill);
        g.Children.Add(_title);
        g.Children.Add(_key);
        g.Children.Add(_loop);
        Child = g;
    }

    public void Show(PadConfig p, string keyText)
    {
        _key.Text = keyText;
        _empty = p.IsEmpty;
        _color = p.Color;
        if (_empty)
        {
            SetResourceReference(BackgroundProperty, "CtrlBg");
            _title.Text = "＋";
            _title.Foreground = DimText;
            _title.FontSize = 18;
            _loop.Visibility = Visibility.Collapsed;
            ToolTip = Loc.Get("g.pad.tip.empty", keyText);
            return;
        }
        _title.Text = p.Title;
        _title.Foreground = Brushes.White;
        _title.FontSize = 13;
        _loop.Visibility = Visibility.Visible;
        var loop = p.Mode == PadMode.Loop;
        _loopText.Text = p.Mode == PadMode.Hold ? "✋" : "↻";
        _loop.Background = loop ? Brushes.White : Brushes.Transparent;
        _loopText.Foreground = loop ? Brushes.Black : DimText;
        _loop.ToolTip = Loc.Get(p.Mode == PadMode.Hold ? "g.pad.loop.hold" : "g.pad.loop.tip");
        Background = _lit ? PadBrushes.Lit(_color) : PadBrushes.Fill(_color);
        ToolTip = Loc.Get("g.pad.tip", p.Title, keyText);
    }

    /// <summary>鳴っているか、進み具合（0〜1）。薄い色が左から右へ流れ、曲の終わりが右端になる。</summary>
    public void SetPlaying(bool playing, double progress)
    {
        if (playing != _lit)
        {
            _lit = playing;
            if (!_empty) Background = playing ? PadBrushes.Lit(_color) : PadBrushes.Fill(_color);
        }
        var w = ActualWidth;
        _fill.Width = playing && w > 0 ? Math.Max(0, w * Math.Clamp(progress, 0, 1)) : 0;
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
