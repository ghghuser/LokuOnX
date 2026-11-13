using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>
/// 入力デバイスのストリップ（録音コンソールの右側）。1行＝1デバイス（有効チェック・プルダウン・波形・ゲイン・レベルメーター）。
/// 行は最初から最大4つ並べる（デバイスを選んだ行だけ録音に使う）。波形は行ごとに1段（録音中に右から左へ流れる）。
/// 2台以上のときだけ「ミックスして1ファイル／デバイスごとに別ファイル」の切替を出す。
/// 選んだデバイスは、ユーザーが触ったときだけ settings.ini に保存する（触らなければ起動のたびに Windows の既定の入力）。
/// </summary>
public partial class DeviceList : UserControl
{
    private sealed class Row
    {
        public readonly Grid Panel = new();
        public readonly CheckBox Use = new();
        public readonly ComboBox Combo = new();
        public readonly StackPanel Left = new();     // 名前の小さいラベル＋プルダウン
        public readonly TextBlock NameText = new();  // 入力の名前（ダブルクリックで変更）
        public readonly TextBox NameBox = new();     // 名前の入力欄（変更中だけ出す）
        public readonly Button FxButton = new();     // この入力のエフェクトを開く（M11-4）
        public readonly GainMeter Gain = new(2, 6);   // 左右2本の細いメーター＋1つのつまみ
        public readonly StackPanel MeterBox = new();   // メーター＋その下のdB目盛り
        public readonly TextBlock GainText = new();
        public LevelBar Level => Gain.Bar;
        public readonly WaveView Wave = new();
        public readonly Border Lane = new();
        public string Key = "";
    }

    private readonly List<Row> _rows = new();
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private List<AudioDeviceInfo> _devices = new();
    private Settings? _s;
    private bool _loading;
    private bool _recording;
    private bool _touched;
    private bool _masterDirty;
    private GainMeter? _master;
    private TextBlock? _masterText;
    private readonly string[] _names = new string[Settings.MaxInputs];   // 入力の名前（空＝既定の「入力N」）

    /// <summary>入力の名前を変えた（メイン画面・出力カード・エフェクトの見出しで共通に使う）。</summary>
    public event Action? NamesChanged;

    /// <summary>この行（入力N。行の番号・0始まり）のエフェクトを開く操作（「FX」ボタン・右クリックの「エフェクト…」）。</summary>
    public event Action<int>? FxRequested;

    /// <summary>入力1〜4（行）すべての、名前と「デバイスが選ばれて使われているか」。エフェクトウィンドウの入力の切り替えに使う。</summary>
    public IReadOnlyList<(int Slot, string Name, bool Active)> AllSlots() =>
        _rows.Select((r, i) => (i, OutputConfig.InputLabel(_names, i), r.Key.Length > 0 && r.Use.IsChecked == true)).ToList();

    /// <summary>行の「FX」ボタンの見た目。効果が設定されているとき（any）は強調色の太字、バイパス中は薄く。</summary>
    public void SetFxState(int slot, bool any, bool bypass)
    {
        if (slot < 0 || slot >= _rows.Count) return;
        FxButtonLook.Apply(_rows[slot].FxButton, any, bypass, "この入力のエフェクト",
            "この入力のエフェクト（ゲート・ローカット・EQ・エコー・リバーブ）。クリックで開きます。名前・波形・メーターの右クリックでも開けます");
    }

    private MenuItem FxItem(Row row)
    {
        var item = new MenuItem { Header = "入力のエフェクト…" };
        item.Click += (_, _) => FxRequested?.Invoke(_rows.IndexOf(row));
        return item;
    }

    /// <summary>「既定のデバイス」を実際のデバイスに置き換える（MainWindow が設定する）。同じデバイスの重複を赤文字で知らせるのに使う。</summary>
    public Func<string, string>? Resolve { get; set; }

    /// <summary>入力デバイス（Key）を選ぶと、音が回るため出力できなくなる出力の名前（なければ空）。プルダウンの黄文字と、デバイス欄の下の黄色い一行に使う。</summary>
    public Func<string, IReadOnlyList<string>>? LoopOutputs { get; set; }

