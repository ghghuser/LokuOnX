using System.Windows;
using System.Windows.Controls;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>
/// 1つの入力（出力のエフェクトでは1つの出力）のエフェクト画面の中身（M15。EffectWindow から分けた）。
/// 上段（反映先・バイパス・コピー・貼り付け・すべて初期値に）＋効果のタブ（イコライザ／ノイズゲート・エコー・リバーブ・リミッター）＋注意書き。
/// タブ式では選んだ入力のぶんだけ作り、カード式ではすべての入力のぶんを並べて作る。設定の読み書きは host 経由（動作中も変えられ、すぐ反映される）。
/// </summary>
internal sealed class EffectSlotView
{
    private static EffectSetting? _clip;   // コピーした効果（反映先の設定は含まない）。ウィンドウを閉じても残す

    private readonly IEffectHost _host;
    private readonly Action<int, EffectSetting> _marks;   // 掛かっている効果の印が変わったとき（タブ・カードの見出しの更新）
    private EffectSetting _fx = new();
    private TabControl? _tabs;
    private CheckBox? _toRec;
    private Button? _paste;
    private bool _building;

    public EffectSlotView(IEffectHost host, Action<int, EffectSetting> marks)
    {
        _host = host;
        _marks = marks;
    }

    /// <summary>入力（出力）の番号。</summary>
    public int Slot { get; set; }

    /// <summary>中身の置き場。作っていない間は空。</summary>
    public ContentControl Root { get; private set; } = new();

    /// <summary>置き場を新しく作り直す（タブ式⇔カード式で置き場所の親が変わるとき、古い親に見た目の親子関係が残って落ちるため）。</summary>
    public void ResetRoot()
    {
        Root.Content = null;
        Root = new ContentControl();
    }

    /// <summary>選んでいる効果のタブ（0＝イコライザ／1＝ノイズゲート…）。</summary>
    public int Inner { get; set; }

    /// <summary>効果のタブを切り替えたとき。</summary>
    public event Action? InnerChanged;

    /// <summary>設定を読み直して、中身を作り直す。</summary>
    public void Load()
    {
        _building = true;
        _fx = _host.GetEffect(Slot);
        Root.Content = BuildBody();
        UpdateEnabled();
        _building = false;
    }

    /// <summary>中身を空にする（使っていないタブの分は作らない）。</summary>
    public void Unload() => Root.Content = null;

