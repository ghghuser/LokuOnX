using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Recorder.Core;
using Recorder.Core.Win;

namespace Recorder.UI;

/// <summary>メイン画面のうち、録音・停止・一時停止・分割と、メーター表示。</summary>
public partial class MainWindow
{
    private static readonly Brush GreenBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x9E, 0x4F));
    private static readonly Brush RedBrush = new SolidColorBrush(Color.FromRgb(0xD3, 0x33, 0x33));

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private RecordingGroup? _session;
    private DateTime _clipUntil = DateTime.MinValue;
    private bool _closeAfterStop;
    private string _notice = "";
    private DateTime _nextDiskCheck = DateTime.MinValue;

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
        var inputs = Devices.Inputs;
        ClearErrors();
        if (inputs.Count == 0)
        {
            Devices.ShowError("入力デバイスが選ばれていません。「（未選択）」ではなくデバイスを選び、左のチェックを入れてください。");
            return;
        }
        if (HasDuplicateDevice(inputs))
        {
            Devices.ShowError("同じデバイスが重複しています（「既定のデバイス（変更に追従）」と、その実際のデバイスも同じ扱いです）。どちらかを外してください。");
            return;
        }
        if (!File.Exists(AppPaths.Ffmpeg))
        {
            ShowError($"FFmpeg が見つかりません: {AppPaths.Ffmpeg}");
            return;
        }

        RecordingGroup? group = null;
        try
        {
            CommitInputs();
            if (DiskSpace.FreeBytes(_settings.Folder) is { } free && free < DiskSpace.StopBytes)
            {
                ShowError($"保存先のドライブの空きが {DiskSpace.Text(free)} しかないため、録音を始められません。空き容量を増やすか、保存先を変えてください。");
                return;
            }
            var no = _settings.NextNo;
            group = RecordingGroup.Create(_provider, _settings, inputs, _devices, AppPaths.Ffmpeg, DateTime.Now);
            group.Error += msg => Dispatcher.BeginInvoke(new Action(async () => await StopAsync(msg)));
            group.Notice += OnNotice;
            group.Start();
            _session = group;
            if (!_settings.ReadOnly) _settings.NextNo = no + 1;
        }
        catch (Exception ex)
        {
            group?.Dispose();
            ShowError("録音を開始できませんでした。" + ex.Message);
            return;
        }
        Wave.SetRows(inputs.Select(i => _devices.FirstOrDefault(d => d.Key == i.Key)?.Label ?? "（不明）").ToList());
        _notice = "";
        _nextDiskCheck = DateTime.MinValue;   // 開始直後に1回、あとは30秒ごとに空き容量を調べる
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
        StatusText.Text = "録音中: " + s.OutputText + (_notice.Length > 0 ? "\n" + _notice : "");
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
        var seconds = s.Seconds;
        var path = results.Count > 0 ? results[results.Count - 1].Path : s.OutputText;
        var warnings = results.Where(r => r.Warning != null).Select(r => r.Warning!).Distinct().ToList();
        s.Dispose();

        SetRecording(false);
        if (failure != null)
        {
            StatusText.Text = "録音が失敗しました（途中までのファイル: " + path + "）";
            ShowError("録音中にエラーが発生しました。" + failure);
        }
        else
        {
            StatusText.Text = SavedText(results, seconds);
        }
        if (warnings.Count > 0) StatusText.Text += "\n注意: " + string.Join(" / ", warnings);
        if (_closeAfterStop) Close();
        else Files.Refresh();   // できたファイルを一覧へ
    }

    private static string SavedText(IReadOnlyList<FinalizeResult> results, IReadOnlyList<double> seconds)
    {
        long total = results.Sum(r => File.Exists(r.Path) ? new FileInfo(r.Path).Length : 0);
        var mb = $"{total / 1024.0 / 1024.0:0.0} MB";
        if (results.Count == 0) return "保存されたファイルがありません。";
        if (total == 0) return "ファイルが空でした。入力デバイスに音が来ていない可能性があります: " + results[0].Path;
        string Sec(int i) => i < seconds.Count ? $"（音声 {seconds[i]:0.00}秒）" : "";
        if (results.Count == 1) return $"保存しました: {results[0].Path}（{mb}）{Sec(0)}";
        var lines = results.Take(5).Select((r, i) => r.Path + Sec(i)).ToList();
        var more = results.Count > lines.Count ? $"\n…ほか {results.Count - lines.Count} 個" : "";
        return $"{results.Count} 個のファイルを保存しました（合計 {mb}）:\n" + string.Join("\n", lines) + more;
    }

    private void SetRecording(bool recording)
    {
        StartButton.IsEnabled = !recording;
        StopButton.IsEnabled = PauseButton.IsEnabled = SplitButton.IsEnabled = recording;
        PauseButton.Content = "一時停止";
        FormatBox.IsEnabled = FolderBox.IsEnabled = SplitBox.IsEnabled = !recording;
        Devices.SetRecording(recording);
        Quality.IsEnabled = Detail.IsEnabled = Presets.IsEnabled = !recording;
        SleepGuard.Prevent(recording && _settings.PreventSleep);
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
        if (DateTime.Now >= _nextDiskCheck)
        {
            _nextDiskCheck = DateTime.Now.AddSeconds(30);
            CheckDisk();
            if (_session == null) return;
        }
        var t = s.Elapsed;
        // 大きい表示 = いまのファイルを始めてから。分割したあとは、横に（連続 ○）で録音全体の時間も出す
        var f = s.TrackElapsed;
        TimeText.Text = Clock(f);
        TotalText.Text = s.SplitCount > 0 ? "（連続 " + Clock(t) + "）" : "";
        StateText.Text = (s.Paused ? "一時停止中" : "録音中") + $" ― トラック {s.CurrentTrackNumber}";
        var devicePeaks = s.TakeDevicePeaks();
        Devices.SetLevels(devicePeaks);
        if (!s.Paused) Wave.Push(devicePeaks);
        var peak = s.TakePeak();
        var level = peak <= 0 ? 0 : Math.Clamp((20 * Math.Log10(peak) + 60) / 60, 0, 1);
        Meter.Value = Math.Max(level, Meter.Value - 0.03);
        if (peak >= 0.99) _clipUntil = DateTime.Now.AddSeconds(1.5);
        var clip = DateTime.Now < _clipUntil;
        Meter.Foreground = clip ? RedBrush : GreenBrush;
        PeakText.Text = clip ? "クリップ!" : "";
    }

    /// <summary>保存先の空き容量。1GB未満で赤文字の警告、100MB未満なら安全のため録音を止めて保存する。</summary>
    private void CheckDisk()
    {
        if (DiskSpace.FreeBytes(_settings.Folder) is not { } free) return;
        if (free < DiskSpace.StopBytes)
            _ = StopAsync($"保存先の空き容量が {DiskSpace.Text(free)} になったため、データを守るために録音を止めて保存しました。");
        else if (free < DiskSpace.WarnBytes)
            ShowError($"保存先のドライブの空きが少なくなっています（残り {DiskSpace.Text(free)}）。空きがなくなる前に録音を止めて、不要なファイルを整理してください。");
    }

    private static string Clock(TimeSpan t) => $"{(int)t.TotalHours}：{t.Minutes:00}：{t.Seconds:00}";

    /// <summary>エラーは音の鳴るポップアップではなく、画面内に赤文字で出す。</summary>
    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void ClearErrors()
    {
        ErrorText.Text = "";
        ErrorText.Visibility = Visibility.Collapsed;
        Devices.ClearError();
    }

    /// <summary>「既定のデバイス」は実際のデバイスに置き換えて、同じデバイスが2回選ばれていないか調べる。</summary>
    private bool HasDuplicateDevice(IReadOnlyList<InputSetting> inputs)
    {
        string Real(string key)
        {
            try { return _provider.Resolve(key) ?? key; }
            catch (Exception) { return key; }
        }
        return inputs.Select(i => Real(i.Key)).Distinct().Count() != inputs.Count;
    }

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
