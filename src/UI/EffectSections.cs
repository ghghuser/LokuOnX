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
        EffectUi.Head(toggle, EffectUi.ResetButton(Loc.Get("effectsections.s01"), () => { resetValues(); changed(); rebuild(); }));

    public static Border Gate(EffectSetting fx, Action changed, Action rebuild)
    {
        var card = EffectUi.Card(Head(EffectUi.Toggle(Loc.Get("effectsections.s02"), fx.GateOn,
            Loc.Get("effectsections.s03"), on => { fx.GateOn = on; changed(); }), () => { var d = new EffectSetting(); fx.GateThresholdDb = d.GateThresholdDb; fx.GateReleaseMs = d.GateReleaseMs; fx.GateReductionDb = d.GateReductionDb; }, changed, rebuild), out var body);
        body.Children.Add(EffectUi.Slide(Loc.Get("effectsections.s04"), -70, -10, fx.GateThresholdDb, -45, EffectUi.Db,
            v => { fx.GateThresholdDb = v; changed(); }, 1, Loc.Get("effectsections.s05")));
        body.Children.Add(EffectUi.Slide(Loc.Get("effectsections.s06"), 20, 1000, fx.GateReleaseMs, 200, EffectUi.Ms,
            v => { fx.GateReleaseMs = v; changed(); }, 10, Loc.Get("effectsections.s07")));
        body.Children.Add(EffectUi.Slide(Loc.Get("effectsections.s08"), 6, 80, fx.GateReductionDb, 40, v => v.ToString("0") + " dB",
            v => { fx.GateReductionDb = v; changed(); }, 1, Loc.Get("effectsections.s09")));
        return card;
    }

    public static Border Echo(EffectSetting fx, Action changed, Action rebuild)
    {
        var card = EffectUi.Card(Head(EffectUi.Toggle(Loc.Get("effectsections.s10"), fx.EchoOn, Loc.Get("effectsections.s11"), on => { fx.EchoOn = on; changed(); }), () => { var d = new EffectSetting(); fx.EchoMs = d.EchoMs; fx.EchoFeedback = d.EchoFeedback; fx.EchoMix = d.EchoMix; }, changed, rebuild), out var body);
        body.Children.Add(EffectUi.Slide(Loc.Get("effectsections.s12"), 20, 1000, fx.EchoMs, 300, EffectUi.Ms,
            v => { fx.EchoMs = v; changed(); }, 10, Loc.Get("effectsections.s13")));
        body.Children.Add(EffectUi.Slide(Loc.Get("effectsections.s14"), 0, 0.9, fx.EchoFeedback, 0.35, EffectUi.Percent,
            v => { fx.EchoFeedback = v; changed(); }, 0.05, Loc.Get("effectsections.s15")));
        body.Children.Add(EffectUi.Slide(Loc.Get("effectsections.s16"), 0, 1, fx.EchoMix, 0.3, EffectUi.Percent,
            v => { fx.EchoMix = v; changed(); }, 0.05, Loc.Get("effectsections.s17")));
        return card;
    }

    public static Border Limiter(EffectSetting fx, Action changed, Action rebuild)
    {
        var card = EffectUi.Card(Head(EffectUi.Toggle(Loc.Get("effectsections.s18"), fx.LimiterOn,
            Loc.Get("effectsections.s19"), on => { fx.LimiterOn = on; changed(); }),
            () => { var d = new EffectSetting(); fx.CeilingDb = d.CeilingDb; fx.ReleaseMs = d.ReleaseMs; }, changed, rebuild), out var body);
        body.Children.Add(EffectUi.Slide(Loc.Get("effectsections.s20"), -24, 0, fx.CeilingDb, -1.0, EffectUi.Db,
            v => { fx.CeilingDb = v; changed(); }, 0.5, Loc.Get("effectsections.s21")));
        body.Children.Add(EffectUi.Slide(Loc.Get("effectsections.s22"), 20, 1000, fx.ReleaseMs, 150, EffectUi.Ms,
            v => { fx.ReleaseMs = v; changed(); }, 10, Loc.Get("effectsections.s23")));
        return card;
    }

    public static Border Reverb(EffectSetting fx, Action changed, Action rebuild)
    {
        var card = EffectUi.Card(Head(EffectUi.Toggle(Loc.Get("effectsections.s24"), fx.ReverbOn, Loc.Get("effectsections.s25"), on => { fx.ReverbOn = on; changed(); }), () => { var d = new EffectSetting(); fx.ReverbRoom = d.ReverbRoom; fx.ReverbDamp = d.ReverbDamp; fx.ReverbMix = d.ReverbMix; }, changed, rebuild), out var body);
        body.Children.Add(EffectUi.Slide(Loc.Get("effectsections.s26"), 0, 1, fx.ReverbRoom, 0.5, EffectUi.Percent,
            v => { fx.ReverbRoom = v; changed(); }, 0.05, Loc.Get("effectsections.s27")));
        body.Children.Add(EffectUi.Slide(Loc.Get("effectsections.s28"), 0, 1, fx.ReverbDamp, 0.5, EffectUi.Percent,
            v => { fx.ReverbDamp = v; changed(); }, 0.05, Loc.Get("effectsections.s29")));
        body.Children.Add(EffectUi.Slide(Loc.Get("effectsections.s16"), 0, 1, fx.ReverbMix, 0.25, EffectUi.Percent,
            v => { fx.ReverbMix = v; changed(); }, 0.05, Loc.Get("effectsections.s30")));
        return card;
    }
}
