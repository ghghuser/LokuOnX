using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>出力ウィンドウが、メイン画面（MainWindow）に頼む操作と、読み取る状態（M11-3）。</summary>
public interface IOutputHost
{
    OutputHub Hub { get; }

    /// <summary>出力カードの設定（何枚目かは Hub のエンジンの順と同じ）。</summary>
    IReadOnlyList<OutputConfig> Configs { get; }

    /// <summary>送れる入力（メイン画面の入力デバイス欄で選ばれている行）。Slot は行の番号（0始まり）、Name は入力の名前。</summary>
    IReadOnlyList<(int Slot, string Name)> InputSlots();

    /// <summary>index 番目の設定を差し替える（エンジンへ即反映し、設定ファイルへは少し遅れて保存する）。</summary>
    void Update(int index, OutputConfig config);

    /// <summary>入力の名前を変える（メイン画面の入力欄にも反映される。空＝既定の「入力N」）。</summary>
    void RenameInput(int slot, string name);

    /// <summary>この出力先は、いま選んでいる入力の「🔊 ○○」（PC内部音）と同じデバイスか（音が回るので使えない）。</summary>
    bool IsLoopOutput(string id);

    /// <summary>出力先の「実際のデバイスID」（「既定の出力デバイス」は、いまの既定のデバイスのIDに置き換える）。同じデバイスを複数の出力で使っていないかの判定用。</summary>
    string RealOutputId(string id);

    /// <summary>index 番目の出力のエフェクトのウィンドウを開く（すでに開いていればそれを前に出す。M11-5）。</summary>
    void OpenOutputFx(int index, Window owner);

    bool AddOutput();

    void RemoveOutput(int index);

    bool Folded(int index);

    void SetFolded(int index, bool folded);
}

/// <summary>
/// 出力カード1枚（M11-3）。名前・出力先・「出力する」・状態・メーター（出力ごとの全体音量）・入力ごとの送り（チェック＋音量）。
/// 設定は host.Update で保存し、状態の表示は 50ms ごとに window の Tick から更新する。
/// </summary>
public sealed class OutputCard : Border
{
    private static readonly OutputDeviceInfo None = new("", Loc.Get("outputcard.s01"));

    private readonly IOutputHost _host;
    private readonly int _index;
    private readonly Action<int> _removeRequested;
    private readonly TextBox _name = new();
    private readonly CheckBox _on = new();
    private readonly Button _fx = new();
    private readonly ComboBox _device = new();
    private readonly TextBlock _state = new();
    private readonly TextBlock _error = new();
    private readonly TextBlock _caution = new();
    private readonly TextBlock _info = new();
    private readonly OutputMeter _meter = new(Loc.Get("outputcard.s02"),
        Loc.Get("outputcard.s03"));
    private readonly StackPanel _inputsPanel = new();
    private readonly Expander _inputs = new();
    private bool _loading = true;
    private int _fxLook = -1;
    private string _inputSig = "\0";
    private int _slotCount;
    private int _warnTick;
    private bool _sharedWarn;   // 選んでいる出力先を、ほかの出力カードも使っている（黄色の注意）
    private bool _loopWarn;   // 選んでいる出力先が、いまの入力と同じ（音が回る）。「出力する」がオフでも赤文字で知らせる

    private OutputEngine Engine => _host.Hub[_index];
    private OutputConfig Cfg => _host.Configs[_index];