    /// <summary>録音に使う行（デバイスが選ばれていて有効）の、行の番号（0始まり）と表示名。上から順。</summary>
    public IReadOnlyList<(int Slot, string Name)> ActiveInputs =>
        _rows.Select((r, i) => (r, i)).Where(x => x.r.Key.Length > 0 && x.r.Use.IsChecked == true)
            .Select(x => (x.i, OutputConfig.InputLabel(_names, x.i))).ToList();

    /// <summary>全体ゲインが変わった（dB）。録音中も使える。</summary>
    public event Action<double>? MasterGainChanged;

    /// <summary>デバイスの選択・追加・削除・ミックス/別ファイルの切替があった。</summary>
    public event Action? Changed;

    /// <summary>「デバイス再取得」ボタン（デバイス一覧を読み直す）。</summary>
    public event Action? RefreshRequested;

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke();

    /// <summary>下の行（デバイス再取得…）の右端に部品（「インジケーター(常時)」のチェック）を入れる。</summary>
    public void AddFooterTrailing(FrameworkElement element)
    {
        element.Margin = new Thickness(22, 0, 0, 0);
        element.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(element, Dock.Right);
        FooterRow.Children.Add(element);
    }

    /// <summary>ゲインが変わった（デバイスの順番, dB）。録音中も使える。</summary>
    public event Action<int, double>? GainChanged;

