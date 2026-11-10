using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>
/// 出力ウィンドウ（M11-2。カード1枚）。🔊ボタンから開く。出力先・「出力する」・音量・メーター・状態を出す。
/// 「出力する」はここでは保存しない（起動のたびにオフ）。出力先と音量は Settings に保存。閉じても出力は止まらない。
/// </summary>
public partial class OutputWindow : Window
{
    private static readonly OutputDeviceInfo None = new("", "（未選択）");

    private readonly Settings _s;
    private readonly OutputEngine _engine;
    private readonly IOutputDeviceProvider _outs;
    private readonly OutputMeter _meter = new("出力レベル(dB)・音量",
        "この出力の音量（左右）。白い線＝直近の最大値、右端の四角が赤＝クリップ。縦長のつまみ＝この出力の全体音量（左端＝ミュート、ダブルクリックで 0 dB）。録音のレベルや全体ゲインには影響しません");
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private bool _loading = true;
    private bool _volumeDirty;
    private int _tick;

    public OutputWindow(Settings settings, OutputEngine engine, IOutputDeviceProvider outs)
    {
        InitializeComponent();
        _s = settings;
        _engine = engine;
        _outs = outs;
        MeterHost.Child = _meter;
        _meter.Gain.Value = settings.OutputGainDb;
        ShowGain();
        _meter.Gain.ValueChanged += (_, _) => OnVolume();
        OnCheck.IsChecked = _engine.On;
        ThemeManager.Attach(this, settings.Theme);

        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveVolume(); };
        Loaded += async (_, _) => await LoadDevicesAsync();
        Closed += (_, _) =>
        {
            _timer.Stop();
            _saveTimer.Stop();
            SaveVolume();
        };
        _loading = false;
    }

    /// <summary>再生デバイスの一覧は別スレッドで取る（取得に時間がかかっても画面を止めない）。</summary>
    private async Task LoadDevicesAsync()
    {
        List<OutputDeviceInfo> list;
        try { list = await Task.Run(() => _outs.List().ToList()); }
        catch (Exception ex)
        {
            StateText.Text = "出力デバイスの一覧を取得できませんでした: " + ex.Message;
            return;
        }
        var items = new List<OutputDeviceInfo> { None };
        items.AddRange(list);
        var saved = _s.OutputDevice;
        if (saved.Length > 0 && !items.Any(d => d.Id == saved))
            items.Add(new OutputDeviceInfo(saved, "（接続されていないデバイス）"));   // 選んだ設定は消さない（つながれば自動で使える）
        _loading = true;
        DeviceBox.ItemsSource = items;
        DeviceBox.SelectedItem = items.FirstOrDefault(d => d.Id == saved) ?? None;
        DeviceBox.IsEnabled = true;
        _loading = false;
        ShowState();
    }

    private void Device_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || DeviceBox.SelectedItem is not OutputDeviceInfo d) return;
        _s.OutputDevice = d.Id;
        _engine.DeviceId = d.Id;
        ShowState();
    }

    private void On_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _engine.On = OnCheck.IsChecked == true;
        ShowState();
    }

    // ---- 音量 ----
    private static string GainLabel(double db) => GainMath.IsMuted(db)
        ? "ミュート"
        : db.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " dB";

    private void ShowGain()
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

    private void OnVolume()
    {
        ShowGain();
        if (_loading) return;
        _engine.VolumeDb = _meter.Gain.Value;   // すぐ反映（動作中でも変えられる）
        _volumeDirty = true;
        _saveTimer.Stop();                      // 保存は、動かし終わってから
        _saveTimer.Start();
    }

    private void SaveVolume()
    {
        if (!_volumeDirty) return;
        _volumeDirty = false;
        _s.OutputGainDb = _meter.Gain.Value;
    }

    // ---- 表示の更新 ----
    private void OnTick()
    {
        if (_engine.Running)
        {
            var (l, r, _) = _engine.ReadPeaks();
            _meter.Update(l, r);
        }
        else
        {
            _engine.ReadPeaks();
            _meter.Fade();
        }
        if (++_tick % 5 == 0) ShowState();   // 0.25秒ごと
    }

    private void ShowState()
    {
        var state = _engine.State;
        if (!DeviceBox.IsEnabled) state = "読み込み中…";
        StateText.Text = state;
        var error = _engine.Error;
        ErrorText.Text = error;
        ErrorText.Visibility = error.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        InfoText.Text = _engine.Running ? _engine.Describe() : "";
    }
}
