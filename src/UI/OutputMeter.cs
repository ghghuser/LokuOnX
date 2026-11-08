using System.Windows;
using System.Windows.Controls;

namespace Recorder.UI;

/// <summary>
/// 出力レベル（dB）と全体ゲインの融合メーター。L・R の2本のメーターと、下に dB の目盛り。2本にまたがる1つのつまみが全体ゲイン（左右は分けない）。
/// 録音中は録音するミックス後の音量、待機中・入力チェック中は選んだデバイスの音量（全体ゲイン後）を出す。
/// </summary>
public sealed class OutputMeter : Grid
{
    private const string Tip = "出力の音量（左右）。白い線＝直近の最大値、右端の四角が赤＝クリップ。縦長のつまみ＝全体ゲイン（ミックス後・録音するファイルに掛かる。左端＝ミュート、ダブルクリックで 0 dB、録音中も調整可能）";

    private readonly GainMeter _gain = new(2, 11);

    /// <summary>全体ゲインのつまみ（メーターと一体）。</summary>
    public GainMeter Gain => _gain;

    /// <summary>全体ゲインの数値（+2.0 dB など）。見出しの右に出る。</summary>
    public TextBlock GainText { get; } = new() { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right };

    public OutputMeter()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 4; i++) RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 3), LastChildFill = true };
        DockPanel.SetDock(GainText, Dock.Right);
        head.Children.Add(GainText);
        head.Children.Add(new TextBlock { Text = "出力レベル(dB)・全体ゲイン", FontSize = 11, Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center });
        SetColumnSpan(head, 2);
        Add(head, 0, 0);
        Add(Label("L"), 1, 0);
        Add(Label("R"), 2, 0);
        SetRowSpan(_gain, 2);
        _gain.VerticalAlignment = VerticalAlignment.Top;
        Add(_gain, 1, 1);
        var scale = new LevelScale { Margin = new Thickness(0, 2, 0, 0) };
        Add(scale, 3, 1);
        ToolTip = Tip;
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
