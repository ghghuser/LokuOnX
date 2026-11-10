using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>
/// エフェクトウィンドウの各効果のカード（M11-4）。fx を直接書き換え、そのたびに changed を呼ぶ（画面→反映は EffectWindow が受ける）。
/// 掛かる順番は ノイズゲート → ローカット → グラフィックEQ → パラメトリックEQ → エコー → リバーブ（Core/EffectChain）。
/// </summary>
internal static class EffectSections
{
    public static Border Gate(EffectSetting fx, Action changed)
    {
        var card = EffectUi.Card(EffectUi.Toggle("ノイズゲート", fx.GateOn,
            "小さい音（ノイズ）を自動で下げ、声などの大きい音のときだけ通します。いちばん最初に掛かります", on => { fx.GateOn = on; changed(); }), out var body);
        body.Children.Add(EffectUi.Slide("しきい値", -70, -10, fx.GateThresholdDb, -45, EffectUi.Db,
            v => { fx.GateThresholdDb = v; changed(); }, 1, "この音量を超えると開いて音を通します"));
        body.Children.Add(EffectUi.Slide("戻り", 20, 1000, fx.GateReleaseMs, 200, EffectUi.Ms,
            v => { fx.GateReleaseMs = v; changed(); }, 10, "音が小さくなってから、閉じていく速さ"));
        body.Children.Add(EffectUi.Slide("下げる量", 6, 80, fx.GateReductionDb, 40, v => v.ToString("0") + " dB",
            v => { fx.GateReductionDb = v; changed(); }, 1, "閉じたときに下げる量（大きいほど無音に近くなります）"));
        return card;
    }

    public static Border LowCut(EffectSetting fx, Action changed)
    {
        var card = EffectUi.Card(EffectUi.Title("ローカット", "この周波数より低い音（風・机の振動・エアコンなど）を削ります。「なし」で無効"), out var body);
        body.Children.Add(EffectUi.Slide("周波数", 0, 400, fx.HpfHz, 0, v => v < 20 ? "なし" : v.ToString("0") + " Hz",
            v => { fx.HpfHz = v; changed(); }, 5, "左端付近＝なし。20〜400 Hz（12 dB/oct）"));
        return card;
    }

    /// <summary>10バンドのグラフィックEQ（31 Hz〜16 kHz、各 ±12 dB）。rebuild は、よく使う設定を選んだあとにスライダーを描き直す。</summary>
    public static Border GraphicEq(EffectSetting fx, Action changed, Action rebuild)
    {
        var card = EffectUi.Card(EffectUi.Toggle("グラフィックEQ（10バンド）", fx.EqOn,
            "帯域ごとに音量を上げ下げします（各 ±12 dB）。ローカットの後に掛かります", on => { fx.EqOn = on; changed(); }), out var body);

        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var reset = new Button { Content = "フラットに戻す", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(8, 0, 0, 0) };
        reset.Click += (_, _) => { Array.Clear(fx.Bands, 0, fx.Bands.Length); changed(); rebuild(); };
        DockPanel.SetDock(reset, Dock.Right);
        bar.Children.Add(reset);
        var presets = new ComboBox { ToolTip = "よく使う設定を選ぶと、10個のつまみがその値になります（選んだあとも自由に動かせます）" };
        foreach (var p in EqBands.Presets) presets.Items.Add(p.Name);
        presets.SelectedIndex = -1;
        presets.SelectionChanged += (_, _) =>
        {
            int i = presets.SelectedIndex;
            if (i < 0 || i >= EqBands.Presets.Length) return;
            Array.Copy(EqBands.Presets[i].Db, fx.Bands, EqBands.Count);
            if (!fx.EqOn) fx.EqOn = true;
            changed();
            rebuild();
        };
        bar.Children.Add(presets);
        body.Children.Add(bar);

        var grid = new UniformGrid { Columns = 2 };
        for (int i = 0; i < EqBands.Count; i++)
        {
            int b = i;
            var row = EffectUi.Slide(EqBands.Labels[i] + " Hz", -EqBands.MaxDb, EqBands.MaxDb, fx.Bands[i], 0, EffectUi.Db,
                v => { fx.Bands[b] = v; changed(); }, 0.5, null, 52, 64);
            row.Margin = new Thickness(0, 2, 14, 2);
            grid.Children.Add(row);
        }
        body.Children.Add(grid);
        return card;
    }

