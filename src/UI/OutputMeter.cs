using System.Windows;
using System.Windows.Controls;

namespace Recorder.UI;

/// <summary>
/// 出力レベル（dB）。L・R の2本のメーターと、下に dB の目盛り。録音ボタンの上に置く。
/// 録音中は録音するミックス後の音量、待機中・入力チェック中は選んだデバイスの音量（全体ゲイン後）を出す。
/// </summary>
public sealed class OutputMeter : Grid
{
    private readonly LevelBar _l = new();
    private readonly LevelBar _r = new();

    public OutputMeter()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 4; i++) RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var caption = new TextBlock { Text = "出力レベル(dB)", FontSize = 11, Opacity = 0.65, Margin = new Thickness(0, 0, 0, 3) };
        SetColumnSpan(caption, 2);
        Add(caption, 0, 0);
        Add(Label("L"), 1, 0);
        Add(Label("R"), 2, 0);
        _l.Margin = new Thickness(0, 1, 0, 2);
        _r.Margin = new Thickness(0, 1, 0, 0);
        Add(_l, 1, 1);
        Add(_r, 2, 1);
        var scale = new LevelScale { Margin = new Thickness(0, 2, 0, 0) };
        Add(scale, 3, 1);
        ToolTip = "出力の音量（左右）。白い線＝直近の最大値、右端の四角が赤＝クリップ。全体ゲインを掛けたあとの値です";
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text, FontSize = 11, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center,
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
        _l.Update(left);
        _r.Update(right);
    }

    /// <summary>メーターを下げる（音が来ていないとき）。</summary>
    public void Fade() => Update(0, 0);

    public void Reset()
    {
        _l.Reset();
        _r.Reset();
    }
}
