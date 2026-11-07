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
        public readonly Slider Gain = new();
        public readonly TextBlock GainText = new();
        public readonly LevelBar Level = new();
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

    /// <summary>全体ゲインが変わった（dB）。録音中も使える。</summary>
    public event Action<double>? MasterGainChanged;

    /// <summary>デバイスの選択・追加・削除・ミックス/別ファイルの切替があった。</summary>
    public event Action? Changed;

    /// <summary>「デバイス再取得」ボタン（デバイス一覧を読み直す）。</summary>
    public event Action? RefreshRequested;

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke();

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
        MasterSlider.Minimum = Settings.MinGainDb;
        MasterSlider.Maximum = Settings.MaxGainDb;
        MasterSlider.TickFrequency = 0.5;
        MasterSlider.IsSnapToTickEnabled = true;
        MasterSlider.SmallChange = 0.5;
        MasterSlider.LargeChange = 3;
        Rebuild(defaultKey);
    }

    /// <summary>設定（settings.ini）の内容で行を作り直す。起動時と、プリセットを読み込んだとき。</summary>
    public void Rebuild(string? defaultKey)
    {
        if (_s == null) return;
        _loading = true;
        _touched = false;
        _saveTimer.Stop();
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
        MasterSlider.Value = _s.MasterGainDb;
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
        foreach (var w in new[] { 30.0, 250.0, -1, 116.0, 64.0, 66.0 })
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });

        row.Use.Style = (Style)Application.Current.FindResource("LampCheck");
        row.Use.IsChecked = enabled;
        row.Use.VerticalAlignment = VerticalAlignment.Center;
        row.Use.Margin = new Thickness(0, 0, 6, 0);
        row.Use.ToolTip = "消灯にすると、このデバイスは録音しません（設定は残ります。録音中は変えられません）";
        Grid.SetColumn(row.Use, 0);

        row.Combo.ItemTemplate = (DataTemplate)Resources["DeviceItem"];
        row.Combo.Margin = new Thickness(0, 0, 10, 0);
        row.Combo.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(row.Combo, 1);

        // 波形（この行のデバイスだけ。丸みのある暗い枠の中に出す）
        row.Wave.ClipToBounds = true;
        row.Lane.Child = row.Wave;
        row.Lane.CornerRadius = new CornerRadius(5);
        row.Lane.BorderThickness = new Thickness(1);
        row.Lane.Height = 38;
        row.Lane.Margin = new Thickness(0, 0, 12, 0);
        row.Lane.ClipToBounds = true;
        row.Lane.SetResourceReference(Border.BackgroundProperty, "LaneBg");
        row.Lane.SetResourceReference(Border.BorderBrushProperty, "LaneBorder");
        row.Lane.ToolTip = "このデバイスの波形（録音中、右から左へ流れます）";
        Grid.SetColumn(row.Lane, 2);

        row.Gain.Minimum = Settings.MinGainDb;
        row.Gain.Maximum = Settings.MaxGainDb;
        row.Gain.TickFrequency = 0.5;
        row.Gain.IsSnapToTickEnabled = true;
        row.Gain.SmallChange = 0.5;
        row.Gain.LargeChange = 3;
        row.Gain.Value = Math.Clamp(gainDb, Settings.MinGainDb, Settings.MaxGainDb);
        row.Gain.VerticalAlignment = VerticalAlignment.Center;
        row.Gain.ToolTip = "ゲイン（音量調整）。左端＝ミュート。ダブルクリックで 0 dB に戻ります。録音中も調整可能。";
        Grid.SetColumn(row.Gain, 3);

        row.GainText.Text = GainLabel(row.Gain.Value);
        row.GainText.VerticalAlignment = VerticalAlignment.Center;
        row.GainText.TextAlignment = TextAlignment.Right;
        row.GainText.Margin = new Thickness(0, 0, 10, 0);
        row.GainText.FontSize = 12;
        Grid.SetColumn(row.GainText, 4);

        row.Level.Margin = new Thickness(0, 0, 4, 0);
        row.Level.VerticalAlignment = VerticalAlignment.Center;
        row.Level.ToolTip = "このデバイスの音量（ゲインを掛けたあと）。白い線＝直近の最大値、右端の四角が赤＝クリップ。ミュート中は橙";
        Grid.SetColumn(row.Level, 5);

        g.Children.Add(row.Use);
        g.Children.Add(row.Combo);
        g.Children.Add(row.Lane);
        g.Children.Add(row.Gain);
        g.Children.Add(row.GainText);
        g.Children.Add(row.Level);

        ShowMute(row);
        Bind(row);
        row.Combo.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            if (row.Combo.SelectedItem is AudioDeviceInfo d) row.Key = d.Key;
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
        row.Gain.MouseDoubleClick += (_, _) => row.Gain.Value = 0;

        _rows.Add(row);
        RowsPanel.Children.Add(g);
        return row;
    }

    private static string GainLabel(double db) => GainMath.IsMuted(db)
        ? "ミュート"
        : db.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " dB";

    /// <summary>ミュート（ゲイン最小）のとき、数値の文字・スライダー・メーターを橙にして、一目で分かるようにする。</summary>
    private static void ShowMute(Row row)
    {
        bool muted = GainMath.IsMuted(row.Gain.Value);
        row.Level.Muted = muted;
        MuteStyle(row.Gain, row.GainText, muted);
    }

    private static void MuteStyle(Slider slider, TextBlock text, bool muted)
    {
        if (muted)
        {
            text.Foreground = MeterColors.Muted;
            text.FontWeight = FontWeights.SemiBold;
            slider.Resources["Pastel"] = MeterColors.Muted;   // ダークのスライダー（つまみ・塗り）はパステル色のリソースを使う
        }
        else
        {
            text.ClearValue(TextBlock.ForegroundProperty);
            text.ClearValue(TextBlock.FontWeightProperty);
            slider.Resources.Remove("Pastel");
        }
    }

    // ---- 全体ゲイン ----
    private void ShowMasterGain()
    {
        MasterText.Text = GainLabel(MasterSlider.Value);
        MuteStyle(MasterSlider, MasterText, GainMath.IsMuted(MasterSlider.Value));
    }

    private void Master_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        ShowMasterGain();
        if (_loading || _s == null) return;
        _masterDirty = true;
        MasterGainChanged?.Invoke(MasterSlider.Value);
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void Master_DoubleClick(object sender, MouseButtonEventArgs e) => MasterSlider.Value = 0;

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
            r.Lane.Opacity = r.Gain.Opacity = r.GainText.Opacity = r.Level.Opacity = o;
        }
        RefreshButton.IsEnabled = !_recording;
        // ミックス／別ファイルの切替は常に表示し、有効なデバイスが2台未満のとき（と録音中）はグレーアウトする
        MixRadio.IsEnabled = SeparateRadio.IsEnabled = !_recording && Active().Count > 1;
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
            _s.MasterGainDb = MasterSlider.Value;
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
            if (!recording) r.Level.Reset();
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
    public void SetLevels(float[] peaks)
    {
        var active = Active();
        foreach (var r in _rows.Where(r => !active.Contains(r))) r.Level.Reset();
        for (int i = 0; i < active.Count; i++)
            active[i].Level.Update(i < peaks.Length ? peaks[i] : 0f);
    }

    /// <summary>全体ゲイン（dB）。</summary>
    public double MasterGainDb => MasterSlider.Value;
}
