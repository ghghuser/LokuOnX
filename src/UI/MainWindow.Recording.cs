using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Recorder.Core;
using Recorder.Core.Win;

namespace Recorder.UI;

/// <summary>メイン画面のうち、録音・停止・一時停止・分割と、メーター表示。</summary>
public partial class MainWindow
{

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private RecordingGroup? _session;
    private bool _pauseShown;   // 一時停止ボタンが「再開」の表示か
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

    private void OnNotice(string message) => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (_session == null) return;
        _notice = message;
        ShowRecordingStatus();
    }));

    private void ShowRecordingStatus()
    {
        if (_session is not { } s) return;
        StatusText.Text = Loc.Get("rec.status.recording", s.OutputText) + (_notice.Length > 0 ? "\n" + _notice : "");
    }

    // ---- 一時停止・分割 ----
    private void Pause_Click(object sender, RoutedEventArgs e) => TogglePause();

    private void TogglePause()
    {
        if (_session is not { } s || !PauseButton.IsEnabled) return;
        if (s.Paused) s.Resume();
        else s.Pause();
        SetPauseButton(s.Paused);
    }

    private async void Split_Click(object sender, RoutedEventArgs e) => await SplitAsync();

    private async Task SplitAsync()
    {
        if (_session is not { } s || !SplitButton.IsEnabled) return;
        SplitButton.IsEnabled = false;
        try { await Task.Run(() => s.Split()); }
        finally { if (_session != null) SplitButton.IsEnabled = true; }
    }

    // ---- 停止 ----
    private async Task StopAsync(string? error)
    {
        var s = _session;
        if (s == null) return;
        _session = null;
        _scheduledStopAt = null;
        _silence = null;
        StartButton.IsEnabled = PauseButton.IsEnabled = SplitButton.IsEnabled = false;
        StatusText.Text = Loc.Get("rec.status.saving");
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
            StatusText.Text = Loc.Get("rec.err.failedWithFile", path);
            ShowError(Loc.Get("rec.err.failed", failure));
        }
        else
        {
            StatusText.Text = SavedText(results, seconds);
        }
        if (warnings.Count > 0) StatusText.Text += Loc.Get("rec.status.warn", string.Join(" / ", warnings));
        if (_closeAfterStop) Close();
        else Files.Refresh();
    }

    private static string SavedText(IReadOnlyList<FinalizeResult> results, IReadOnlyList<double> seconds)
    {
        long total = results.Sum(r => File.Exists(r.Path) ? new FileInfo(r.Path).Length : 0);
        var mb = $"{total / 1024.0 / 1024.0:0.0} MB";
        if (results.Count == 0) return Loc.Get("rec.saved.none");
        if (total == 0) return Loc.Get("rec.saved.empty", results[0].Path);
        string Sec(int i) => i < seconds.Count ? Loc.Get("rec.saved.audioSeconds", seconds[i].ToString("0.00")) : "";
        if (results.Count == 1) return Loc.Get("rec.saved.one", results[0].Path, mb, Sec(0));
        var lines = results.Take(5).Select((r, i) => r.Path + Sec(i)).ToList();
        var more = results.Count > lines.Count ? Loc.Get("rec.saved.more", results.Count - lines.Count) : "";
        return Loc.Get("rec.saved.many", results.Count, mb) + "\n" + string.Join("\n", lines) + more;
    }

    /// <summary>
    /// 録音ボタンの表示（トグル）。「● 録音開始」は文字が白にうっすら緑・●だけ赤紫（録音中の行と同じ色）、
    /// 「■ 停止」は白にうっすら青。色は画面の色（ライト／ダーク）に合わせて差し替わる。
    /// </summary>
    private void SetRecordButton(bool recording)
    {
        var label = new System.Windows.Controls.TextBlock();
        var mark = new System.Windows.Documents.Run((recording ? Loc.Get("rec.btn.markStop") : Loc.Get("rec.btn.markStart")) + " ");
        // 待機中は「待機をやめる」、自動開始がONなら開始前は「録音待機」
        var text = new System.Windows.Documents.Run(Loc.Get(recording ? (_standby ? "rec.btn.standbyCancel" : "rec.btn.stop") : (_settings.AutoStart ? "rec.btn.standby" : "rec.btn.start")));
        mark.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, recording ? "StopFg" : "RecDot");
        text.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, recording ? "StopFg" : "StartFg");
        label.Inlines.Add(mark);
        label.Inlines.Add(text);
        StartButton.Content = label;
    }

    private void SetRecording(bool recording)
    {
        // 開始と停止は1つのトグルボタン（録音中は「■ 停止」）
        StartButton.IsEnabled = true;
        SetRecordButton(recording);
        PauseButton.IsEnabled = SplitButton.IsEnabled = recording;
        SetPauseButton(false);
        FolderBox.IsEnabled = SplitBox.IsEnabled = !recording;
        Devices.SetRecording(recording);
        Quality.IsEnabled = Detail.IsEnabled = TagsTab.IsEnabled = Profiles.IsEnabled = !recording;
        SleepGuard.Prevent(recording && _settings.PreventSleep);
        BrowseButton.IsEnabled = !recording;
        if (!recording)
        {
            StateText.Text = Loc.Get("rec.state.idle");
            TotalPanel.Visibility = Visibility.Collapsed;
            Files.SetRecording(null);
        }
    }

    // ---- 表示の更新（50msごと） ----
    private void OnTick()
    {
        CheckSchedule();
        var s = _session;
        if (s == null)
        {
            if (_standby) TickStandby();
            else TickInputCheck();
            _tray?.SetState(_standby ? TrayState.Standby : TrayState.Idle);   // 待機中はトレイのアイコンが黄色いリングになる
            _tray?.SetTip((_standby ? Loc.Get("rec.tip.waitingSound", AppInfo.Name) : Loc.Get("rec.tip.idle", AppInfo.Name)) + ScheduleTip());
            return;
        }
        if (_scheduledStopAt is { } stopAt && DateTime.Now >= stopAt)
        {
            // 予約録音の長さに達した。停止して保存する
            _scheduledStopAt = null;
            _ = StopAsync(null);
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
        TotalPanel.Visibility = s.SplitCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        TotalText.Text = Clock(t);
        StateText.Text = Loc.Get("rec.state.withTrack", Loc.Get(s.Paused ? "rec.state.paused" : "rec.state.recording"), s.CurrentTrackNumber);
        if (_pauseShown != s.Paused) SetPauseButton(s.Paused);   // トレイ・ホットキー・デバイス待機での一時停止もボタンへ反映
        _tray?.SetState(s.Paused ? TrayState.Paused : TrayState.Recording);   // 録音中は赤い丸、一時停止中は一時停止マーク
        _tray?.SetTip(Loc.Get("rec.tip.running", AppInfo.Name, Loc.Get(s.Paused ? "rec.state.paused" : "rec.state.recording"), Clock(f)) + ScheduleTip());
        Files.SetRecording(Loc.Get(s.Paused ? "files.recordingPaused" : "files.recording", s.CurrentTrackNumber));
        var devicePeaks = s.TakeDevicePeaks();
        var devicePairs = s.TakeDevicePeakPairs();
        Devices.SetLevels(devicePairs);
        if (!s.Paused) Devices.PushWave(devicePeaks);
        var wave = s.TakeWave();
        if (!s.Paused) OverallWave.Push(wave);
        FeedSilence(s, SilencePeak(s.TakePeak(), devicePairs));   // 無音検出はエフェクト前（Dry）の音で（M11-4）
        var (left, right) = s.TakeChannelPeaks();
        OutMeter.Update(left, right);   // 出力レベル（L・R。白い線＝最大値、右端＝クリップ）
    }

    /// <summary>一時停止ボタンの表示（「一時停止」＝2本線の図形、「再開」＝三角の図形）。図形は文字と同じ色になる（無効のときも）。</summary>
    private void SetPauseButton(bool paused)
    {
        _pauseShown = paused;
        var icon = new System.Windows.Shapes.Path
        {
            Data = System.Windows.Media.Geometry.Parse(paused ? "M0,0 L10,5.5 L0,11 Z" : "M0,0 H3.4 V11 H0 Z M6.6,0 H10 V11 H6.6 Z"),
            Stretch = System.Windows.Media.Stretch.Uniform,
            Width = 10,
            Height = 11,
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        // 塗りの色は、ボタンの文字の色をそのまま使う（有効・無効で自動的に変わる）
        icon.SetBinding(System.Windows.Shapes.Shape.FillProperty, new System.Windows.Data.Binding
        {
            Path = new PropertyPath("(0)", System.Windows.Documents.TextElement.ForegroundProperty),
            RelativeSource = System.Windows.Data.RelativeSource.Self,
        });
        var panel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(icon);
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = Loc.Get(paused ? "rec.btn.resume" : "rec.btn.pause"), VerticalAlignment = VerticalAlignment.Center });
        PauseButton.Content = panel;
    }

    /// <summary>保存先の空き容量。1GB未満で赤文字の警告、100MB未満なら安全のため録音を止めて保存する。</summary>
    private void CheckDisk()
    {
        if (DiskSpace.FreeBytes(_settings.Folder) is not { } free) return;
        if (free < DiskSpace.StopBytes)
            _ = StopAsync(Loc.Get("rec.disk.stopped", DiskSpace.Text(free)));
        else if (free < DiskSpace.WarnBytes)
            ShowError(Loc.Get("rec.disk.low", DiskSpace.Text(free)));
    }

    private static string Clock(TimeSpan t) => $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";

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
    private bool HasDuplicateDevice(IReadOnlyList<InputSetting> inputs) =>
        inputs.Select(i => RealInputKey(i.Key)).Distinct().Count() != inputs.Count;

    /// <summary>「既定のデバイス（変更に追従）」を、いま実際に使われるデバイスの Key に置き換える（それ以外はそのまま）。</summary>
    private string RealInputKey(string key)
    {
        try { return _provider.Resolve(key) ?? key; }
        catch (Exception) { return key; }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_session != null && !_closeAfterStop)
        {
            var answer = MessageBox.Show(this, Loc.Get("rec.closeConfirm"),
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
