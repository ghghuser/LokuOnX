using System.Windows;
using System.Windows.Controls;
using Recorder.Core;

namespace Recorder.UI;

// エフェクトウィンドウの上部（反映先・コピー・初期値）と、タブ見出しの印（M11-4確認後に EffectWindow.cs から分けた）
public sealed partial class EffectWindow
{
    /// <summary>反映先（録音・出力）・バイパス・コピー／貼り付け・初期値。</summary>
    private UIElement BuildTopBar()
    {
        var head = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        _toRec = new CheckBox { Content = "録音に反映", IsChecked = _fx.ToRecording, Margin = new Thickness(0, 0, 18, 0),
            ToolTip = "オンにすると、エフェクトを掛けた音が録音されます（オフ＝録音は素の音のまま）。録音中は切り替えられません。メーター・無音検出・自動録音開始は、エフェクト前の音で判定します" };
        var toOut = new CheckBox { Content = "出力に反映", IsChecked = _fx.ToOutput, Margin = new Thickness(0, 0, 18, 0),
            ToolTip = "オンにすると、エフェクトを掛けた音が出力（再生デバイス）へ送られます（既定）。オフなら素の音が送られます" };
        var bypass = new CheckBox { Content = "すべてバイパス", IsChecked = _fx.Bypass, ToolTip = "設定は残したまま、効果だけを一時的に切ります（録音・出力とも素の音になります）" };
        _toRec.Click += (_, _) => Edit(() => _fx.ToRecording = _toRec.IsChecked == true);
        toOut.Click += (_, _) => Edit(() => _fx.ToOutput = toOut.IsChecked == true);
        bypass.Click += (_, _) => Edit(() => _fx.Bypass = bypass.IsChecked == true);
        var row2 = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        row2.Children.Add(_toRec);
        row2.Children.Add(toOut);
        row2.Children.Add(bypass);
        head.Children.Add(row2);

        var copy = new Button { Content = "コピー", Padding = new Thickness(12, 3, 12, 3), ToolTip = "この入力の効果の設定をコピーします（録音・出力への反映先は含みません）" };
        copy.Click += (_, _) => { _clip = _fx.Clone(); UpdateEnabled(); };
        _paste = new Button { Content = "貼り付け", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(8, 0, 0, 0), ToolTip = "コピーした効果の設定を、この入力へ貼り付けます（録音・出力への反映先は変わりません）" };
        _paste.Click += (_, _) =>
        {
            if (_clip == null) return;
            _fx.CopySoundFrom(_clip);
            Commit();
            Rebuild();
        };
        var reset = new Button { Content = "すべて初期値に", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(8, 0, 0, 0), ToolTip = "この入力の効果の設定をすべて初期値（何も掛けない）に戻します" };
        reset.Click += (_, _) =>
        {
            _fx.CopySoundFrom(new EffectSetting());
            Commit();
            Rebuild();
        };
        var row3 = new StackPanel { Orientation = Orientation.Horizontal };
        row3.Children.Add(copy);
        row3.Children.Add(_paste);
        row3.Children.Add(reset);
        head.Children.Add(row3);
        return head;
    }

    /// <summary>効果のタブの見出しに、そのタブの中で掛かっているものの印（●）を出す。バイパス中は「○」。</summary>
    private void UpdateMarks()
    {
        if (_tabs == null || _tabs.Items.Count < 3) return;
        var c = _fx;
        string Mark(bool on) => on ? (c.Bypass ? "  ○" : "  ●") : "";
        ((TabItem)_tabs.Items[0]).Header = "イコライザ" + Mark(c.EqOn || c.HpfHz >= 20 || c.ParamOn);
        ((TabItem)_tabs.Items[1]).Header = "ノイズゲート" + Mark(c.GateOn);
        ((TabItem)_tabs.Items[2]).Header = "エコー・リバーブ" + Mark(c.EchoOn || c.ReverbOn);
        if (_slot < _slotTabs.Items.Count && _slotTabs.Items[_slot] is TabItem t)
        {
            var name = _host.Slots().FirstOrDefault(s => s.Slot == _slot).Name ?? "";
            t.Header = name + (c.IsActive ? "  ●" : "");
        }
    }
}