    private UIElement BuildBody()
    {
        var root = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        root.Children.Add(BuildTopBar());

        Action changed = () => { Commit(); UpdateMarks(); };
        _tabs = new TabControl { Margin = new Thickness(0, 6, 0, 0) };
        var eq = EffectEq.Build(_fx, changed, Rebuild);
        var fx = new StackPanel();   // ノイズゲート・エコー・リバーブ・リミッターは同じタブに
        fx.Children.Add(EffectSections.Gate(_fx, changed, Rebuild));
        fx.Children.Add(EffectSections.Echo(_fx, changed, Rebuild));
        fx.Children.Add(EffectSections.Reverb(_fx, changed, Rebuild));
        fx.Children.Add(EffectSections.Limiter(_fx, changed, Rebuild));
        _tabs.Items.Add(new TabItem { Content = eq });
        _tabs.Items.Add(new TabItem { Content = fx });
        _tabs.SelectedIndex = Math.Clamp(Inner, 0, 1);
        var tabs = _tabs;
        tabs.SelectionChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, tabs) || tabs.SelectedIndex < 0) return;
            Inner = tabs.SelectedIndex;
            InnerChanged?.Invoke();
        };
        UpdateMarks();
        root.Children.Add(_tabs);

        root.Children.Add(new TextBlock
        {
            Text = (_host.HasRouting ? (Loc.Get("effectwindow.s02") + " ") : (Loc.Get("effectwindow.s03") + " "))
                + Loc.Get("effectwindow.s04"),
            FontSize = 11, Opacity = 0.65, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
        });
        return root;
    }

    /// <summary>反映先（録音・出力。入力のみ）・バイパス・コピー／貼り付け／すべて初期値に（この3つはひとまとまりで、折り返してもばらけない）。</summary>
    private UIElement BuildTopBar()
    {
        var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
        _toRec = null;
        if (_host.HasRouting)
        {
            var toRec = new CheckBox { Content = Loc.Get("effectwindow.top.s01"), IsChecked = _fx.ToRecording, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center,
                ToolTip = Loc.Get("effectwindow.top.s02") };
            var toOut = new CheckBox { Content = Loc.Get("effectwindow.top.s03"), IsChecked = _fx.ToOutput, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center,
                ToolTip = Loc.Get("effectwindow.top.s04") };
            toRec.Click += (_, _) => Edit(() => _fx.ToRecording = toRec.IsChecked == true);
            toOut.Click += (_, _) => Edit(() => _fx.ToOutput = toOut.IsChecked == true);
            _toRec = toRec;
            row.Children.Add(toRec);
            row.Children.Add(toOut);
        }
        var bypass = new CheckBox { Content = Loc.Get("effectwindow.top.s05"), IsChecked = _fx.Bypass, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = _host.HasRouting ? Loc.Get("effectwindow.top.s06") : Loc.Get("effectwindow.top.s07") };
        bypass.Click += (_, _) => Edit(() => _fx.Bypass = bypass.IsChecked == true);
        row.Children.Add(bypass);

        string what = _host.HasRouting ? Loc.Get("effectwindow.top.s08") : Loc.Get("effectwindow.top.s09");
        var copy = new Button { Content = Loc.Get("effectwindow.top.s10"), Padding = new Thickness(12, 3, 12, 3), ToolTip = what + Loc.Get("effectwindow.top.s11") + (_host.HasRouting ? Loc.Get("effectwindow.top.s12") : "") };
        copy.Click += (_, _) => { _clip = _fx.Clone(); UpdateEnabled(); };
        _paste = new Button { Content = Loc.Get("effectwindow.top.s13"), Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(8, 0, 0, 0), ToolTip = Loc.Get("effectwindow.top.s14") + what + Loc.Get("effectwindow.top.s15") };
        _paste.Click += (_, _) =>
        {
            if (_clip == null) return;
            _fx.CopySoundFrom(_clip);
            Commit();
            Rebuild();
        };
        var reset = new Button { Content = Loc.Get("effectwindow.top.s16"), Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(8, 0, 0, 0), ToolTip = what + Loc.Get("effectwindow.top.s17") };
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

    private void Edit(Action change)
    {
        if (_building) return;
        change();
        Commit();
        UpdateMarks();
    }

    /// <summary>設定を入れ替えたあと、この入力の中身を作り直す。</summary>
    private void Rebuild() => Load();

    /// <summary>画面で変えた設定を、録音・出力へ反映して保存する。</summary>
    private void Commit()
    {
        if (_building) return;
        _host.SetEffect(Slot, _fx.Clone());
    }

    /// <summary>効果のタブの見出しと、入力のタブ（カード）の見出しに、掛かっている効果の印（⚡＋頭文字）を出す。</summary>
    private void UpdateMarks()
    {
        if (_tabs != null && _tabs.Items.Count >= 2)
        {
            var c = _fx;
            ((TabItem)_tabs.Items[0]).Header = EffectWindow.Header(Loc.Get("effectwindow.top.s18"), EffectWindow.Letters(c, "Lc,G,P"), c.Bypass);
            ((TabItem)_tabs.Items[1]).Header = EffectWindow.Header(Loc.Get("effectwindow.top.s19"), EffectWindow.Letters(c, "N,E,R,Lm"), c.Bypass);
        }
        _marks(Slot, _fx);
    }

    /// <summary>録音中は「録音に反映」を切り替えない。コピーが無いうちは貼り付けを押せない。</summary>
    public void UpdateEnabled()
    {
        if (_toRec != null) _toRec.IsEnabled = !_host.IsRecording;
        if (_paste != null) _paste.IsEnabled = _clip != null;
    }
}
