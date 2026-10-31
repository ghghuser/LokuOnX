using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Recorder.Core;

namespace Recorder.UI;

public partial class MainWindow : Window
{
    private readonly string _ffmpegPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe");
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private RecorderSession? _session;
    private DateTime _startedAt;
    private double _level;
    private bool _stopping;
    private bool _closeAfterStop;

    public MainWindow()
    {
        InitializeComponent();

        Title = AppInfo.Name;
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
        VersionText.Text = "バージョン " + version;
        FfmpegText.Text = File.Exists(_ffmpegPath) ? "FFmpeg: 同梱を確認しました" : "FFmpeg: 見つかりません";
        FolderBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), AppInfo.Name);

        _timer.Tick += OnTick;
        LoadDevices();
    }

    private void LoadDevices()
    {
        var selectedId = (DeviceCombo.SelectedItem as DeviceItem)?.Id;
        try
        {
            var list = DeviceList.GetCaptureDevices();
            DeviceCombo.ItemsSource = list;
            DeviceCombo.SelectedItem = list.FirstOrDefault(d => d.Id == selectedId) ?? list[0];
        }
        catch (Exception ex)
        {
            Warn("入力デバイスの一覧を取得できませんでした。\n" + ex.Message);
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => LoadDevices();

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "保存先フォルダを選択", InitialDirectory = FolderBox.Text };
        if (dlg.ShowDialog(this) == true) FolderBox.Text = dlg.FolderName;
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session != null || _stopping) return;
        if (!File.Exists(_ffmpegPath))
        {
            Warn("ffmpeg.exe が見つかりません。ZIPをすべて展開した状態で起動してください。");
            return;
        }

        var deviceId = (DeviceCombo.SelectedItem as DeviceItem)?.Id;
        var format = FormatCombo.SelectedIndex == 1 ? OutputFormat.Mp3 : OutputFormat.Flac;
        RecorderSession? session = null;
        try
        {
            var path = OutputFile.Create(FolderBox.Text.Trim(), format);
            session = new RecorderSession(deviceId, _ffmpegPath, path, format);
            session.Faulted += OnFaulted;
            session.Start();
        }
        catch (Exception ex)
        {
            session?.Dispose();
            Warn("録音を開始できませんでした。\n" + ex.Message);
            return;
        }

        _session = session;
        _startedAt = DateTime.Now;
        _level = 0;
        SetRecordingUi(true);
        StatusText.Text = "録音中: " + session.OutputPath;
        _timer.Start();
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e) => await StopRecordingAsync();

    private async void OnFaulted(string message)
    {
        await StopRecordingAsync();
        Warn("録音が中断されました。ここまでの音声は保存されています。\n" + message);
    }

    private async Task StopRecordingAsync()
    {
        var session = _session;
        if (session == null || _stopping) return;
        _stopping = true;
        _session = null;
        StopButton.IsEnabled = false;
        StatusText.Text = "保存しています…";

        string? error = null;
        try { await session.StopAsync(); }
        catch (Exception ex) { error = ex.Message; }

        var path = session.OutputPath;
        if (session.EncoderFailed && error == null) error = "FFmpegでエラーが発生しました。\n" + session.EncoderLog;
        session.Dispose();

        _timer.Stop();
        MeterBar.Value = 0;
        PeakText.Text = "-∞ dB";
        SetRecordingUi(false);
        StatusText.Text = error == null ? "保存しました: " + path : "保存時に問題が発生しました: " + path;
        _stopping = false;

        if (error != null) Warn(error);
        if (_closeAfterStop) Close();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var session = _session;
        if (session == null) return;

        var peak = session.TakePeak();
        var db = peak > 0.00001f ? 20 * Math.Log10(peak) : double.NegativeInfinity;
        var v = double.IsNegativeInfinity(db) ? 0 : Math.Clamp((db + 60) / 60, 0, 1);
        _level = v > _level ? v : Math.Max(v, _level - 0.02);
        MeterBar.Value = _level;
        PeakText.Text = double.IsNegativeInfinity(db) ? "-∞ dB" : $"{db:0.0} dB";
        ElapsedText.Text = (DateTime.Now - _startedAt).ToString(@"h\:mm\:ss");
    }

    private void SetRecordingUi(bool recording)
    {
        StartButton.IsEnabled = !recording;
        StopButton.IsEnabled = recording;
        DeviceCombo.IsEnabled = !recording;
        RefreshButton.IsEnabled = !recording;
        FormatCombo.IsEnabled = !recording;
        FolderBox.IsEnabled = !recording;
        BrowseButton.IsEnabled = !recording;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_session != null || _stopping)
        {
            // 録音中・保存中に閉じられたら、いったん止めて保存してから閉じる
            e.Cancel = true;
            _closeAfterStop = true;
            _ = StopRecordingAsync();
        }
        base.OnClosing(e);
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
}
