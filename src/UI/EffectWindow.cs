using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>エフェクトウィンドウ（M11-4）がメイン画面（MainWindow）に頼む操作と、読み取る状態。</summary>
public interface IEffectHost
{
    /// <summary>入力1〜4（スロット）の名前と、いまデバイスが選ばれて使われているか。</summary>
    IReadOnlyList<(int Slot, string Name, bool Active)> Slots();

    /// <summary>入力 slot のエフェクト設定（編集用のコピー）。</summary>
    EffectSetting GetEffect(int slot);

    /// <summary>入力 slot のエフェクト設定を差し替える（録音・出力へ即反映し、設定ファイルへは少し遅れて保存する）。</summary>
    void SetEffect(int slot, EffectSetting fx);

    /// <summary>録音中か（録音中は「録音に反映」を切り替えない。つなぎ目でプツッと鳴るのを防ぐため）。</summary>
    bool IsRecording { get; }
}

/// <summary>
/// 入力ごとのエフェクトのウィンドウ（M11-4。確認後に、入力の切り替えをタブ式・効果をタブ分けに変更）。
/// 上のタブ＝入力1〜4（選んだ入力の内容だけを作って表示する）。その下に、反映先・コピー・初期値、さらに効果のタブ
/// （イコライザ＝グラフィックEQ・ローカット・パラメトリックEQ／ノイズゲート／エコー・リバーブ）。
/// 開き方: 入力デバイス欄の「FX」ボタン／入力の名前・波形・メーターの右クリック「エフェクト…」。
/// 設定の読み書きは host（MainWindow）経由。エフェクトは動かしている最中も変えられ、すぐ音に反映される。
/// </summary>
public sealed partial class EffectWindow : Window
{
    private static EffectSetting? _clip;   // コピーした効果（反映先の設定は含まない）。ウィンドウを閉じても残す

    private readonly IEffectHost _host;
    private readonly TabControl _slotTabs = new();
    private readonly List<ContentControl> _hosts = new();   // 入力のタブごとの中身の置き場（選んだタブだけ作る）
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private int _slot;
    private EffectSetting _fx = new();
    private bool _building;
    private int _innerIndex;   // 選んでいる効果のタブ（入力を切り替えても維持）
    private TabControl? _tabs;
    private CheckBox? _toRec;
    private Button? _paste;

