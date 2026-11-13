using Recorder.Core;
using System.Windows;
using System.Windows.Controls;

namespace Recorder.UI;

/// <summary>
/// 出力レベル（dB）と全体ゲインの融合メーター。L・R の2本のメーターと、下に dB の目盛り。2本にまたがる1つのつまみが全体ゲイン（左右は分けない）。
/// 録音中は録音するミックス後の音量、待機中・入力チェック中は選んだデバイスの音量（全体ゲイン後）を出す。
/// </summary>
public sealed class OutputMeter : Grid
{
    private static string DefaultTip => Loc.Get("outmeter.defaultTip");

    private readonly GainMeter _gain = new(2, 11);

    /// <summary>全体ゲインのつまみ（メーターと一体）。</summary>
    public GainMeter Gain => _gain;

    /// <summary>全体ゲインの数値（+2.0 dB など）。見出しの右に出る。</summary>
    public TextBlock GainText { get; } = new() { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right };

    /// <summary>録音コンソール用（XAML から作られる。XAML は引数なしのコンストラクターが必須なので、省略可能な引数では代用できない）。</summary>
    public OutputMeter() : this(Loc.Get("outputmeter.s01"), null)
    {
    }

    /// <param name="heading">見出し。</param>
    /// <param name="tip">ツールチップ（null なら録音コンソール用の説明）。</param>
    public OutputMeter(string heading, string? tip)
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 4; i++) RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 3), LastChildFill = true };
        DockPanel.SetDock(GainText, Dock.Right);
        head.Children.Add(GainText);
        head.Children.Add(new TextBlock { Text = heading, FontSize = 11, Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center });
        SetColumnSpan(head, 2);
        Add(head, 0, 0);
        Add(Label("L"), 1, 0);
        Add(Label("R"), 2, 0);
        SetRowSpan(_gain, 2);
        _gain.VerticalAlignment = VerticalAlignment.Top;
        Add(_gain, 1, 1);
        var scale = new LevelScale { Margin = new Thickness(0, 2, 0, 0) };
        Add(scale, 3, 1);
        ToolTip = tip ?? DefaultTip;
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text, FontSize = 11, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center,
        LineHeight = 11, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Margin = new Thickness(0, 0, 0, 1),
    };

    private void Add(UIElement e, int row, int col)
    {
        SetRow(e, row);
        SetColumn(e, col);
        Children.Add(e);
    }

    /// <summary>左右のピーク（0.0〜1.0超）を渡す（50msごと）。</summary>
    public void Update(float left, float right)
    {
        _gain.Bars[0].Update(left);
        _gain.Bars[1].Update(right);
    }

    /// <summary>メーターを下げる（音が来ていないとき）。</summary>
    public void Fade() => Update(0, 0);

    public void Reset() => _gain.Reset();
}
