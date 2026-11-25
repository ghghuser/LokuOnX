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

    /// <summary>ウィンドウのタイトル（「入力のエフェクト」「出力のエフェクト」）。</summary>
    string WindowTitle { get; }

    /// <summary>「録音に反映」「出力に反映」を出すか（入力のエフェクトだけ。出力のエフェクトは最終ミックスに掛かるだけ）。</summary>
    bool HasRouting { get; }

    /// <summary>エフェクトのプリセット（入力・出力で共通。M16）。</summary>
    EffectPresetStore Presets { get; }

    /// <summary>ウィンドウの位置と大きさ（"左,上,幅,高さ"。空＝未保存）。入力用・出力用で別々に覚える。</summary>
    string WindowRect { get; set; }
}

/// <summary>
/// エフェクトのウィンドウ（M11-4。入力ごと／11-5確認後に、出力ごとも同じ画面を使うよう統一。違いは IEffectHost だけ）。
/// 表示は2通り（M15。右上の ⊞／🗇 で切り替え。設定 EffectView。既定はタブ）: タブ式＝上のタブで入力1〜4を1つずつ／カード式＝すべての入力を並べる。
/// 各入力の中身は EffectSlotView（反映先・コピー・初期値、効果のタブ）。
/// 開き方: 入力デバイス欄の「FX」ボタン／入力の名前・波形・メーターの右クリック「入力のエフェクト…」。
/// エフェクトは動かしている最中も変えられ、すぐ音に反映される。
/// </summary>
public sealed partial class EffectWindow : Window
{
    private const double CardWidth = 548;   // タブ式で最小サイズのときの中身の幅と同じ

