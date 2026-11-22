using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Recorder.Core;

namespace Recorder.UI;

// エフェクトウィンドウのタブ・カードの見出しの印（M11-4確認後に EffectWindow.cs から分けた。上段の操作は M15 で EffectSlotView へ移した）
public sealed partial class EffectWindow
{
    /// <summary>省略文字の並び（掛かる順番）。V＝ボーカル低減 N＝ノイズゲート Lc＝ローカット G＝グラフィックEQ P＝パラメトリックEQ E＝エコー R＝リバーブ Lm＝リミッター。</summary>
    internal const string AllOrder = "V,N,Lc,G,P,E,R,Lm";

    /// <summary>掛かっている効果の省略文字のうち、order（カンマ区切り）に含まれるものを順に並べる（例 "N,Lc,G"）。</summary>
    internal static string Letters(EffectSetting c, string order)
    {
        bool On(string t) => t switch
        {
            "V" => c.VocalOn, "N" => c.GateOn, "Lc" => c.HpfHz >= 20, "G" => c.EqOn, "P" => c.ParamOn, "E" => c.EchoOn, "R" => c.ReverbOn, "Lm" => c.LimiterOn, _ => false,
        };
        return string.Join(".", order.Split(',').Where(On));   // 「.」区切り（例 N.Lc.G）
    }

    /// <summary>見出し。名前の後ろに「⚡N,Lc,G」のように、掛かっている効果を緑（入力ランプと同じ緑）で出す。バイパス中は灰色。</summary>
    internal static TextBlock Header(string name, string letters, bool bypass)
    {
        var tb = new TextBlock();
        tb.Inlines.Add(new Run(name));
        if (letters.Length > 0)
        {
            var mark = new Run("  ⚡" + letters) { FontWeight = FontWeights.SemiBold, FontSize = 12 };   // 見出しより1つ小さく
            mark.SetResourceReference(TextElement.ForegroundProperty, bypass ? "DisabledFg" : "LampOn");
            tb.Inlines.Add(mark);
        }
        return tb;
    }
}