    public DeviceList()
    {
        InitializeComponent();
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveNow();
        };
    }

    /// <summary>起動時。保存されたデバイスがなければ defaultKey（Windowsの既定の入力）を1行目にする。</summary>
    public void Init(Settings settings, IReadOnlyList<AudioDeviceInfo> devices, string? defaultKey)
    {
        _loading = true;   // スライダーの初期設定で、変更の通知が出ないように
        _s = settings;
        _devices = devices.ToList();
        if (_master != null)
        {
            _master.Minimum = Settings.MinGainDb;
            _master.Maximum = Settings.MaxGainDb;
        }
        Rebuild(defaultKey);
    }

    /// <summary>設定（settings.ini）の内容で行を作り直す。起動時と、プロファイルを読み込んだとき。</summary>
    public void Rebuild(string? defaultKey)
    {
        if (_s == null) return;
        _loading = true;
        _touched = false;
        _saveTimer.Stop();
        var saved = _s.InputNames;
        for (int n = 0; n < _names.Length; n++) _names[n] = n < saved.Count ? saved[n] : "";
        RowsPanel.Children.Clear();
        _rows.Clear();
        var inputs = _s.Inputs.ToList();
        if (inputs.Count == 0)
        {
            var key = defaultKey != null && _devices.Any(d => d.Key == defaultKey)
                ? defaultKey
                : _devices.FirstOrDefault(d => !d.IsLoopback && !AudioKeys.IsDefault(d.Key))?.Key
                  ?? _devices.FirstOrDefault()?.Key ?? "";
            inputs.Add(new InputSetting(key, 0));
        }
        else if (inputs.All(i => i.Key.Length == 0) && defaultKey != null && _devices.Any(d => d.Key == defaultKey))
        {
            inputs[0] = inputs[0] with { Key = defaultKey };
        }
        else
        {
            _touched = true;
        }
        foreach (var i in inputs.Take(Settings.MaxInputs)) AddRow(i.Key, i.GainDb, i.Enabled);
        while (_rows.Count < Settings.MaxInputs) AddRow("", 0);   // 空き行（デバイスを選ぶまで録音に使われない）
        MixRadio.IsChecked = !_s.SeparateFiles;
        SeparateRadio.IsChecked = _s.SeparateFiles;
        if (_master != null) _master.Value = _s.MasterGainDb;
        ShowMasterGain();
        _loading = false;
        UpdateLayout2();
    }

    /// <summary>「一覧を更新」・デバイスの抜き差し後。選んでいるデバイスは Key で選び直す。</summary>
    public void Reload(IReadOnlyList<AudioDeviceInfo> devices)
    {
        _devices = devices.ToList();
        var was = _loading;
        _loading = true;
        foreach (var r in _rows) Bind(r);
        _loading = was;
        UpdateWarnings();
        Changed?.Invoke();
    }

    /// <summary>いま選ばれているデバイス（上から順）とゲイン。</summary>
    public IReadOnlyList<InputSetting> Inputs =>
        Active().Select(r => new InputSetting(r.Key, r.Gain.Value)).ToList();

    /// <summary>録音に使う行（デバイスが選ばれていて、チェックが入っているもの）。</summary>
    private List<Row> Active() => _rows.Where(r => r.Key.Length > 0 && r.Use.IsChecked == true).ToList();

    /// <summary>設定へ保存する全行（「未選択」・無効の行も含む）。</summary>
    private List<InputSetting> AllRows() =>
        _rows.Select(r => new InputSetting(r.Key, r.Gain.Value, r.Use.IsChecked == true)).ToList();

    /// <summary>ファイル名の {Device} の例に使う名前。</summary>
    public string NameLabel
    {
        get
        {
            var sel = Active().Select(r => r.Combo.SelectedItem as AudioDeviceInfo).Where(d => d != null).Select(d => d!).ToList();
            if (sel.Count == 0) return "";
            // 「既定のデバイス」は、いま実際の既定のデバイス名で表す
            var first = AudioKeys.IsDefault(sel[0].Key)
                ? _devices.FirstOrDefault(d => d.IsDefault && d.IsLoopback == sel[0].IsLoopback) ?? sel[0]
                : sel[0];
            if (sel.Count == 1 || (_s?.SeparateFiles ?? false)) return first.Label;
            return "ミックス";
        }
    }

    // ---- 行の作成 ----
    private Row AddRow(string key, double gainDb, bool enabled = true)
    {
        var row = new Row { Key = key };
        var g = row.Panel;
        g.Margin = new Thickness(0, 0, 0, 6);
        // 見出し（DeviceList.xaml）と同じ列幅: チェック・プルダウン・波形・ゲイン・ゲインの数値・レベル
        foreach (var w in new[] { 30.0, 250.0, -1, 220.0, 70.0 })
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });

        row.Use.Style = (Style)Application.Current.FindResource("LampCheck");
        row.Use.IsChecked = key.Length > 0 && enabled;   // 空き行（デバイス未選択）は消灯。初期状態で点灯するのは1行目だけ
        row.Use.VerticalAlignment = VerticalAlignment.Center;
        row.Use.Margin = new Thickness(0, 0, 6, 0);
        row.Use.ToolTip = "消灯にすると、このデバイスは録音しません（設定は残ります。録音中は変えられません）";
        Grid.SetColumn(row.Use, 0);

        row.Combo.ItemTemplate = (DataTemplate)Resources["DeviceItem"];
        row.Combo.DropDownOpened += (_, _) => UpdateWarnings();
        row.Combo.VerticalAlignment = VerticalAlignment.Center;
        // 入力の名前（小さいラベル）をプルダウンの上に置く。ダブルクリック／右クリックで変更
        var slot = _rows.Count;
        row.NameText.Text = OutputConfig.InputLabel(_names, slot);
        row.NameText.FontSize = 10;
        row.NameText.Opacity = 0.7;
        row.NameText.Margin = new Thickness(2, 0, 0, 1);
        row.NameText.Cursor = Cursors.IBeam;
        row.NameText.ToolTip = "入力の名前（ダブルクリックか右クリックで変更。12文字まで。出力カードにも同じ名前が出ます）";
        row.NameBox.FontSize = 10;
        row.NameBox.Height = 17;
        row.NameBox.Padding = new Thickness(1, 0, 1, 0);
        row.NameBox.MaxLength = OutputConfig.MaxNameLength;
        row.NameBox.Visibility = Visibility.Collapsed;
        row.NameText.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) BeginRename(row); };
        var rename = new MenuItem { Header = "名前を変更" };
        rename.Click += (_, _) => BeginRename(row);
        row.NameText.ContextMenu = new ContextMenu { Items = { rename, FxItem(row) } };
        row.NameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { CommitRename(row, true); e.Handled = true; }
            else if (e.Key == Key.Escape) { CommitRename(row, false); e.Handled = true; }
        };
        row.NameBox.LostFocus += (_, _) => CommitRename(row, true);
        row.Left.Margin = new Thickness(0, 0, 10, 0);
        row.Left.VerticalAlignment = VerticalAlignment.Center;
        // 名前ラベルの行の右端に「FX」ボタン（M11-4）
        row.FxButton.Content = "FX";
        row.FxButton.Style = (Style)Application.Current.FindResource("FxButton");
        row.FxButton.FontSize = 10;
        row.FxButton.Height = 18;
        row.FxButton.MinWidth = 26;
        row.FxButton.Margin = new Thickness(6, 0, 0, 2);
        row.FxButton.VerticalAlignment = VerticalAlignment.Center;
        row.FxButton.Click += (_, _) => FxRequested?.Invoke(_rows.IndexOf(row));
        DockPanel.SetDock(row.FxButton, Dock.Right);
        var nameRow = new DockPanel { LastChildFill = true };
        nameRow.Children.Add(row.FxButton);
        nameRow.Children.Add(row.NameText);
        nameRow.Children.Add(row.NameBox);
        row.Left.Children.Add(nameRow);
        row.Left.Children.Add(row.Combo);
        Grid.SetColumn(row.Left, 1);

        // 波形（この行のデバイスだけ。丸みのある暗い枠の中に出す）
        row.Wave.ClipToBounds = true;
        row.Lane.Child = row.Wave;
        row.Lane.CornerRadius = new CornerRadius(5);
        row.Lane.BorderThickness = new Thickness(1);
        row.Lane.Height = 38;
        row.Lane.Margin = new Thickness(0, 0, 12, 0);
        row.Lane.ClipToBounds = true;
        row.Lane.SetResourceReference(Border.BackgroundProperty, key.Length > 0 ? "WaveBg" : "WaveBgOff");
        row.Lane.SetResourceReference(Border.BorderBrushProperty, "WaveBorder");
        row.Lane.ToolTip = "このデバイスの波形（録音中、右から左へ流れます）";
        Grid.SetColumn(row.Lane, 2);

        row.Gain.Minimum = Settings.MinGainDb;
        row.Gain.Maximum = Settings.MaxGainDb;
        row.Gain.Value = Math.Clamp(gainDb, Settings.MinGainDb, Settings.MaxGainDb);
        row.Gain.VerticalAlignment = VerticalAlignment.Center;
        row.Gain.ToolTip = "メーター＝このデバイスの音量（左右。ゲインを掛けたあと。白い線＝直近の最大値、右端の四角が赤＝クリップ）。縦長のつまみ＝ゲイン（左端＝ミュート。下の数字をダブルクリックで 0 dB。録音中も調整可能）";
        row.MeterBox.VerticalAlignment = VerticalAlignment.Center;
        row.MeterBox.Children.Add(row.Gain);
        row.MeterBox.Children.Add(new LevelScale { Margin = new Thickness(0, 1, 0, 0) });
        Grid.SetColumn(row.MeterBox, 3);

        row.GainText.Text = GainLabel(row.Gain.Value);
        row.GainText.VerticalAlignment = VerticalAlignment.Center;
        row.GainText.TextAlignment = TextAlignment.Left;
        row.GainText.Margin = new Thickness(10, 0, 0, 0);
        row.GainText.FontSize = 12;
        row.Gain.AttachReset(row.GainText);   // 数字のダブルクリックで 0 dB
        Grid.SetColumn(row.GainText, 4);

        g.Children.Add(row.Use);
        g.Children.Add(row.Left);
        g.Children.Add(row.Lane);
        g.Children.Add(row.MeterBox);
        g.Children.Add(row.GainText);
        row.Lane.ContextMenu = new ContextMenu { Items = { FxItem(row) } };
        row.MeterBox.ContextMenu = new ContextMenu { Items = { FxItem(row) } };

        ShowMute(row);
        Bind(row);
        row.Combo.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            if (row.Combo.SelectedItem is AudioDeviceInfo d)
            {
                bool wasEmpty = row.Key.Length == 0;
                row.Key = d.Key;
                // 空き行にデバイスを選んだらランプを点ける（「（未選択）」に戻したら消す）
                if (wasEmpty && d.Key.Length > 0) row.Use.IsChecked = true;
                else if (d.Key.Length == 0) row.Use.IsChecked = false;
            }
            _touched = true;
            UpdateLayout2();
            SaveNow();
            Changed?.Invoke();
        };
        row.Use.Click += (_, _) =>
        {
            if (_loading) return;
            _touched = true;
            UpdateLayout2();
            SaveNow();
            Changed?.Invoke();
        };
        row.Gain.ValueChanged += (_, _) =>
        {
            row.GainText.Text = GainLabel(row.Gain.Value);
            ShowMute(row);
            if (_loading) return;
            _touched = true;
            var at = Active().IndexOf(row);
            if (at >= 0) GainChanged?.Invoke(at, row.Gain.Value);
            _saveTimer.Stop();
            _saveTimer.Start();
        };

        _rows.Add(row);
        RowsPanel.Children.Add(g);
        return row;
    }

    // ---- 入力の名前 ----
    private void BeginRename(Row row)
    {
        var slot = _rows.IndexOf(row);
        if (slot < 0) return;
        row.NameBox.Text = _names[slot] ?? "";
        row.NameText.Visibility = Visibility.Collapsed;
        row.NameBox.Visibility = Visibility.Visible;
        row.NameBox.Focus();
        row.NameBox.SelectAll();
    }

    private void CommitRename(Row row, bool apply)
    {
        if (row.NameBox.Visibility != Visibility.Visible) return;
        var slot = _rows.IndexOf(row);
        var text = row.NameBox.Text;
        row.NameBox.Visibility = Visibility.Collapsed;
        row.NameText.Visibility = Visibility.Visible;
        if (!apply) return;
        SetInputName(slot, text);
    }

    /// <summary>入力の名前を変える（slot＝行の番号。メイン画面の入力欄・出力ウィンドウのどちらからでも）。空・既定と同じ名前は「未設定」。</summary>
    public void SetInputName(int slot, string text)
    {
        if (slot < 0 || slot >= _rows.Count || _s == null) return;
        var name = OutputConfig.CleanName(text, "");
        if (name == OutputConfig.DefaultInputName(slot)) name = "";
        if (name == (_names[slot] ?? "")) return;
        _names[slot] = name;
        _rows[slot].NameText.Text = OutputConfig.InputLabel(_names, slot);
        _s.InputNames = _names.ToList();
        NamesChanged?.Invoke();
    }

    // ---- プルダウンの注意（赤文字） ----
    /// <summary>この行のプルダウンで、選ぶと注意が出る項目（ほかの有効な行と同じデバイス・ASIOの2行目）のキー。</summary>
    private HashSet<string> WarnFor(Row row)
    {
        var warn = new HashSet<string>();
        var others = _rows.Where(r => r != row && r.Key.Length > 0 && r.Use.IsChecked == true).Select(r => r.Key).ToList();
        if (others.Count == 0) return warn;
        string Real(string k)
        {
            try { return Resolve?.Invoke(k) ?? k; }
            catch (Exception) { return k; }
        }
        var otherReal = others.Select(Real).ToHashSet();
        bool otherAsio = others.Any(AsioKey.Is);
        foreach (var d in _devices)
        {
            if (d.Key.Length == 0) continue;
            if (otherReal.Contains(Real(d.Key)) || (otherAsio && AsioKey.Is(d.Key))) warn.Add(d.Key);
        }
        return warn;
    }

    /// <summary>出力の出力先が変わったときなど、プルダウンの注意と黄色い一行を付け直す。</summary>
    public void RefreshWarnings() => UpdateWarnings();

    private void UpdateWarnings()
    {
        // 音が回るため出力が止まる入力（黄文字）。入力ごとに調べて、全デバイスぶんを一度だけ求める
        var loopKeys = new HashSet<string>();
        var loopMap = new Dictionary<string, IReadOnlyList<string>>();
        if (LoopOutputs != null)
        {
            foreach (var d in _devices)
            {
                if (d.Key.Length == 0) continue;
                IReadOnlyList<string> names;
                try { names = LoopOutputs(d.Key); } catch (Exception) { continue; }
                if (names.Count > 0) { loopKeys.Add(d.Key); loopMap[d.Key] = names; }
            }
        }
        var lines = new List<string>();
        for (int i = 0; i < _rows.Count; i++)
        {
            var r = _rows[i];
            if (r.Key.Length == 0 || r.Use.IsChecked != true || !loopMap.TryGetValue(r.Key, out var outs)) continue;
            lines.Add($"「{OutputConfig.InputLabel(_names, i)}」は、出力「{string.Join("」「", outs)}」の出力先と同じデバイスです。音が回るため、その出力は止まります。");
        }
        CautionText.Text = string.Join("\n", lines);
        CautionText.Visibility = lines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var r in _rows)
        {
            var warn = WarnFor(r);
            r.Combo.Tag = new WarnSet(warn, loopKeys);
            // ランプがオンで、同じデバイスを重ねて使っている（プルダウンで赤文字になる状態）なら、ランプを赤にする
            r.Use.Tag = r.Key.Length > 0 && warn.Contains(r.Key) ? "Error" : null;
        }
    }

    private static string GainLabel(double db) => GainMath.IsMuted(db)
        ? "ミュート"
        : db.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " dB";

    /// <summary>ミュート（ゲイン最小）のとき、数値の文字・スライダー・メーターを橙にして、一目で分かるようにする。</summary>
    private static void ShowMute(Row row)
    {
        bool muted = GainMath.IsMuted(row.Gain.Value);
        row.Gain.Muted = muted;
        MuteText(row.GainText, muted);
    }

    private static void MuteText(TextBlock text, bool muted)
    {
        if (muted)
        {
            text.Foreground = MeterColors.Muted;
            text.FontWeight = FontWeights.SemiBold;
        }
        else
        {
            text.ClearValue(TextBlock.ForegroundProperty);
            text.ClearValue(TextBlock.FontWeightProperty);
        }
    }

    // ---- 全体ゲイン（つまみは画面右の出力メーターと一体。MainWindow が AttachMaster で渡す） ----
    /// <summary>全体ゲインのつまみと数値の表示を受け取る。Init の前に呼ぶこと。</summary>
    public void AttachMaster(GainMeter gain, TextBlock text)
    {
        _master = gain;
        _masterText = text;
        gain.AttachReset(text);
        gain.ValueChanged += Master_ValueChanged;
    }

    private void ShowMasterGain()
    {
        if (_master == null || _masterText == null) return;
        _masterText.Text = GainLabel(_master.Value);
        _master.Muted = GainMath.IsMuted(_master.Value);
        MuteText(_masterText, _master.Muted);
    }

    private void Master_ValueChanged(object? sender, EventArgs e)
    {
        ShowMasterGain();
        if (_loading || _s == null || _master == null) return;
        _masterDirty = true;
        MasterGainChanged?.Invoke(_master.Value);
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private List<AudioDeviceInfo> ItemsFor(string key)
    {
        var items = new List<AudioDeviceInfo> { new("", "（未選択）", false, 0, 0) };
        items.AddRange(_devices);
        if (key.Length > 0 && !items.Any(d => d.Key == key))
            items.Add(new AudioDeviceInfo(key,
                AsioKey.Is(key) ? $"🎛 {AsioKey.Describe(key)}（未接続／ASIO非表示）" : "（接続されていないデバイス）",
                AudioKeys.IsLoopback(key), 0, 0));
        return items;
    }

    private void Bind(Row row)
    {
        var items = ItemsFor(row.Key);
        row.Combo.ItemsSource = items;
        row.Combo.SelectedItem = items.FirstOrDefault(d => d.Key == row.Key);
    }

    // ---- 切替 ----
    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || _s == null) return;
        _s.SeparateFiles = SeparateRadio.IsChecked == true;
        Changed?.Invoke();
    }

    /// <summary>行数に応じて、削除ボタン・追加ボタン・モード切替の表示を整える。</summary>
    private void UpdateLayout2()
    {
        // デバイスを選んでいない行は、ゲイン・波形・レベルを薄くして「空き」とわかるようにする
        foreach (var r in _rows)
        {
            var o = r.Key.Length > 0 ? 1.0 : 0.4;
            r.Lane.Opacity = r.MeterBox.Opacity = r.GainText.Opacity = o;
            r.Lane.SetResourceReference(Border.BackgroundProperty, r.Key.Length > 0 ? "WaveBg" : "WaveBgOff");
        }
        RefreshButton.IsEnabled = !_recording;
        // ミックス／別ファイルの切替は常に表示し、有効なデバイスが2台未満のとき（と録音中）はグレーアウトする
        MixRadio.IsEnabled = SeparateRadio.IsEnabled = !_recording && Active().Count > 1;
        UpdateWarnings();
    }

    /// <summary>デバイス欄のすぐ下に、赤文字でエラーを出す（ポップアップは出さない）。</summary>
    public void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    public void ClearError()
    {
        ErrorText.Text = "";
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void SaveNow()
    {
        _saveTimer.Stop();
        if (_s != null && !_loading && _masterDirty)
        {
            _masterDirty = false;
            _s.MasterGainDb = _master?.Value ?? 0;
        }
        if (_s == null || _loading || !_touched) return;
        _s.Inputs = AllRows();
    }

    // ---- 録音中の表示 ----
    /// <summary>録音中は、デバイスの選択・モード切替を止める（ゲインは変えられる）。</summary>
    public void SetRecording(bool recording)
    {
        _recording = recording;
        foreach (var r in _rows)
        {
            r.Combo.IsEnabled = !recording;
            r.Use.IsEnabled = !recording;
            if (!recording) r.Gain.Reset();
        }
        UpdateLayout2();
    }

    /// <summary>録音を始めるとき。録音に使う行の波形を空にして、使わない行は何も出さない。</summary>
    public void StartWave()
    {
        var active = Active();
        Array.Clear(_waveAcc, 0, _waveAcc.Length);
        _waveCount = 0;
        foreach (var r in _rows) r.Wave.SetRows(active.Contains(r) ? new[] { "" } : Array.Empty<string>());
    }

    private const int WaveStride = 1;   // 波形の1列は、呼び出し WaveStride 回ぶん（50ms×2＝100ms）。大きいほど流れる速さが遅い
    private float[] _waveAcc = Array.Empty<float>();
    private int _waveCount;

    /// <summary>デバイスごとのピーク（上から順）を、行の波形へ足す（一時停止中は呼ばない）。50msごとに呼ばれ、WaveStride 回ぶんの最大値を1列にする（波形の流れが遅くなる）。</summary>
    public void PushWave(float[] peaks)
    {
        if (_waveAcc.Length != peaks.Length) { _waveAcc = new float[peaks.Length]; _waveCount = 0; }
        for (int i = 0; i < peaks.Length; i++) if (peaks[i] > _waveAcc[i]) _waveAcc[i] = peaks[i];
        if (++_waveCount < WaveStride) return;
        var active = Active();
        for (int i = 0; i < active.Count; i++)
            active[i].Wave.Push(new[] { i < _waveAcc.Length ? _waveAcc[i] : 0f });
        Array.Clear(_waveAcc, 0, _waveAcc.Length);
        _waveCount = 0;
    }

    /// <summary>デバイスごとのピーク（上から順）を、行のメーターに出す。</summary>
    public void SetLevels((float L, float R)[] peaks)
    {
        var active = Active();
        foreach (var r in _rows.Where(r => !active.Contains(r))) r.Gain.Reset();
        for (int i = 0; i < active.Count; i++)
        {
            var (l, r) = i < peaks.Length ? peaks[i] : (0f, 0f);
            active[i].Gain.Bars[0].Update(l);
            active[i].Gain.Bars[1].Update(r);
        }
    }

    /// <summary>全体ゲイン（dB）。</summary>
    public double MasterGainDb => _master?.Value ?? 0;
}
