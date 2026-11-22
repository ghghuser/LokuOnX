using System.Windows;
using System.Windows.Controls;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>
/// 1つの入力（出力のエフェクトでは1つの出力）のエフェクト画面の中身（M15。EffectWindow から分けた）。
/// 上段（反映先・バイパス・コピー・貼り付け・すべて初期値に）＋効果のタブ（イコライザ／ノイズゲート・コーラス・エコー・リバーブ・リミッター）＋注意書き。
/// タブ式では選んだ入力のぶんだけ作り、カード式ではすべての入力のぶんを並べて作る。設定の読み書きは host 経由（動作中も変えられ、すぐ反映される）。
/// </summary>
internal sealed class EffectSlotView
{
    private readonly IEffectHost _host;
    private readonly Action<int, EffectSetting> _marks;   // 掛かっている効果の印が変わったとき（タブ・カードの見出しの更新）
    private EffectSetting _fx = new();
    private TabControl? _tabs;
    private CheckBox? _toRec;
    private ComboBox? _presetBox;
    private TextBlock? _presetMsg;
    private string _presetText = "";   // プリセット欄の文字（画面を作り直しても保つ）
    private bool _filling;
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
        var fx = new StackPanel();   // ノイズゲート・コーラス・エコー・リバーブ・リミッターは同じタブに
        fx.Children.Add(EffectSections.Gate(_fx, changed, Rebuild));
        fx.Children.Add(EffectSections.Chorus(_fx, changed, Rebuild));
        fx.Children.Add(EffectSections.Echo(_fx, changed, Rebuild));
        fx.Children.Add(EffectSections.Reverb(_fx, changed, Rebuild));
        fx.Children.Add(EffectSections.Limiter(_fx, changed, Rebuild));
        _tabs.Items.Add(new TabItem { Content = eq });
        _tabs.Items.Add(new TabItem { Content = fx });
        _tabs.Items.Add(new TabItem { Content = EffectSections.Vocal(_fx, changed, Rebuild) });   // ボーカル低減（掛かる順は最初だが、タブは末尾に足した）
        _tabs.SelectedIndex = Math.Clamp(Inner, 0, 2);
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

    /// <summary>反映先（録音・出力。入力のみ）・バイパス・プリセット（M16。旧コピー／貼り付けの置き換え）・すべて初期値に（ひとまとまりで、折り返してもばらけない）。</summary>
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
        var reset = new Button { Content = Loc.Get("effectwindow.top.s16"), Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(8, 0, 0, 0), ToolTip = what + Loc.Get("effectwindow.top.s17") };
        reset.Click += (_, _) =>
        {
            _fx.CopySoundFrom(new EffectSetting());
            Commit();
            Rebuild();
        };
        row.Children.Add(reset);

        // 2段目: プリセット（幅が足りなければ折り返す）。登録・削除などの知らせは、その下に小さく出す
        var top = new StackPanel();
        top.Children.Add(row);
        top.Children.Add(BuildPresetBar());
        top.Children.Add(_presetMsg!);
        return top;
    }

    /// <summary>プリセット（M16）: 名前の欄（選ぶとその設定に置き換わる。書いて「登録」で追加・上書き）・登録・削除・初期プリセットの復元。入力・出力で共通。</summary>
    private UIElement BuildPresetBar()
    {
        var p = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock { Text = Loc.Get("effectwindow.top.s20"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        _presetBox = new ComboBox { Width = 190, IsEditable = true, Tag = "InputBg", ToolTip = Loc.Get("effectwindow.top.s21") };
        var save = new Button { Content = Loc.Get("effectwindow.top.s22"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0), ToolTip = Loc.Get("effectwindow.top.s23") };
        var del = new Button { Content = Loc.Get("effectwindow.top.s24"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0), ToolTip = Loc.Get("effectwindow.top.s25") };
        var restore = new Button { Content = "↺", Width = 26, Padding = new Thickness(0), Margin = new Thickness(6, 0, 0, 0), ToolTip = Loc.Get("effectwindow.top.s26") };
        _presetMsg = new TextBlock { FontSize = 11, Opacity = 0.75, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
        FillPresets(_presetText);
        _presetBox.SelectionChanged += (_, _) =>
        {
            if (_building || _filling || _presetBox.SelectedItem is not string name) return;
            var preset = _host.Presets.Get(name);
            if (preset == null) return;
            _presetText = name;
            _fx.CopySoundFrom(preset.Sound);   // 効果の中身だけ置き換える（録音・出力への反映先は変えない）
            Commit();
            Rebuild();
        };
        save.Click += (_, _) =>
        {
            var name = EffectPresetStore.CleanName(_presetBox.Text);
            if (name.Length == 0) { Say(Loc.Get("effectwindow.top.s27")); return; }
            if (!_host.Presets.Save(name, _fx)) { Say(Loc.Get("effectwindow.top.s29")); return; }
            _presetText = name;
            FillPresets(name);
            Say(Loc.Get("effectwindow.top.s28", name));
        };
        del.Click += (_, _) =>
        {
            var name = EffectPresetStore.CleanName(_presetBox.Text);
            if (_host.Presets.Get(name) == null) { Say(Loc.Get("effectwindow.top.s34", name)); return; }
            if (!ConfirmDialog.Ask(Window.GetWindow(Root), Loc.Get("effectwindow.top.s30", name))) return;
            _host.Presets.Delete(name);
            _presetText = "";
            FillPresets("");
            Say(Loc.Get("effectwindow.top.s32", name));
        };
        restore.Click += (_, _) =>
        {
            if (!ConfirmDialog.Ask(Window.GetWindow(Root), Loc.Get("effectwindow.top.s31"))) return;
            _host.Presets.RestoreDefaults();
            FillPresets(_presetBox.Text);
            Say(Loc.Get("effectwindow.top.s33"));
        };
        p.Children.Add(label);
        p.Children.Add(_presetBox);
        p.Children.Add(save);
        p.Children.Add(del);
        p.Children.Add(restore);
        return p;
    }

    /// <summary>プリセットの一覧を入れ直し、欄の文字を text にする（一覧にある名前なら選んだ状態にする）。</summary>
    private void FillPresets(string text)
    {
        if (_presetBox == null) return;
        _filling = true;
        _presetBox.Items.Clear();
        foreach (var p in _host.Presets.List()) _presetBox.Items.Add(p.Name);
        _presetBox.Text = text;
        _filling = false;
    }

    private void Say(string text)
    {
        if (_presetMsg != null) _presetMsg.Text = text;
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
        if (_tabs != null && _tabs.Items.Count >= 3)
        {
            var c = _fx;
            ((TabItem)_tabs.Items[0]).Header = EffectWindow.Header(Loc.Get("effectwindow.top.s18"), EffectWindow.Letters(c, "Lc,G,P"), c.Bypass);
            ((TabItem)_tabs.Items[1]).Header = EffectWindow.Header(Loc.Get("effectwindow.top.s19"), EffectWindow.Letters(c, "N,C,E,R,Lm"), c.Bypass);
            ((TabItem)_tabs.Items[2]).Header = EffectWindow.Header(Loc.Get("effectwindow.top.s35"), EffectWindow.Letters(c, "V"), c.Bypass);
        }
        _marks(Slot, _fx);
    }

    /// <summary>録音中は「録音に反映」を切り替えない。コピーが無いうちは貼り付けを押せない。</summary>
    public void UpdateEnabled()
    {
        if (_toRec != null) _toRec.IsEnabled = !_host.IsRecording;
    }
}