    public OutputCard(IOutputHost host, int index, DataTemplate deviceTemplate, Action<int> removeRequested)
    {
        _host = host;
        _index = index;
        _removeRequested = removeRequested;
        Width = 423;
        Margin = new Thickness(0, 0, 12, 12);
        CornerRadius = new CornerRadius(10);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(16, 12, 16, 12);
        SetResourceReference(BackgroundProperty, "CardBg");
        SetResourceReference(BorderBrushProperty, "CardBorder");
        VerticalAlignment = VerticalAlignment.Top;

        var root = new StackPanel();

        // 1行目: 「出力する」ランプ 名前 ... ✕
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var remove = new Button
        {
            Content = "✕", Width = 24, Height = 24, Padding = new Thickness(0), Margin = new Thickness(10, 0, 0, 0),
            ToolTip = Loc.Get("outputcard.s04"), VerticalAlignment = VerticalAlignment.Center,
        };
        remove.Click += (_, _) => AskRemove();
        DockPanel.SetDock(remove, Dock.Right);
        head.Children.Add(remove);
        // この出力のエフェクト（ローカット・EQ・リミッター。M11-5）。✕の左
        _fx.Content = "FX";
        _fx.Style = (Style)Application.Current.FindResource("FxButton");
        _fx.FontSize = 10;
        _fx.Height = 18;
        _fx.MinWidth = 26;
        _fx.Margin = new Thickness(8, 0, 0, 0);
        _fx.VerticalAlignment = VerticalAlignment.Center;
        _fx.Click += (_, _) => _host.OpenOutputFx(_index, Window.GetWindow(this));
        DockPanel.SetDock(_fx, Dock.Right);
        head.Children.Add(_fx);
        ShowFx();
        // 「出力する」のランプ（入力デバイスの「使う」ランプと同じ見た目・色）。名前の前に置く。文字は付けない
        _on.Style = (Style)Application.Current.FindResource("LampCheck");
        _on.VerticalAlignment = VerticalAlignment.Center;
        _on.Margin = new Thickness(0, 0, 10, 0);
        _on.ToolTip = Loc.Get("outputcard.s05");
        _on.IsChecked = Engine.On;
        _on.Checked += (_, _) => OnToggled();
        _on.Unchecked += (_, _) => OnToggled();
        DockPanel.SetDock(_on, Dock.Left);
        head.Children.Add(_on);
        _name.MaxLength = OutputConfig.MaxNameLength;
        _name.Text = Cfg.Name;
        _name.FontWeight = FontWeights.SemiBold;
        _name.Width = 150;
        _name.HorizontalAlignment = HorizontalAlignment.Left;
        _name.ToolTip = Loc.Get("outputcard.s06");
        _name.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Keyboard.ClearFocus(); e.Handled = true; } };
        _name.LostFocus += (_, _) => CommitName();
        head.Children.Add(_name);
        root.Children.Add(head);

        // 出力先
        var dev = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        dev.Children.Add(new TextBlock { Text = Loc.Get("outputcard.s07"), Width = 52, VerticalAlignment = VerticalAlignment.Center });
        _device.ItemTemplate = deviceTemplate;
        _device.IsEnabled = false;
        _device.ToolTip = Loc.Get("outputcard.s08");
        _device.SelectionChanged += (_, _) => OnDeviceChanged();
        _device.DropDownOpened += (_, _) => UpdateLoopWarnings();
        dev.Children.Add(_device);
        root.Children.Add(dev);

        _state.FontSize = 12;
        _state.Opacity = 0.85;
        _state.Text = Loc.Get("outputcard.s09");
        root.Children.Add(_state);
        _error.SetResourceReference(TextBlock.ForegroundProperty, "WarnFg");
        _error.FontWeight = FontWeights.SemiBold;
        _error.FontSize = 12;
        _error.TextWrapping = TextWrapping.Wrap;
        _error.Margin = new Thickness(0, 4, 0, 0);
        _error.Visibility = Visibility.Collapsed;
        root.Children.Add(_error);
        // 注意（黄）: ほかの出力カードと同じ出力先。使えるが、同じ音が重なって鳴ることがある
        _caution.SetResourceReference(TextBlock.ForegroundProperty, "CautionFg");
        _caution.FontWeight = FontWeights.SemiBold;
        _caution.FontSize = 12;
        _caution.TextWrapping = TextWrapping.Wrap;
        _caution.Margin = new Thickness(0, 4, 0, 0);
        _caution.Visibility = Visibility.Collapsed;
        _caution.Text = Loc.Get("outputcard.s10");
        root.Children.Add(_caution);

        _meter.Margin = new Thickness(0, 10, 0, 0);
        _meter.Gain.Value = Cfg.VolumeDb;
        _meter.Gain.AttachReset(_meter.GainText);   // 数字のダブルクリックで 0 dB
        ShowVolume();
        _meter.Gain.ValueChanged += (_, _) => OnVolume();
        root.Children.Add(_meter);

        // 入力ごとの送り
        _inputs.Header = Loc.Get("outputcard.s11");   // 畳んだときも分かるよう、オンの数を後ろに付ける（ShowInputsHeader）
        _inputs.FontSize = 12;
        _inputs.Margin = new Thickness(0, 10, 0, 0);
        _inputs.IsExpanded = !_host.Folded(_index);
        _inputs.Expanded += (_, _) => { if (!_loading) _host.SetFolded(_index, false); };
        _inputs.Collapsed += (_, _) => { if (!_loading) _host.SetFolded(_index, true); };
        _inputs.Content = _inputsPanel;
        root.Children.Add(_inputs);

        _info.FontSize = 11;
        _info.Opacity = 0.55;
        _info.TextWrapping = TextWrapping.Wrap;
        _info.Margin = new Thickness(0, 8, 0, 0);
        _info.ToolTip = Loc.Get("outputcard.s12");
        root.Children.Add(_info);

        Child = root;
        RefreshInputs();
        _loading = false;
    }

    // ---- 設定の変更 ----
    private void CommitName()
    {
        if (_loading) return;
        var name = OutputConfig.CleanName(_name.Text, OutputConfig.DefaultName(_index));
        _name.Text = name;
        if (name != Cfg.Name) _host.Update(_index, Cfg with { Name = name });
    }

    private void OnToggled()
    {
        if (_loading) return;
        Engine.On = _on.IsChecked == true;
        ShowState();
    }

    /// <summary>「FX」ボタンの見た目（設定があれば光る。バイパス中は薄く）。</summary>
    private void ShowFx()
    {
        var fx = Cfg.Fx;
        int look = fx is { HasAny: true } ? (fx.Bypass ? 2 : 1) : 0;
        if (look == _fxLook) return;   // 変わっていなければ何もしない（光の効果を作り直さない）
        _fxLook = look;
        FxButtonLook.Apply(_fx, fx is { HasAny: true }, fx is { Bypass: true }, Loc.Get("outputcard.s13"),
            Loc.Get("outputcard.s14"));
    }

    private void OnDeviceChanged()
    {
        if (_loading || _device.SelectedItem is not OutputDeviceInfo d) return;
        _host.Update(_index, Cfg with { DeviceId = d.Id });
        UpdateLoopWarnings();
        ShowState();
    }

    private void OnVolume()
    {
        ShowVolume();
        if (_loading) return;
        _host.Update(_index, Cfg with { VolumeDb = _meter.Gain.Value });
    }

    private static string GainLabel(double db) => GainMath.IsMuted(db)
        ? Loc.Get("outputcard.s15")
        : db.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " dB";

    private void ShowVolume()
    {
        var db = _meter.Gain.Value;
        _meter.GainText.Text = GainLabel(db);
        bool muted = GainMath.IsMuted(db);
        _meter.Gain.Muted = muted;
        if (muted)
        {
            _meter.GainText.Foreground = MeterColors.Muted;
            _meter.GainText.FontWeight = FontWeights.SemiBold;
        }
        else
        {
            _meter.GainText.ClearValue(TextBlock.ForegroundProperty);
            _meter.GainText.ClearValue(TextBlock.FontWeightProperty);
        }
    }

    private void AskRemove()
    {
        var owner = Window.GetWindow(this);
        var r = MessageBox.Show(owner, Loc.Get("outputcard.s16", Cfg.Name), Loc.Get("outputcard.s17"),
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (r == MessageBoxResult.Yes) _removeRequested(_index);
    }

    // ---- 出力先 ----
    public void SetDevices(IReadOnlyList<OutputDeviceInfo> list)
    {
        var items = new List<OutputDeviceInfo> { None };
        items.AddRange(list);
        var saved = Cfg.DeviceId;
        if (saved.Length > 0 && !items.Any(d => d.Id == saved))
            items.Add(new OutputDeviceInfo(saved, Loc.Get("outputcard.s18")));   // 選んだ設定は消さない（つながれば自動で使える）
        _loading = true;
        _device.ItemsSource = items;
        _device.SelectedItem = items.FirstOrDefault(d => d.Id == saved) ?? None;
        _device.IsEnabled = true;
        _loading = false;
        UpdateLoopWarnings();
        ShowState();
    }

    /// <summary>プルダウンのうち、いまの入力と組み合わせると音が回る出力先を赤文字にする（選ぶこと自体はできる）。</summary>
    private void UpdateLoopWarnings()
    {
        _loopWarn = _host.IsLoopOutput(Cfg.DeviceId);
        if (_device.ItemsSource is not IEnumerable<OutputDeviceInfo> items) return;
        var red = items.Where(d => d.Id.Length > 0 && _host.IsLoopOutput(d.Id)).Select(d => d.Id).ToHashSet();
        // ほかの出力カードが使っているデバイス（黄色の注意。選ぶことはできる。「既定」と実際のデバイスも同じ扱い）
        var others = _host.Configs.Where((_, i) => i != _index).Select(c => c.DeviceId).Where(id => id.Length > 0)
            .Select(_host.RealOutputId).ToHashSet();
        var yellow = items.Where(d => d.Id.Length > 0 && !red.Contains(d.Id) && others.Contains(_host.RealOutputId(d.Id))).Select(d => d.Id).ToHashSet();
        _sharedWarn = Cfg.DeviceId.Length > 0 && others.Contains(_host.RealOutputId(Cfg.DeviceId));
        _device.Tag = new WarnSet(red, yellow);
    }

    // ---- 入力ごとの送り ----
    /// <summary>メイン画面の入力（行・名前）が変わったとき、入力欄を作り直す。</summary>
    public void RefreshInputs()
    {
        var slots = _host.InputSlots();
        UpdateLoopWarnings();   // 入力の行が同じでも、行のデバイスが変わると音が回るかどうかが変わる
        var sig = string.Join("|", slots.Select(s => s.Slot + ":" + s.Name));
        if (sig == _inputSig) { ShowState(); return; }
        _inputSig = sig;
        var was = _loading;
        _loading = true;
        _inputsPanel.Children.Clear();
        if (slots.Count == 0)
            _inputsPanel.Children.Add(new TextBlock { Text = Loc.Get("outputcard.s19"), Opacity = 0.6, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) });
        foreach (var (slot, name) in slots) _inputsPanel.Children.Add(MakeSendRow(slot, name));
        _slotCount = slots.Count;
        _loading = was;
        ShowInputsHeader();
        UpdateLoopWarnings();   // 入力が変わると、音が回る出力先も変わる
    }

    /// <summary>入力欄の見出し。畳んでいても、いくつの入力がこの出力へ送られているか分かるようにする。</summary>
    private void ShowInputsHeader()
    {
        if (_slotCount == 0) { _inputs.Header = Loc.Get("outputcard.s11"); return; }
        var on = _host.InputSlots().Count(s => Cfg.SendAt(s.Slot).On);
        _inputs.Header = Loc.Get("outputcard.s20", on, _slotCount);
    }

    private UIElement MakeSendRow(int slot, string name)
    {
        var send = Cfg.SendAt(slot);
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0), Height = 24 };
        var check = new CheckBox { IsChecked = send.On, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = Loc.Get("outputcard.s21") };
        // 名前（ダブルクリックか右クリックで変更。メイン画面の入力の名前と同じもの）
        var label = new TextBlock
        {
            Text = name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Cursor = Cursors.IBeam,
            ToolTip = Loc.Get("outputcard.s22"),
        };
        var edit = new TextBox { MaxLength = OutputConfig.MaxNameLength, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(1, 0, 1, 0) };
        var nameHost = new Grid { Width = 86 };
        nameHost.Children.Add(label);
        nameHost.Children.Add(edit);
        void Begin()
        {
            edit.Text = name == OutputConfig.DefaultInputName(slot) ? "" : name;
            label.Visibility = Visibility.Collapsed;
            edit.Visibility = Visibility.Visible;
            edit.Focus();
            edit.SelectAll();
        }
        void End(bool apply)
        {
            if (edit.Visibility != Visibility.Visible) return;
            var text = edit.Text;
            edit.Visibility = Visibility.Collapsed;
            label.Visibility = Visibility.Visible;
            // 名前が変わると入力欄ごと作り直されるので、この操作が終わってから反映する
            if (apply) Dispatcher.BeginInvoke(new Action(() => _host.RenameInput(slot, text)));
        }
        label.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) { Begin(); e.Handled = true; } };
        var rename = new MenuItem { Header = Loc.Get("outputcard.s23") };
        rename.Click += (_, _) => Begin();
        label.ContextMenu = new ContextMenu { Items = { rename } };
        edit.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { End(true); e.Handled = true; }
            else if (e.Key == Key.Escape) { End(false); e.Handled = true; }
        };
        edit.LostFocus += (_, _) => End(true);
        var text = new TextBlock { Width = 62, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        var slider = new Slider
        {
            Minimum = Settings.MinGainDb, Maximum = Settings.MaxGainDb, Value = send.Db, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = Loc.Get("outputcard.s24"),
        };
        void Show()
        {
            text.Text = GainLabel(slider.Value);
            // ミュート（左端）のとき、メイン画面と同じ橙で示す（数字・ダークのスライダー）
            if (GainMath.IsMuted(slider.Value))
            {
                text.Foreground = MeterColors.Muted;
                text.FontWeight = FontWeights.SemiBold;
                slider.Resources["Pastel"] = MeterColors.Muted;
            }
            else
            {
                text.ClearValue(TextBlock.ForegroundProperty);
                text.ClearValue(TextBlock.FontWeightProperty);
                slider.Resources.Remove("Pastel");
            }
        }
        void Commit()
        {
            Show();
            if (_loading) return;
            _host.Update(_index, Cfg.WithSend(slot, new OutSend(check.IsChecked == true, slider.Value)));
            ShowInputsHeader();
        }
        check.Checked += (_, _) => Commit();
        check.Unchecked += (_, _) => Commit();
        slider.ValueChanged += (_, _) => Commit();
        // 0 dB に戻すのは数字のダブルクリック（メーターと同じ。11-4確認後）
        text.Background = System.Windows.Media.Brushes.Transparent;
        text.Cursor = Cursors.Hand;
        text.ToolTip = Loc.Get("outputcard.s25");
        text.MouseLeftButtonDown += (_, e) => { if (e.ClickCount >= 2) { slider.Value = 0; e.Handled = true; } };
        Show();
        DockPanel.SetDock(check, Dock.Left);
        DockPanel.SetDock(text, Dock.Right);
        DockPanel.SetDock(nameHost, Dock.Left);
        row.Children.Add(check);
        row.Children.Add(nameHost);
        row.Children.Add(text);
        row.Children.Add(slider);
        return row;
    }

    // ---- 表示の更新 ----
    /// <summary>50msごと。メーターと状態を更新する（slow＝0.25秒ごとの更新も行う）。</summary>
    public void Tick(bool slow)
    {
        var e = Engine;
        if (e.Running)
        {
            var (l, r, _) = e.ReadPeaks();
            _meter.Update(l, r);
        }
        else
        {
            e.ReadPeaks();
            _meter.Fade();
        }
        if (slow)
        {
            // 「すべて停止」・🔊のメニューなど、ほかから変わったオン/オフも反映する
            if ((_on.IsChecked == true) != e.On)
            {
                _loading = true;
                _on.IsChecked = e.On;
                _loading = false;
            }
            ShowFx();   // エフェクトウィンドウでの変更にも追従する
            if (++_warnTick % 4 == 0) UpdateLoopWarnings();   // 1秒ごと。ほかのカードの出力先の変更にも追従する
            ShowState();
        }
    }

    private void ShowState()
    {
        var e = Engine;
        _state.Text = _device.IsEnabled ? e.State : Loc.Get("outputcard.s09");
        var error = e.Error;
        if (error.Length == 0 && _loopWarn) error = OutputGuard.LoopMessage;   // 「出力する」がオフでも、選んだ時点で理由を出す
        _error.Text = error;
        // 「出力する」がオンなのに出力できていない（理由が赤文字で出ている）ときは、ランプを赤にする
        _on.Tag = _on.IsChecked == true && !e.Running && error.Length > 0 ? "Error" : null;
        _error.Visibility = error.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        bool shared = _sharedWarn && !_loopWarn;
        _caution.Visibility = shared ? Visibility.Visible : Visibility.Collapsed;
        _info.Text = e.Running ? e.Describe() : "";
    }
}
