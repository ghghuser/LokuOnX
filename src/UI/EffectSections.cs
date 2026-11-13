using System.Windows;
using System.Windows.Controls;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>
/// エフェクトウィンドウの、イコライザ以外の効果のカード（M11-4。ノイズゲート・エコー・リバーブ・リミッター。イコライザ系は EffectEq）。
/// fx を直接書き換え、そのたびに changed を呼ぶ（画面→反映は EffectWindow が受ける）。
/// 掛かる順番は ノイズゲート → ローカット → グラフィックEQ → パラメトリックEQ → エコー → リバーブ（Core/EffectChain）。
/// </summary>
internal static class EffectSections
{
    /// <summary>見出し行（スイッチ＋右端の「↺」）。↺は、その効果のつまみを初期値に戻し（入／切はそのまま）、画面を作り直す。</summary>
    private static DockPanel Head(CheckBox toggle, Action resetValues, Action changed, Action rebuild) =>
        EffectUi.Head(toggle, EffectUi.ResetButton("この効果のつまみを初期値に戻します（入／切はそのまま）", () => { resetValues(); changed(); rebuild(); }));

    public static Border Gate(EffectSetting fx, Action changed, Action rebuild)
    {
        var card = EffectUi.Card(Head(EffectUi.Toggle("ノイズゲート", fx.GateOn,
            "小さい音（ノイズ）を自動で下げ、声などの大きい音のときだけ通します。いちばん最初に掛かります", on => { fx.GateOn = on; changed(); }), () => { var d = new EffectSetting(); fx.GateThresholdDb = d.GateThresholdDb; fx.GateReleaseMs = d.GateReleaseMs; fx.GateReductionDb = d.GateReductionDb; }, changed, rebuild), out var body);
        body.Children.Add(EffectUi.Slide("しきい値", -70, -10, fx.GateThresholdDb, -45, EffectUi.Db,
            v => { fx.GateThresholdDb = v; changed(); }, 1, "この音量を超えると開いて音を通します"));
        body.Children.Add(EffectUi.Slide("戻り", 20, 1000, fx.GateReleaseMs, 200, EffectUi.Ms,
            v => { fx.GateReleaseMs = v; changed(); }, 10, "音が小さくなってから、閉じていく速さ"));
        body.Children.Add(EffectUi.Slide("下げる量", 6, 80, fx.GateReductionDb, 40, v => v.ToString("0") + " dB",
            v => { fx.GateReductionDb = v; changed(); }, 1, "閉じたときに下げる量（大きいほど無音に近くなります）"));
        return card;
    }

    public static Border Echo(EffectSetting fx, Action changed, Action rebuild)
    {
        var card = EffectUi.Card(Head(EffectUi.Toggle("エコー", fx.EchoOn, "やまびこのように音が遅れて繰り返されます", on => { fx.EchoOn = on; changed(); }), () => { var d = new EffectSetting(); fx.EchoMs = d.EchoMs; fx.EchoFeedback = d.EchoFeedback; fx.EchoMix = d.EchoMix; }, changed, rebuild), out var body);
        body.Children.Add(EffectUi.Slide("間隔", 20, 1000, fx.EchoMs, 300, EffectUi.Ms,
            v => { fx.EchoMs = v; changed(); }, 10, "繰り返しの間隔"));
        body.Children.Add(EffectUi.Slide("繰り返し", 0, 0.9, fx.EchoFeedback, 0.35, EffectUi.Percent,
            v => { fx.EchoFeedback = v; changed(); }, 0.05, "大きいほど何回も繰り返します"));
        body.Children.Add(EffectUi.Slide("ミックス量", 0, 1, fx.EchoMix, 0.3, EffectUi.Percent,
            v => { fx.EchoMix = v; changed(); }, 0.05, "エコーの音の大きさ"));
        return card;
    }

    public static Border Limiter(EffectSetting fx, Action changed, Action rebuild)
    {
        var card = EffectUi.Card(Head(EffectUi.Toggle("リミッター", fx.LimiterOn,
            "音の大きすぎる部分を、割れないように自動で下げます。上限を超えない音量に収めます（最後に掛かります）", on => { fx.LimiterOn = on; changed(); }),
            () => { var d = new EffectSetting(); fx.CeilingDb = d.CeilingDb; fx.ReleaseMs = d.ReleaseMs; }, changed, rebuild), out var body);
        body.Children.Add(EffectUi.Slide("上限", -24, 0, fx.CeilingDb, -1.0, EffectUi.Db,
            v => { fx.CeilingDb = v; changed(); }, 0.5, "この音量を超えないように下げます（0 dB＝割れる寸前）"));
        body.Children.Add(EffectUi.Slide("戻り時間", 20, 1000, fx.ReleaseMs, 150, EffectUi.Ms,
            v => { fx.ReleaseMs = v; changed(); }, 10, "下げたあと、元の音量に戻るまでの時間"));
        return card;
    }

    public static Border Reverb(EffectSetting fx, Action changed, Action rebuild)
    {
        var card = EffectUi.Card(Head(EffectUi.Toggle("リバーブ", fx.ReverbOn, "部屋やホールのような響きを足します（最後に掛かります）", on => { fx.ReverbOn = on; changed(); }), () => { var d = new EffectSetting(); fx.ReverbRoom = d.ReverbRoom; fx.ReverbDamp = d.ReverbDamp; fx.ReverbMix = d.ReverbMix; }, changed, rebuild), out var body);
        body.Children.Add(EffectUi.Slide("部屋の広さ", 0, 1, fx.ReverbRoom, 0.5, EffectUi.Percent,
            v => { fx.ReverbRoom = v; changed(); }, 0.05, "大きいほど響きが長く続きます"));
        body.Children.Add(EffectUi.Slide("高域の減衰", 0, 1, fx.ReverbDamp, 0.5, EffectUi.Percent,
            v => { fx.ReverbDamp = v; changed(); }, 0.05, "大きいほど、響きの高い音が早く消えて、こもった感じになります"));
        body.Children.Add(EffectUi.Slide("ミックス量", 0, 1, fx.ReverbMix, 0.25, EffectUi.Percent,
            v => { fx.ReverbMix = v; changed(); }, 0.05, "響きの音の大きさ"));
        return card;
    }
}
