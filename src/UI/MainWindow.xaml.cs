using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Recorder.Core;
using Recorder.Core.Win;

namespace Recorder.UI;

public partial class MainWindow : Window
{
    private static readonly Brush GreenBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x9E, 0x4F));
    private static readonly Brush RedBrush = new SolidColorBrush(Color.FromRgb(0xD3, 0x33, 0x33));

    private readonly Settings _settings;
    private readonly IAudioDeviceProvider _provider = new WasapiDeviceProvider();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private List<AudioDeviceInfo> _devices = new();
    private RecordingSession? _session;
    private RecordingPlan? _plan;
    private DateTime _clipUntil = DateTime.MinValue;
    private bool _loading;
    private bool _closeAfterStop;

    public MainWindow(Settings settings)
    {
        InitializeComponent();
        _settings = settings;
        Title = settings.ReadOnly ? $"{AppInfo.Name}（2つ目の起動・設定は保存されません）" : AppInfo.Name;

        _loading = true;
        FormatBox.ItemsSource = OutputFormat.All;
        FormatBox.SelectedValue = settings.Format;
        FolderBox.Text = settings.Folder;
        RefreshDevices();
        Detail.Init(settings, () => SelectedDevice?.Label ?? "");
        Detail.Changed += UpdateSummary;
        _loading = false;

        UpdateSummary();
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
    }

    private AudioDeviceInfo? SelectedDevice => DeviceBox.SelectedItem as AudioDeviceInfo;

    private void RefreshDevices()
    {
        var wanted = SelectedDevice?.Key ?? _settings.Device;
        try { _devices = _provider.List().ToList(); }
        catch (Exception ex)
        {
            _devices = new List<AudioDeviceInfo>();
            StatusText.Text = "デバイス一覧を取得できませんでした: " + ex.Message;
        }
        DeviceBox.ItemsSource = _devices;
        DeviceBox.SelectedItem = _devices.FirstOrDefault(d => d.Key == wanted)
                                 ?? _devices.FirstOrDefault(d => !d.IsLoopback);
    }

    private void UpdateSummary()
    {
        SummaryText.Text = SummaryBar.Build(_settings, SelectedDevice);
        // マウスを乗せると全文を表示。長いときは左右に流す
        SummaryBorder.ToolTip = SummaryText.Text + "\n\n（クリックで詳細設定を開きます）";
        ApplyMarquee();
        Detail.Refresh();
    }

    private void ApplyMarquee() =>
        Marquee.Apply(SummaryHost, SummaryBorder.ActualWidth - SummaryBorder.Padding.Left
                                   - SummaryBorder.Padding.Right - 2);

    private void SummaryBorder_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyMarquee();

    // ---- 設定の変更 ----
    private void DeviceBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (SelectedDevice != null) _settings.Device = SelectedDevice.Key;
        UpdateSummary();
    }

    private void FormatBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loading || FormatBox.SelectedValue is not string f) return;
        _settings.Format = f;
        UpdateSummary();
    }

    private void FolderBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var text = FolderBox.Text.Trim();
        if (text.Length == 0) { FolderBox.Text = _settings.Folder; return; }
        _settings.Folder = text;
        UpdateSummary();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = _settings.Folder };
        if (dlg.ShowDialog(this) != true) return;
        FolderBox.Text = dlg.FolderName;
        _settings.Folder = dlg.FolderName;
        UpdateSummary();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _loading = true;
        RefreshDevices();
        _loading = false;
        UpdateSummary();
    }

    private void Summary_Click(object sender, MouseButtonEventArgs e) => ExpDetail.IsExpanded = true;

    // ---- 録音 ----
    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_session != null) return;
        if (SelectedDevice is not { } dev) { ShowError("入力デバイスを選んでください。"); return; }
        if (!File.Exists(AppPaths.Ffmpeg))
        {
            ShowError($"FFmpeg が見つかりません。\n{AppPaths.Ffmpeg}");
            return;
        }

        IAudioSource? src = null;
        RecordingSession? session = null;
        try
        {
            FolderBox_LostFocus(sender, e);
            var no = _settings.NextNo;
            var plan = RecordingPlan.Create(_settings, dev.Label, DateTime.Now);
            Directory.CreateDirectory(plan.Folder);
            src = _provider.Create(dev.Key);
            session = new RecordingSession(src, AppPaths.Ffmpeg, plan.TempPath, plan.Options);
            session.Error += msg => Dispatcher.BeginInvoke(new Action(async () => await StopAsync(msg)));
            session.Start();
            _session = session;
            _plan = plan;
            if (!_settings.ReadOnly) _settings.NextNo = no + 1;
        }
        catch (Exception ex)
        {
            if (session != null) session.Dispose(); else src?.Dispose();
            ShowError("録音を開始できませんでした。\n" + ex.Message);
            return;
        }
        SetRecording(true);
        Detail.Reload();   // {No} が進んだので画面へ反映
        StatusText.Text = "録音中: " + _session!.OutputPath;
        UpdateSummary();
    }

    private async void Stop_Click(object sender, RoutedEventArgs e) => await StopAsync(null);

    private async Task StopAsync(string? error)
    {
        var s = _session;
        var plan = _plan;
        if (s == null) return;
        _session = null;
        _plan = null;
        StopButton.IsEnabled = false;
        StatusText.Text = "保存中...";
        FinalizeResult? fin = null;
        await Task.Run(() =>
        {
            try { s.Stop(); } catch (Exception ex) { error ??= ex.Message; }
            // 閉じたあとで最終のファイル名を確定する（名前は保存時に確定。録音中は仮名）
            try { if (plan != null) fin = plan.Finish(s.Elapsed, AppPaths.Ffmpeg); }
            catch (Exception ex) { error ??= "ファイル名の確定に失敗しました: " + ex.Message; }
        });
        var failure = error ?? s.Failure;
        var path = fin?.Path ?? s.OutputPath;
        var warning = fin?.Warning;
        s.Dispose();

        SetRecording(false);
        if (failure != null)
        {
            StatusText.Text = "録音が失敗しました（途中までのファイル: " + path + "）";
            ShowError("録音中にエラーが発生しました。\n" + failure);
        }
        else
        {
            var size = File.Exists(path) ? new FileInfo(path).Length : 0;
            StatusText.Text = size > 0
                ? $"保存しました: {path}（{size / 1024.0 / 1024.0:0.0} MB）"
                : "ファイルが空でした。入力デバイスに音が来ていない可能性があります: " + path;
            if (warning != null) StatusText.Text += "\n注意: " + warning;
        }
        if (_closeAfterStop) Close();
    }

    private void SetRecording(bool recording)
    {
        StartButton.IsEnabled = !recording;
        StopButton.IsEnabled = recording;
        DeviceBox.IsEnabled = FormatBox.IsEnabled = FolderBox.IsEnabled = Detail.IsEnabled = !recording;
        RefreshButton.IsEnabled = BrowseButton.IsEnabled = !recording;
        UpdateSummary();
    }

    // ---- 表示の更新（50msごと） ----
    private void OnTick()
    {
        var s = _session;
        if (s == null)
        {
            Meter.Value = Math.Max(0, Meter.Value - 0.05);
            return;
        }
        var t = s.Elapsed;
        TimeText.Text = $"{(int)t.TotalHours}：{t.Minutes:00}：{t.Seconds:00}";
        var peak = s.TakePeak();
        var level = peak <= 0 ? 0 : Math.Clamp((20 * Math.Log10(peak) + 60) / 60, 0, 1);
        Meter.Value = Math.Max(level, Meter.Value - 0.03);
        if (peak >= 0.99) _clipUntil = DateTime.Now.AddSeconds(1.5);
        var clip = DateTime.Now < _clipUntil;
        Meter.Foreground = clip ? RedBrush : GreenBrush;
        PeakText.Text = clip ? "クリップ!" : "";
    }

    private void ShowError(string message) =>
        MessageBox.Show(this, message, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_session != null && !_closeAfterStop)
        {
            var answer = MessageBox.Show(this, "録音中です。停止して保存してから終了しますか？",
                AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            e.Cancel = true;
            if (answer != MessageBoxResult.Yes) return;
            _closeAfterStop = true;
            _ = StopAsync(null);
            return;
        }
        base.OnClosing(e);
    }
}