    public EffectWindow(IEffectHost host, Settings settings, int slot)
    {
        _host = host;
        _slot = Math.Clamp(slot, 0, Settings.MaxInputs - 1);
        Title = "エフェクト";
        Width = 720;
        Height = 780;
        MinWidth = 600;
        MinHeight = 460;
        FontFamily = new FontFamily("Yu Gothic UI, Meiryo UI");
        FontSize = 13;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBg");
        SetResourceReference(ForegroundProperty, "WindowFg");
        ApplyPalette();

        _slotTabs.Margin = new Thickness(16, 12, 16, 8);
        _slotTabs.SelectionChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, _slotTabs) || _building || _slotTabs.SelectedIndex < 0) return;   // 中のタブ・プルダウンの変更は無視
            _slot = _slotTabs.SelectedIndex;
            Load();
        };
        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _slotTabs,
        };
        ThemeManager.Attach(this, settings.Theme);

        RefreshSlots();
        Load();
        _timer.Tick += (_, _) => UpdateEnabled();
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>このウィンドウの中だけ、黄色の強調色を「赤紫〜青」系（紫）に差し替える（スライダー・トグル・タブの下線・グラフの色など）。</summary>
    private void ApplyPalette()
    {
        bool dark = TryFindResource("WindowBg") is SolidColorBrush bg && (bg.Color.R + bg.Color.G + bg.Color.B) / 3 < 128;
        Brush B(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        Resources["Pastel"] = B(dark ? "#A58CFF" : "#6C4FE0");
        Resources["PastelDim"] = B(dark ? "#3E3470" : "#E0D9FA");
        Resources["ToggleOn"] = B(dark ? "#9A86FF" : "#6C4FE0");
        Resources["ToggleKnobOn"] = B(dark ? "#1E1A3A" : "#FFFFFF");
        Resources["GroupFg"] = B(dark ? "#B9A6FF" : "#5A3FC8");
    }

    // ---- 入力のタブ ----
    /// <summary>入力の名前・使われているか・効果が掛かっているかが変わったとき、タブの見出しを作り直す。</summary>
    public void RefreshSlots()
    {
        var slots = _host.Slots();
        _building = true;
        while (_hosts.Count < slots.Count)
        {
            var h = new ContentControl();
            _hosts.Add(h);
            _slotTabs.Items.Add(new TabItem { Content = h });
        }
        for (int i = 0; i < slots.Count; i++)
        {
            var (slot, name, active) = slots[i];
            var tab = (TabItem)_slotTabs.Items[i];
            var e = _host.GetEffect(slot);
            tab.Header = Header(name, Letters(e, "NLGPER"), e.Bypass);
            tab.Opacity = active ? 1.0 : 0.55;
            tab.ToolTip = active ? null : "いまデバイスが選ばれていない（または使わない）入力です。設定はしておけます";
        }
        if (_slotTabs.SelectedIndex != _slot) _slotTabs.SelectedIndex = _slot;
        _building = false;
    }

    /// <summary>入力 slot のエフェクトを表示する（すでに開いているウィンドウを、別の入力へ切り替える）。</summary>
    public void ShowSlot(int slot)
    {
        _slot = Math.Clamp(slot, 0, Settings.MaxInputs - 1);
        RefreshSlots();
        Load();
    }

    /// <summary>設定が丸ごと変わったとき（プリセットの読み込みなど）。</summary>
    public void Reload()
    {
        RefreshSlots();
        Load();
    }

    /// <summary>選んだ入力の中身を作り直す（ほかの入力のタブの中身は空にする）。</summary>
    private void Load()
    {
        _building = true;
        _fx = _host.GetEffect(_slot);
        foreach (var h in _hosts) h.Content = null;
        if (_slot < _hosts.Count) _hosts[_slot].Content = BuildBody();
        UpdateEnabled();
        _building = false;
    }

    // ---- 選んだ入力の中身 ----
    private UIElement BuildBody()
    {
        var root = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        root.Children.Add(BuildTopBar());

        Action changed = () => { Commit(); UpdateMarks(); };
        _tabs = new TabControl { Margin = new Thickness(0, 6, 0, 0) };
        var eq = EffectEq.Build(_fx, changed, Rebuild);
        var fx = new StackPanel();   // ノイズゲート・エコー・リバーブは同じタブに
        fx.Children.Add(EffectSections.Gate(_fx, changed));
        fx.Children.Add(EffectSections.Echo(_fx, changed));
        fx.Children.Add(EffectSections.Reverb(_fx, changed));
        _tabs.Items.Add(new TabItem { Content = eq });
        _tabs.Items.Add(new TabItem { Content = fx });
        _tabs.SelectedIndex = Math.Clamp(_innerIndex, 0, 1);
        _tabs.SelectionChanged += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, _tabs) && _tabs.SelectedIndex >= 0) _innerIndex = _tabs.SelectedIndex;
        };
        UpdateMarks();
        root.Children.Add(_tabs);

        root.Children.Add(new TextBlock
        {
            Text = "掛かる順番: ノイズゲート → ローカット → グラフィックEQ → パラメトリックEQ → エコー → リバーブ。つまみのダブルクリックで初期値に戻ります。",
            FontSize = 11, Opacity = 0.65, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
        });
        return root;
    }

    private void Edit(Action change)
    {
        if (_building) return;
        change();
        Commit();
        UpdateMarks();
    }

    /// <summary>設定を入れ替えたあと、いまの入力の中身を作り直す（ほかのタブの印も更新する）。</summary>
    private void Rebuild()
    {
        Load();
        RefreshSlots();
    }

    /// <summary>画面で変えた設定を、録音・出力へ反映して保存する。</summary>
    private void Commit()
    {
        if (_building) return;
        _host.SetEffect(_slot, _fx.Clone());
    }

    private void UpdateEnabled()
    {
        if (_toRec != null) _toRec.IsEnabled = !_host.IsRecording;
        if (_paste != null) _paste.IsEnabled = _clip != null;
    }
}
