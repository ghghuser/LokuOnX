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
/// 入力ごとのエフェクトのウィンドウ（M11-4）。パネルは1枚だけで、上の入力の切り替えで移る。
/// 開き方: 入力デバイス欄の「FX」ボタン／入力の名前・波形・メーターの右クリック「エフェクト…」。
/// 設定の読み書きは host（MainWindow）経由。エフェクトは動かしている最中も変えられ、すぐ音に反映される。
/// </summary>
public sealed class EffectWindow : Window
{
    private static EffectSetting? _clip;   // コピーした効果（反映先の設定は含まない）。ウィンドウを閉じても残す

    private readonly IEffectHost _host;
    private readonly StackPanel _slotBar = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _sections = new();
    private readonly CheckBox _toRec = new() { Content = "録音に反映", Margin = new Thickness(0, 0, 18, 0) };
    private readonly CheckBox _toOut = new() { Content = "出力に反映", Margin = new Thickness(0, 0, 18, 0) };
    private readonly CheckBox _bypass = new() { Content = "すべてバイパス" };
    private readonly Button _paste = new() { Content = "貼り付け", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(8, 0, 0, 0) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private int _slot;
    private EffectSetting _fx = new();
    private bool _building;

    public EffectWindow(IEffectHost host, Settings settings, int slot)
    {
        _host = host;
        _slot = Math.Clamp(slot, 0, Settings.MaxInputs - 1);
        Title = "エフェクト";
        Width = 640;
        Height = 780;
        MinWidth = 520;
        MinHeight = 420;
        FontFamily = new FontFamily("Yu Gothic UI, Meiryo UI");
        FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBg");
        SetResourceReference(ForegroundProperty, "WindowFg");

        var dock = new DockPanel();
        dock.Children.Add(BuildHeader());
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _sections,
        };
        _sections.Margin = new Thickness(16, 6, 8, 8);
        dock.Children.Add(scroll);
        Content = dock;
        ThemeManager.Attach(this, settings.Theme);

        RefreshSlots();
        Load();
        _timer.Tick += (_, _) => UpdateEnabled();
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    // ---- 上のバー（入力の切り替え・反映先・コピー） ----
    private UIElement BuildHeader()
    {
        var head = new StackPanel { Margin = new Thickness(16, 12, 16, 6) };
        DockPanel.SetDock(head, Dock.Top);

        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        row1.Children.Add(new TextBlock { Text = "入力", Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
        row1.Children.Add(_slotBar);
        head.Children.Add(row1);

        _toRec.ToolTip = "オンにすると、エフェクトを掛けた音が録音されます（オフ＝録音は素の音のまま）。録音中は切り替えられません。メーター・無音検出・自動録音開始は、エフェクト前の音で判定します";
        _toOut.ToolTip = "オンにすると、エフェクトを掛けた音が出力（再生デバイス）へ送られます（既定）。オフなら素の音が送られます";
        _bypass.ToolTip = "設定は残したまま、効果だけを一時的に切ります（録音・出力とも素の音になります）";
        _toRec.Click += (_, _) => Edit(() => _fx.ToRecording = _toRec.IsChecked == true);
        _toOut.Click += (_, _) => Edit(() => _fx.ToOutput = _toOut.IsChecked == true);
        _bypass.Click += (_, _) => Edit(() => _fx.Bypass = _bypass.IsChecked == true);
        var row2 = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        row2.Children.Add(_toRec);
        row2.Children.Add(_toOut);
        row2.Children.Add(_bypass);
        head.Children.Add(row2);

        var copy = new Button { Content = "コピー", Padding = new Thickness(12, 3, 12, 3), ToolTip = "この入力の効果の設定をコピーします（録音・出力への反映先は含みません）" };
        copy.Click += (_, _) => { _clip = _fx.Clone(); UpdateEnabled(); };
        _paste.ToolTip = "コピーした効果の設定を、この入力へ貼り付けます（録音・出力への反映先は変わりません）";
        _paste.Click += (_, _) =>
        {
            if (_clip == null) return;
            _fx.CopySoundFrom(_clip);
            Commit();
            Load();
        };
        var reset = new Button { Content = "すべて初期値に", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(8, 0, 0, 0), ToolTip = "この入力の効果の設定をすべて初期値（何も掛けない）に戻します" };
        reset.Click += (_, _) =>
        {
            _fx.CopySoundFrom(new EffectSetting());
            Commit();
            Load();
        };
        var row3 = new StackPanel { Orientation = Orientation.Horizontal };
        row3.Children.Add(copy);
        row3.Children.Add(_paste);
        row3.Children.Add(reset);
        head.Children.Add(row3);

        head.Children.Add(new TextBlock
        {
            Text = "掛かる順番: ノイズゲート → ローカット → グラフィックEQ → パラメトリックEQ → エコー → リバーブ。つまみのダブルクリックで初期値に戻ります。",
            FontSize = 12, Opacity = 0.65, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
        });
        return head;
    }

    // ---- 入力の切り替え ----
    /// <summary>入力の名前・使われているかが変わったとき（メイン画面で名前やデバイスを変えたとき）に、切り替えのラジオを作り直す。</summary>
    public void RefreshSlots()
    {
        _building = true;
        _slotBar.Children.Clear();
        foreach (var (slot, name, active) in _host.Slots())
        {
            int s = slot;
            var r = new RadioButton
            {
                Content = name, GroupName = "FxSlot", IsChecked = slot == _slot, Margin = new Thickness(0, 0, 18, 0),
                Opacity = active ? 1.0 : 0.55,
                ToolTip = active ? null : "いまデバイスが選ばれていない（または使わない）入力です。設定はしておけます",
            };
            r.Checked += (_, _) => { if (!_building) ShowSlot(s); };
            _slotBar.Children.Add(r);
        }
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

    private void Load()
    {
        _building = true;
        _fx = _host.GetEffect(_slot);
        _toRec.IsChecked = _fx.ToRecording;
        _toOut.IsChecked = _fx.ToOutput;
        _bypass.IsChecked = _fx.Bypass;
        BuildSections();
        UpdateEnabled();
        _building = false;
    }

    private void BuildSections()
    {
        _sections.Children.Clear();
        Action changed = Commit;
        _sections.Children.Add(EffectSections.Gate(_fx, changed));
        _sections.Children.Add(EffectSections.LowCut(_fx, changed));
        _sections.Children.Add(EffectSections.GraphicEq(_fx, changed, BuildSections));
        _sections.Children.Add(EffectSections.ParamEq(_fx, changed));
        _sections.Children.Add(EffectSections.Echo(_fx, changed));
        _sections.Children.Add(EffectSections.Reverb(_fx, changed));
    }

    private void Edit(Action change)
    {
        if (_building) return;
        change();
        Commit();
    }

    /// <summary>画面で変えた設定を、録音・出力へ反映して保存する。</summary>
    private void Commit()
    {
        if (_building) return;
        _host.SetEffect(_slot, _fx.Clone());
    }

    private void UpdateEnabled()
    {
        _toRec.IsEnabled = !_host.IsRecording;
        _paste.IsEnabled = _clip != null;
    }
}
