using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>メイン画面のうち、録音・停止・一時停止・分割と、メーター表示。</summary>
public partial class MainWindow
{
    private static readonly Brush GreenBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x9E, 0x4F));
    private static readonly Brush RedBrush = new SolidColorBrush(Color.FromRgb(0xD3, 0x33, 0x33));

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private RecordingSession? _session;
    private DateTime _clipUntil = DateTime.MinValue;
    private bool _closeAfterStop;
    private string _notice = "";

    private void StartTimer()
    {
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
    }

    private Task RecoverAsync() =>
        RecoveryPrompt.RunAsync(this, _settings, () => _session != null, text => StatusText.Text = text);

    // ---- 開始 ----
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
            Split_LostFocus(sender, e);
            var no = _settings.NextNo;
            var plan = RecordingPlan.Create(_settings, dev.Label, DateTime.Now);
            Directory.CreateDirectory(plan.Folder);
            src = _provider.Create(dev.Key);
            session = new RecordingSession(src, AppPaths.Ffmpeg, plan, TimeSpan.FromMinutes(_settings.SplitMinutes));
            session.Error += msg => Dispatcher.BeginInvoke(new Action(async () => await StopAsync(msg)));
            session.Notice += OnNotice;
            session.Start();
            _session = session;
            if (!_settings.ReadOnly) _settings.NextNo = no + 1;
        }
        catch (Exception ex)
        {
            if (session != null) session.Dispose(); else src?.Dispose();
            ShowError("録音を開始できませんでした。\n" + ex.Message);
            return;
        }
        _notice = "";
        SetRecording(true);
        Detail.Reload();   // {No} が進んだので画面へ反映
        UpdateInfo();
        ShowRecordingStatus();
    }

    private void OnNotice(string message) => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (_session == null) return;
        _notice = message;
        ShowRecordingStatus();
    }));

    private void ShowRecordingStatus()
    {
        if (_session is not { } s) return;
        StatusText.Text = "録音中: " + s.OutputPath + (_notice.Length > 0 ? "\n" + _notice : "");
    }

    // ---- 一時停止・分割 ----
    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (_session is not { } s) return;
        if (s.Paused) { s.Resume(); PauseButton.Content = "一時停止"; }
        else { s.Pause(); PauseButton.Content = "再開"; }
    }

    private async void Split_Click(object sender, RoutedEventArgs e)
    {
        if (_session is not { } s) return;
        SplitButton.IsEnabled = false;
        try { await Task.Run(() => s.Split()); }
        finally { if (_session != null) SplitButton.IsEnabled = true; }
    }

    // ---- 停止 ----
    private async void Stop_Click(object sender, RoutedEventArgs e) => await StopAsync(null);

    private async Task StopAsync(string? error)
    {
        var s = _session;
        if (s == null) return;
        _session = null;
        StopButton.IsEnabled = PauseButton.IsEnabled = SplitButton.IsEnabled = false;
        StatusText.Text = "保存中...";
        await Task.Run(() =>
        {
            // 閉じたあとで最終のファイル名を確定する（名前は保存時に確定。録音中は仮名）
            try { s.Stop(); } catch (Exception ex) { error ??= ex.Message; }
        });
        var failure = error ?? s.Failure;
        var results = s.Results;
        var path = results.Count > 0 ? results[results.Count - 1].Path : s.OutputPath;
        var warnings = results.Where(r => r.Warning != null).Select(r => r.Warning!).Distinct().ToList();
        s.Dispose();

        SetRecording(false);
        if (failure != null)
        {
            StatusText.Text = "録音が失敗しました（途中までのファイル: " + path + "）";
            ShowError("録音中にエラーが発生しました。\n" + failure);
        }
        else
        {
            StatusText.Text = SavedText(results);
        }
        if (warnings.Count > 0) StatusText.Text += "\n注意: " + string.Join(" / ", warnings);
        if (_closeAfterStop) Close();
    }

    private static string SavedText(IReadOnlyList<FinalizeResult> results)
    {
        long total = results.Sum(r => File.Exists(r.Path) ? new FileInfo(r.Path).Length : 0);
        var mb = $"{total / 1024.0 / 1024.0:0.0} MB";
        if (results.Count == 0) return "保存されたファイルがありません。";
        if (total == 0) return "ファイルが空でした。入力デバイスに音が来ていない可能性があります: " + results[0].Path;
        if (results.Count == 1) return $"保存しました: {results[0].Path}（{mb}）";
        var lines = results.Take(5).Select(r => r.Path).ToList();
        var more = results.Count > lines.Count ? $"\n…ほか {results.Count - lines.Count} 個" : "";
        return $"{results.Count} 個のファイルを保存しました（合計 {mb}）:\n" + string.Join("\n", lines) + more;
    }

    private void SetRecording(bool recording)
    {
        StartButton.IsEnabled = !recording;
        StopButton.IsEnabled = PauseButton.IsEnabled = SplitButton.IsEnabled = recording;
        PauseButton.Content = "一時停止";
        DeviceBox.IsEnabled = FormatBox.IsEnabled = FolderBox.IsEnabled = SplitBox.IsEnabled = !recording;
        Quality.IsEnabled = Detail.IsEnabled = !recording;
        RefreshButton.IsEnabled = BrowseButton.IsEnabled = !recording;
        if (!recording) StateText.Text = "待機中";
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
        StateText.Text = (s.Paused ? "一時停止中" : "録音中") + $" ― トラック {s.CurrentTrackNumber}";
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
