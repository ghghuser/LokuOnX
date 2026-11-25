using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Gokko.Core;
using Recorder.Core;

namespace Gokko.UI;

/// <summary>パッド1つの見た目（色・名前・キー・進み具合）。操作は MainWindow が受ける。</summary>
public sealed class PadView : Border
{
    private readonly TextBlock _title = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(6) };
    private readonly TextBlock _key = new() { Foreground = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)), FontSize = 11, Margin = new Thickness(6, 3, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _mode = new() { Foreground = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)), FontSize = 11, Margin = new Thickness(0, 3, 6, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
    private readonly Border _bar = new() { Height = 4, Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Width = 0 };
    private readonly string _keyText;
    private bool _lit;

    public int Slot { get; }

    public PadView(int slot, string keyText)
    {
        Slot = slot;
        _keyText = keyText;
        _key.Text = keyText;
        CornerRadius = new CornerRadius(8);
        Margin = new Thickness(4);
        MinWidth = 100;
        MinHeight = 80;
        Cursor = System.Windows.Input.Cursors.Hand;
        AllowDrop = true;
        var g = new Grid { ClipToBounds = true };
        g.Children.Add(_title);
        g.Children.Add(_key);
        g.Children.Add(_mode);
        g.Children.Add(_bar);
        Child = g;
    }

    public string KeyText => _keyText;

    public void Show(PadConfig p)
    {
        if (p.IsEmpty)
        {
            Background = new SolidColorBrush(Color.FromRgb(0x40, 0x44, 0x4C));
            _title.Text = Loc.Get("g.pad.empty");
            _title.Opacity = 0.5;
            _mode.Text = "";
            ToolTip = Loc.Get("g.pad.tip.empty", _keyText);
        }
        else
        {
            Background = _lit ? PadBrushes.Lit(p.Color) : PadBrushes.Fill(p.Color);
            _title.Text = p.Title;
            _title.Opacity = 1;
            _mode.Text = p.Mode switch { PadMode.Loop => "🔁", PadMode.Hold => "✋", _ => "" };
            ToolTip = Loc.Get("g.pad.tip", p.Title, _keyText);
        }
        _colorIndex = p.Color;
        _empty = p.IsEmpty;
    }

    private int _colorIndex;
    private bool _empty = true;

    /// <summary>鳴っているか（明るく）と、進み具合（0〜1。ループ・押している間は端まで進むと戻る）。</summary>
    public void SetPlaying(bool playing, double progress)
    {
        if (playing != _lit)
        {
            _lit = playing;
            if (!_empty) Background = playing ? PadBrushes.Lit(_colorIndex) : PadBrushes.Fill(_colorIndex);
        }
        var w = ActualWidth;
        _bar.Width = playing && w > 0 ? Math.Max(0, w * progress) : 0;
    }
}
