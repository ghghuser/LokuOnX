using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Recorder.Core;

namespace Recorder.UI;

// エフェクトウィンドウの上部（反映先・コピー・初期値）と、タブ見出しの印（M11-4確認後に EffectWindow.cs から分けた）
public sealed partial class EffectWindow
{
    /// <summary>反映先（録音・出力。入力のみ）・バイパス・コピー／貼り付け／すべて初期値に（この3つはひとまとまりで、折り返してもばらけない）。</summary>
    private UIElement BuildTopBar()
    {
        var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
        _toRec = null;
        if (_host.HasRouting)
        {
            _toRec = new CheckBox { Content = "録音に反映", IsChecked = _fx.ToRecording, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "オンにすると、エフェクトを掛けた音が録音されます（オフ＝録音は素の音のまま）。録音中は切り替えられません。メーター・無音検出・自動録音開始は、エフェクト前の音で判定します" };
            var toOut = new CheckBox { Content = "出力に反映", IsChecked = _fx.ToOutput, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "オンにすると、エフェクトを掛けた音が出力（再生デバイス）へ送られます（既定）。オフなら素の音が送られます" };
            _toRec.Click += (_, _) => Edit(() => _fx.ToRecording = _toRec.IsChecked == true);
            toOut.Click += (_, _) => Edit(() => _fx.ToOutput = toOut.IsChecked == true);
            row.Children.Add(_toRec);
            row.Children.Add(toOut);
        }
        var bypass = new CheckBox { Content = "すべてバイパス", IsChecked = _fx.Bypass, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = _host.HasRouting ? "設定は残したまま、効果だけを一時的に切ります（録音・出力とも素の音になります）" : "設定は残したまま、効果だけを一時的に切ります" };
        bypass.Click += (_, _) => Edit(() => _fx.Bypass = bypass.IsChecked == true);
        row.Children.Add(bypass);

        string what = _host.HasRouting ? "この入力" : "この出力";
        var copy = new Button { Content = "コピー", Padding = new Thickness(12, 3, 12, 3), ToolTip = what + "の効果の設定をコピーします" + (_host.HasRouting ? "（録音・出力への反映先は含みません）" : "") };
        copy.Click += (_, _) => { _clip = _fx.Clone(); UpdateEnabled(); };
        _paste = new Button { Content = "貼り付け", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(8, 0, 0, 0), ToolTip = "コピーした効果の設定を、" + what + "へ貼り付けます（入力と出力のあいだでも貼り付けられます）" };
        _paste.Click += (_, _) =>
        {
            if (_clip == null) return;
            _fx.CopySoundFrom(_clip);
            Commit();
            Rebuild();
        };
        var reset = new Button { Content = "すべて初期値に", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(8, 0, 0, 0), ToolTip = what + "の効果の設定をすべて初期値（何も掛けない）に戻します" };
        reset.Click += (_, _) =>
        {
            _fx.CopySoundFrom(new EffectSetting());
            Commit();
            Rebuild();
        };
        var set = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        set.Children.Add(copy);
        set.Children.Add(_paste);
        set.Children.Add(reset);
        row.Children.Add(set);
        return row;
    }

    /// <summary>省略文字の並び（掛かる順番）。N＝ノイズゲート Lc＝ローカット G＝グラフィックEQ P＝パラメトリックEQ E＝エコー R＝リバーブ Lm＝リミッター。</summary>
    private const string AllOrder = "N,Lc,G,P,E,R,Lm";

    /// <summary>掛かっている効果の省略文字のうち、order（カンマ区切り）に含まれるものを順に並べる（例 "NGRLm"）。</summary>
    internal static string Letters(EffectSetting c, string order)
    {
        bool On(string t) => t switch
        {
            "N" => c.GateOn, "Lc" => c.HpfHz >= 20, "G" => c.EqOn, "P" => c.ParamOn, "E" => c.EchoOn, "R" => c.ReverbOn, "Lm" => c.LimiterOn, _ => false,
        };
        return string.Concat(order.Split(',').Where(On));
    }

    /// <summary>タブの見出し。名前の後ろに「⚡NGR」のように、掛かっている効果を緑（入力ランプと同じ緑）で出す。バイパス中は灰色。</summary>
    private static TextBlock Header(string name, string letters, bool bypass)
    {
        var tb = new TextBlock();
        tb.Inlines.Add(new Run(name));
        if (letters.Length > 0)
        {
            var mark = new Run("  ⚡" + letters) { FontWeight = FontWeights.SemiBold };
            mark.SetResourceReference(TextElement.ForegroundProperty, bypass ? "DisabledFg" : "LampOn");
            tb.Inlines.Add(mark);
        }
        return tb;
    }

    /// <summary>効果のタブと、選んでいる入力のタブの見出しに、掛かっている効果の印（⚡＋頭文字）を出す。</summary>
    private void UpdateMarks()
    {
        if (_tabs == null || _tabs.Items.Count < 2) return;
        var c = _fx;
        ((TabItem)_tabs.Items[0]).Header = Header("イコライザ", Letters(c, "Lc,G,P"), c.Bypass);
        ((TabItem)_tabs.Items[1]).Header = Header("ノイズゲート・エコー・リバーブ・リミッター", Letters(c, "N,E,R,Lm"), c.Bypass);
        if (_slot < _slotTabs.Items.Count && _slotTabs.Items[_slot] is TabItem t)
        {
            var name = _host.Slots().FirstOrDefault(s => s.Slot == _slot).Name ?? "";
            t.Header = Header(name, Letters(c, AllOrder), c.Bypass);
        }
    }
}