    /// <summary>4バンドのパラメトリックEQ。位置（周波数）・ゲイン・幅（Q）を自由に決める。グラフィックEQの後に掛かります。</summary>
    public static Border ParamEq(EffectSetting fx, Action changed)
    {
        var card = EffectUi.Card(EffectUi.Toggle("パラメトリックEQ（4バンド）", fx.ParamOn,
            "位置（周波数）・上げ下げ（ゲイン）・幅（Q。大きいほど狭い）を自由に決めて、気になる音だけを調整します", on => { fx.ParamOn = on; changed(); }), out var body);
        for (int i = 0; i < ParamBand.Count; i++)
        {
            int b = i;
            var pb = fx.Params[i];
            var title = new TextBlock { Text = ParamBand.Names[i], FontSize = 12, FontWeight = FontWeights.SemiBold, Opacity = 0.8, Margin = new Thickness(0, i == 0 ? 0 : 8, 0, 0) };
            body.Children.Add(title);
            body.Children.Add(EffectUi.Slide("周波数", Math.Log10(ParamBand.MinHz), Math.Log10(ParamBand.MaxHz), Math.Log10(pb.Freq), Math.Log10(ParamBand.DefaultFreqs[i]),
                v => EffectUi.Hz(Math.Pow(10, v)), v => { fx.Params[b].Freq = Math.Pow(10, v); changed(); }, 0, "気になる音の高さ（20 Hz〜20 kHz）"));
            body.Children.Add(EffectUi.Slide("ゲイン", -ParamBand.MaxDb, ParamBand.MaxDb, pb.Db, 0, EffectUi.Db,
                v => { fx.Params[b].Db = v; changed(); }, 0.5, "上げる／下げる量（0 dB＝効果なし）"));
            body.Children.Add(EffectUi.Slide("幅（Q）", ParamBand.MinQ, ParamBand.MaxQ, pb.Q, ParamBand.DefaultQ, v => v.ToString("0.0"),
                v => { fx.Params[b].Q = v; changed(); }, 0.1, "大きいほど狭い範囲だけに効きます"));
        }
        return card;
    }

    public static Border Echo(EffectSetting fx, Action changed)
    {
        var card = EffectUi.Card(EffectUi.Toggle("エコー", fx.EchoOn, "やまびこのように音が遅れて繰り返されます", on => { fx.EchoOn = on; changed(); }), out var body);
        body.Children.Add(EffectUi.Slide("間隔", 20, 1000, fx.EchoMs, 300, EffectUi.Ms,
            v => { fx.EchoMs = v; changed(); }, 10, "繰り返しの間隔"));
        body.Children.Add(EffectUi.Slide("繰り返し", 0, 0.9, fx.EchoFeedback, 0.35, EffectUi.Percent,
            v => { fx.EchoFeedback = v; changed(); }, 0.05, "大きいほど何回も繰り返します"));
        body.Children.Add(EffectUi.Slide("ミックス量", 0, 1, fx.EchoMix, 0.3, EffectUi.Percent,
            v => { fx.EchoMix = v; changed(); }, 0.05, "エコーの音の大きさ"));
        return card;
    }

    public static Border Reverb(EffectSetting fx, Action changed)
    {
        var card = EffectUi.Card(EffectUi.Toggle("リバーブ", fx.ReverbOn, "部屋やホールのような響きを足します（最後に掛かります）", on => { fx.ReverbOn = on; changed(); }), out var body);
        body.Children.Add(EffectUi.Slide("部屋の広さ", 0, 1, fx.ReverbRoom, 0.5, EffectUi.Percent,
            v => { fx.ReverbRoom = v; changed(); }, 0.05, "大きいほど響きが長く続きます"));
        body.Children.Add(EffectUi.Slide("高域の減衰", 0, 1, fx.ReverbDamp, 0.5, EffectUi.Percent,
            v => { fx.ReverbDamp = v; changed(); }, 0.05, "大きいほど、響きの高い音が早く消えて、こもった感じになります"));
        body.Children.Add(EffectUi.Slide("ミックス量", 0, 1, fx.ReverbMix, 0.25, EffectUi.Percent,
            v => { fx.ReverbMix = v; changed(); }, 0.05, "響きの音の大きさ"));
        return card;
    }
}
