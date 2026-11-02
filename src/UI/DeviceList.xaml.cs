using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>
/// 入力デバイスの選択欄。1行＝1デバイス（プルダウン・ゲイン・音量メーター・削除）。「＋デバイスを追加」で行を増やす。
/// 2台以上のときだけ「ミックスして1ファイル／デバイスごとに別ファイル」の切替を出す。
/// 選んだデバイスは、ユーザーが触ったときだけ settings.ini に保存する（触らなければ起動のたびに Windows の既定の入力）。
/// </summary>
public partial class DeviceList : UserControl
{
    private static readonly Brush GreenBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x9E, 0x4F));
    private static readonly Brush RedBrush = new SolidColorBrush(Color.FromRgb(0xD3, 0x33, 0x33));

    private sealed class Row
    {
        public readonly Grid Panel = new();
        public readonly ComboBox Combo = new();
        public readonly Slider Gain = new();
        public readonly TextBlock GainText = new();
        public readonly ProgressBar Level = new();
        public readonly Button Remove = new();
        public DateTime ClipUntil = DateTime.MinValue;
        public string Key = "";
    }

    private readonly List<Row> _rows = new();
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private List<AudioDeviceInfo> _devices = new();
    private Settings? _s;
    private bool _loading;
    private bool _recording;
    private bool _touched;

    /// <summary>デバイスの選択・追加・削除・ミックス/別ファイルの切替があった。</summary>
    public event Action? Changed;

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
        _s = settings;
        _devices = devices.ToList();
        _loading = true;
        var inputs = settings.Inputs.ToList();
        if (inputs.Count == 0)
        {
            var key = defaultKey != null && _devices.Any(d => d.Key == defaultKey)
                ? defaultKey
                : _devices.FirstOrDefault(d => !d.IsLoopback && !AudioKeys.IsDefault(d.Key))?.Key
                  ?? _devices.FirstOrDefault()?.Key ?? "";
            inputs.Add(new InputSetting(key, 0));
        }
        else
        {
            _touched = true;
        }
        foreach (var i in inputs) AddRow(i.Key, i.GainDb);
        MixRadio.IsChecked = !settings.SeparateFiles;
        SeparateRadio.IsChecked = settings.SeparateFiles;
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
        _rows.Where(r => r.Key.Length > 0).Select(r => new InputSetting(r.Key, r.Gain.Value)).ToList();

    /// <summary>ファイル名の {Device} の例に使う名前。</summary>
    public string NameLabel
    {
        get
        {
            var sel = _rows.Select(r => r.Combo.SelectedItem as AudioDeviceInfo).Where(d => d != null).ToList();
            if (sel.Count == 0) return "";
            if (sel.Count == 1 || (_s?.SeparateFiles ?? false)) return sel[0]!.Label;
            return "ミックス";
        }
    }

    // ---- 行の作成 ----
    private Row AddRow(string key, double gainDb)
    {
        var row = new Row { Key = key };
        var g = row.Panel;
        g.Margin = new Thickness(0, 0, 0, 4);
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

        row.Combo.DisplayMemberPath = "DisplayLabel";
        row.Combo.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(row.Combo, 0);

        row.Gain.Minimum = Settings.MinGainDb;
        row.Gain.Maximum = Settings.MaxGainDb;
        row.Gain.TickFrequency = 0.5;
        row.Gain.IsSnapToTickEnabled = true;
        row.Gain.SmallChange = 0.5;
        row.Gain.LargeChange = 3;
        row.Gain.Value = Math.Clamp(gainDb, Settings.MinGainDb, Settings.MaxGainDb);
        row.Gain.VerticalAlignment = VerticalAlignment.Center;
        row.Gain.ToolTip = "ゲイン（音量調整）。ダブルクリックで 0 dB に戻ります。録音中も変えられます";
        Grid.SetColumn(row.Gain, 1);

        row.GainText.Text = GainLabel(row.Gain.Value);
        row.GainText.VerticalAlignment = VerticalAlignment.Center;
        row.GainText.TextAlignment = TextAlignment.Right;
        row.GainText.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(row.GainText, 2);

        row.Level.Minimum = 0;
        row.Level.Maximum = 1;
        row.Level.Height = 10;
        row.Level.Foreground = GreenBrush;
        row.Level.VerticalAlignment = VerticalAlignment.Center;
        row.Level.ToolTip = "このデバイスの音量（ゲインを掛けたあと）。赤＝クリップ";
        Grid.SetColumn(row.Level, 3);

        row.Remove.Content = "✕";
        row.Remove.Width = 24;
        row.Remove.Margin = new Thickness(6, 0, 0, 0);
        row.Remove.ToolTip = "このデバイスを外す";
        Grid.SetColumn(row.Remove, 4);

        g.Children.Add(row.Combo);
        g.Children.Add(row.Gain);
        g.Children.Add(row.GainText);
        g.Children.Add(row.Level);
        g.Children.Add(row.Remove);

        Bind(row);
        row.Combo.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            if (row.Combo.SelectedItem is AudioDeviceInfo d) row.Key = d.Key;
            _touched = true;
            SaveNow();
            Changed?.Invoke();
        };
        row.Gain.ValueChanged += (_, _) =>
        {
            row.GainText.Text = GainLabel(row.Gain.Value);
            if (_loading) return;
            _touched = true;
            GainChanged?.Invoke(_rows.IndexOf(row), row.Gain.Value);
            _saveTimer.Stop();
            _saveTimer.Start();
        };
        row.Gain.MouseDoubleClick += (_, _) => row.Gain.Value = 0;
        row.Remove.Click += (_, _) => RemoveRow(row);

        _rows.Add(row);
        RowsPanel.Children.Add(g);
        return row;
    }

    private static string GainLabel(double db) =>
        db.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " dB";

    private List<AudioDeviceInfo> ItemsFor(string key)
    {
        var items = new List<AudioDeviceInfo>(_devices);
        if (key.Length > 0 && !items.Any(d => d.Key == key))
            items.Add(new AudioDeviceInfo(key, "（接続されていないデバイス）", AudioKeys.IsLoopback(key), 0, 0));
        return items;
    }

    private void Bind(Row row)
    {
        var items = ItemsFor(row.Key);
        row.Combo.ItemsSource = items;
        row.Combo.SelectedItem = items.FirstOrDefault(d => d.Key == row.Key);
    }

    // ---- 追加・削除・切替 ----
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_rows.Count >= Settings.MaxInputs) return;
        var used = _rows.Select(r => r.Key).ToHashSet();
        var free = _devices.Where(d => !used.Contains(d.Key) && !AudioKeys.IsDefault(d.Key)).ToList();
        // 例: マイクが1行目なら、2行目は Windows の既定の出力（PC音）
        var pick = free.FirstOrDefault(d => d.IsLoopback && d.IsDefault)
                   ?? free.FirstOrDefault(d => d.IsDefault)
                   ?? free.FirstOrDefault()
                   ?? _devices.FirstOrDefault();
        AddRow(pick?.Key ?? "", 0);
        _touched = true;
        UpdateLayout2();
        SaveNow();
        Changed?.Invoke();
    }

    private void RemoveRow(Row row)
    {
        if (_rows.Count <= 1 || _recording) return;
        RowsPanel.Children.Remove(row.Panel);
        _rows.Remove(row);
        _touched = true;
        UpdateLayout2();
        SaveNow();
        Changed?.Invoke();
    }

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || _s == null) return;
        _s.SeparateFiles = SeparateRadio.IsChecked == true;
        Changed?.Invoke();
    }

    /// <summary>行数に応じて、削除ボタン・追加ボタン・モード切替の表示を整える。</summary>
    private void UpdateLayout2()
    {
        foreach (var r in _rows) r.Remove.Visibility = _rows.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        AddButton.IsEnabled = !_recording && _rows.Count < Settings.MaxInputs;
        var multi = _rows.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        MixRadio.Visibility = SeparateRadio.Visibility = multi;
    }

    private void SaveNow()
    {
        _saveTimer.Stop();
        if (_s == null || _loading || !_touched) return;
        _s.Inputs = Inputs;
    }

    // ---- 録音中の表示 ----
    /// <summary>録音中は、デバイスの選択・追加・削除・モード切替を止める（ゲインは変えられる）。</summary>
    public void SetRecording(bool recording)
    {
        _recording = recording;
        foreach (var r in _rows)
        {
            r.Combo.IsEnabled = !recording;
            r.Remove.IsEnabled = !recording;
            if (!recording) r.Level.Value = 0;
        }
        MixRadio.IsEnabled = SeparateRadio.IsEnabled = !recording;
        UpdateLayout2();
    }

    /// <summary>デバイスごとのピーク（上から順）を、行のメーターに出す。</summary>
    public void SetLevels(float[] peaks)
    {
        var now = DateTime.Now;
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            float p = i < peaks.Length ? peaks[i] : 0f;
            var level = p <= 0 ? 0 : Math.Clamp((20 * Math.Log10(p) + 60) / 60, 0, 1);
            row.Level.Value = Math.Max(level, row.Level.Value - 0.03);
            if (p >= 0.99) row.ClipUntil = now.AddSeconds(1.5);
            row.Level.Foreground = now < row.ClipUntil ? RedBrush : GreenBrush;
        }
    }
}