    private readonly IEffectHost _host;
    private readonly Settings _s;
    private readonly TabControl _slotTabs = new() { Margin = new Thickness(16, 2, 16, 4) };
    private readonly WrapPanel _cards = new() { Margin = new Thickness(16, 4, 4, 8) };
    private readonly List<EffectSlotView> _views = new();
    private readonly List<ContentControl> _cardHeads = new();
    private readonly List<Border> _cardBorders = new();
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Button _cardButton = new() { Content = "⊞", Width = 32, Padding = new Thickness(0, 2, 0, 2) };
    private readonly Button _tabButton = new() { Content = "🗇", Width = 32, Padding = new Thickness(0, 2, 0, 2), Margin = new Thickness(4, 0, 0, 0) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private int _slot;
    private bool _building;
    private string _slotSig = "";   // タブの名前・枚数の変化を見つけるための記録
    private int _innerIndex;   // タブ式で選んでいる効果のタブ（入力を切り替えても維持）

    private bool CardMode => _s.EffectView == "card";

    public EffectWindow(IEffectHost host, Settings settings, int slot)
    {
        _host = host;
        _s = settings;
        _slot = Math.Max(0, slot);
        Title = host.WindowTitle;
        Width = 720;
        Height = 780;
        MinWidth = 570;
        MinHeight = 430;
        FontFamily = new FontFamily("Yu Gothic UI, Meiryo UI");
        FontSize = 13;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBg");
        SetResourceReference(ForegroundProperty, "WindowFg");
        ApplyPalette();
        RestoreRect();

        _slotTabs.SelectionChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, _slotTabs) || _building || _slotTabs.SelectedIndex < 0) return;   // 中のタブ・プルダウンの変更は無視
            _slot = _slotTabs.SelectedIndex;
            LoadSelected();
        };
        _cardButton.ToolTip = Loc.Get("effectwindow.view.card.tip");
        _tabButton.ToolTip = Loc.Get("effectwindow.view.tab.tip");
        _cardButton.Click += (_, _) => SwitchView("card");
        _tabButton.Click += (_, _) => SwitchView("tab");
        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 8, 16, 0) };
        bar.Children.Add(_cardButton);
        bar.Children.Add(_tabButton);
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_scroll, 1);
        grid.Children.Add(bar);
        grid.Children.Add(_scroll);
        Content = grid;
        ThemeManager.Attach(this, settings.Theme);

        EnsureViews(_host.Slots().Count);
        Layout();
        _timer.Tick += (_, _) => { SyncSlots(); UpdateEnabled(); };
        _timer.Start();
        Closed += (_, _) =>
        {
            _timer.Stop();
            SaveRect();
        };
    }

    /// <summary>このウィンドウの中だけ、黄色の強調色を「赤紫〜青」系（紫）に差し替える（スライダー・トグル・タブの下線・グラフの色など）。</summary>
    private void ApplyPalette() => EffectPalette.Apply(this);

    // ---- 位置と大きさ（閉じるときに保存し、次に開くときに復元する。タブ式とカード式で別々） ----
    private string RectValue
    {
        get => CardMode ? (_host.HasRouting ? _s.EffectCardRect : _s.OutputFxCardRect) : _host.WindowRect;
        set
        {
            if (!CardMode) _host.WindowRect = value;
            else if (_host.HasRouting) _s.EffectCardRect = value;
            else _s.OutputFxCardRect = value;
        }
    }

    private bool RestoreRect()
    {
        var parts = RectValue.Split(',');
        if (parts.Length != 4) return false;
        var v = new double[4];
        for (int i = 0; i < 4; i++)
            if (!double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i])) return false;
        // 画面の外に出てしまわないときだけ復元する
        double sl = SystemParameters.VirtualScreenLeft, st = SystemParameters.VirtualScreenTop;
        double sw = SystemParameters.VirtualScreenWidth, sh = SystemParameters.VirtualScreenHeight;
        if (v[2] < MinWidth || v[3] < MinHeight || v[0] + 80 > sl + sw || v[0] + v[2] - 80 < sl || v[1] + 40 > st + sh || v[1] < st - 10) return false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = v[0]; Top = v[1]; Width = v[2]; Height = v[3];
        return true;
    }

    private void SaveRect()
    {
        if (WindowState != WindowState.Normal) return;
        RectValue = string.Join(",", new[] { Left, Top, Width, Height }.Select(d => Math.Round(d).ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    // ---- 表示の切り替え（タブ式／カード式） ----
    private void SwitchView(string mode)
    {
        if (_s.EffectView == mode) return;
        SaveRect();   // 切り替える前の表示の大きさを覚える
        _s.EffectView = mode;
        if (!RestoreRect())
        {
            var wa = SystemParameters.WorkArea;
            if (mode == "card") { Width = Math.Min(1320, wa.Width - 40); Height = Math.Min(860, wa.Height - 40); }
            else { Width = 720; Height = Math.Min(780, wa.Height - 40); }
        }
        Layout();
    }

    // ---- 入力ごとの中身 ----
    private void EnsureViews(int count)
    {
        while (_views.Count < count)
        {
            var v = new EffectSlotView(_host, HeaderFor);
            v.InnerChanged += () => { if (!CardMode) _innerIndex = v.Inner; };
            _views.Add(v);
        }
        while (_views.Count > count && _views.Count > 0) _views.RemoveAt(_views.Count - 1);
    }

    /// <summary>画面の組み立て（表示の切り替え・入力の枚数の変化・設定が丸ごと変わったとき）。</summary>
    private void Layout()
    {
        _building = true;
        var slots = _host.Slots();
        foreach (var t in _slotTabs.Items.OfType<TabItem>()) t.Content = null;   // 置き場から外す（親が残ると別の置き場へ移せない）
        _slotTabs.Items.Clear();
        foreach (var b in _cardBorders) b.Child = null;
        _cards.Children.Clear();
        _cardHeads.Clear();
        _cardBorders.Clear();
        foreach (var v in _views) v.ResetRoot();   // 置き場ごと作り直す（古い親に残った親子関係で「Must disconnect…」と落ちるのを防ぐ）
        if (_slot >= slots.Count) _slot = Math.Max(0, slots.Count - 1);
        for (int i = 0; i < _views.Count && i < slots.Count; i++) _views[i].Slot = slots[i].Slot;

        if (CardMode)
        {
            for (int i = 0; i < _views.Count; i++)
            {
                var head = new ContentControl { FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
                var d = new DockPanel();
                DockPanel.SetDock(head, Dock.Top);
                d.Children.Add(head);
                d.Children.Add(_views[i].Root);
                var border = new Border { Width = CardWidth, Margin = new Thickness(0, 0, 12, 12), Padding = new Thickness(12, 8, 12, 8), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = d };
                border.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
                _cards.Children.Add(border);
                _cardBorders.Add(border);
                _cardHeads.Add(head);
                _views[i].Load();
            }
            _scroll.Content = _cards;
        }
        else
        {
            foreach (var v in _views) _slotTabs.Items.Add(new TabItem { Content = v.Root });
            if (_slotTabs.Items.Count > 0) _slotTabs.SelectedIndex = _slot;
            _scroll.Content = _slotTabs;
            LoadSelected();
        }
        _cardButton.Opacity = CardMode ? 1.0 : 0.45;
        _tabButton.Opacity = CardMode ? 0.45 : 1.0;
        UpdateHeaders();
        UpdateEnabled();
        _building = false;
    }

    /// <summary>タブ式: 選んだ入力の中身だけを作る（ほかの入力のタブの中身は空にする）。</summary>
    private void LoadSelected()
    {
        bool was = _building;
        _building = true;
        for (int i = 0; i < _views.Count; i++)
        {
            if (i != _slot) { _views[i].Unload(); continue; }
            _views[i].Inner = _innerIndex;
            _views[i].Load();
        }
        UpdateEnabled();
        _building = was;
    }

    /// <summary>入力の名前・使われているか・効果が掛かっているかが変わったとき、見出しを作り直す。</summary>
    public void RefreshSlots()
    {
        int n = _host.Slots().Count;
        if (n != _views.Count) { EnsureViews(n); Layout(); }
        else UpdateHeaders();
    }

    /// <summary>いま開いている入力（タブ式で選ばれているタブ）。カード式では、同じ入力と見なさない（-1）。</summary>
    public int ShownSlot => CardMode ? -1 : _slot;

    /// <summary>入力 slot のエフェクトを表示する（すでに開いているウィンドウを、別の入力へ切り替える）。</summary>
    public void ShowSlot(int slot)
    {
        _slot = Math.Max(0, slot);
        RefreshSlots();
        if (CardMode)
        {
            if (_slot < _cardBorders.Count) _cardBorders[_slot].BringIntoView();
        }
        else if (_slot < _slotTabs.Items.Count)
        {
            if (_slotTabs.SelectedIndex != _slot) _slotTabs.SelectedIndex = _slot;   // 切り替えで読み込まれる
            else LoadSelected();
        }
    }

    /// <summary>設定が丸ごと変わったとき（プロファイルの読み込みなど）。</summary>
    public void Reload()
    {
        EnsureViews(_host.Slots().Count);
        Layout();
    }

    // ---- 見出し ----
    /// <summary>入力 slot の見出し（名前＋掛かっている効果の印）を、いま画面にある編集中の設定に合わせて更新する。</summary>
    private void HeaderFor(int slot, EffectSetting fx)
    {
        var slots = _host.Slots();
        int i = -1;
        for (int k = 0; k < slots.Count; k++) if (slots[k].Slot == slot) { i = k; break; }
        if (i < 0) return;
        var h = Header(slots[i].Name, Letters(fx, AllOrder), fx.Bypass);
        if (CardMode) { if (i < _cardHeads.Count) _cardHeads[i].Content = h; }
        else if (i < _slotTabs.Items.Count && _slotTabs.Items[i] is TabItem t) t.Header = h;
    }

    private void UpdateHeaders()
    {
        var slots = _host.Slots();
        for (int i = 0; i < slots.Count; i++)
        {
            var (slot, name, active) = slots[i];
            var e = _host.GetEffect(slot);
            var h = Header(name, Letters(e, AllOrder), e.Bypass);
            if (CardMode)
            {
                if (i >= _cardHeads.Count) continue;
                _cardHeads[i].Content = h;
                _cardBorders[i].Opacity = active ? 1.0 : 0.7;
                _cardBorders[i].ToolTip = active ? null : Loc.Get("effectwindow.s01");
            }
            else if (i < _slotTabs.Items.Count && _slotTabs.Items[i] is TabItem tab)
            {
                tab.Header = h;
                tab.Opacity = active ? 1.0 : 0.55;
                tab.ToolTip = active ? null : Loc.Get("effectwindow.s01");
            }
        }
    }

    /// <summary>タブの名前・枚数が変わったら（出力カードの追加・削除・名前の変更）、見出し（枚数が変わったら画面全体）を作り直す。</summary>
    private void SyncSlots()
    {
        var sig = string.Join("|", _host.Slots().Select(s => s.Slot + ":" + s.Name));
        if (sig == _slotSig) return;
        _slotSig = sig;
        RefreshSlots();
    }

    private void UpdateEnabled()
    {
        foreach (var v in _views) v.UpdateEnabled();
    }
}
