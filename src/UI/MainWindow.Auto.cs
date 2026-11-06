using System.Windows;
using Recorder.Core;
using Recorder.Core.Win;

namespace Recorder.UI;

/// <summary>メイン画面のうち、予約録音・「音が出たら自動で録音開始」（待機）・無音での自動分割（PROJECT.md §3-8）。</summary>
public partial class MainWindow
{
    private DateTime? _scheduleAt;          // 次の予約の時刻（予約なしは null）
    private DateTime? _scheduledStopAt;     // 予約録音を自動で止める時刻
    private LevelMonitor? _monitor;         // 待機中に音量を見る
    private bool _standby;                  // 「音が出たら自動で録音開始」の待機中
    private SilenceDetector? _silence;      // 録音中の無音検出（OFFなら null）
    private LevelMonitor? _checkMon;        // 入力チェック（録音していないあいだもメーターを動かす）の音量監視
    private string _checkSig = "";          // 入力チェックで開いているデバイスの組み合わせ（変わったら開き直す）
    private DateTime _nextCheckTry = DateTime.MinValue;
    private DateTime _lastSilenceTick = DateTime.Now;

    // ---- 予約録音 ----
    /// <summary>設定から次の予約の時刻を計算し直す（2つ目の起動では予約しない）。</summary>
    private void ArmSchedule()
    {
        _scheduleAt = _settings.ScheduleOn && !_settings.ReadOnly
            ? ClockTime.Next(DateTime.Now, _settings.ScheduleTime)
            : null;
        Plans.ShowNext(_scheduleAt);
    }

    /// <summary>50msごと。予約の時刻になっていれば録音を始める。</summary>
    private void CheckSchedule()
    {
        if (_scheduleAt is not { } at || DateTime.Now < at) return;
        var now = DateTime.Now;
        if (_settings.ScheduleDaily)
        {
            _scheduleAt = ClockTime.Next(now, _settings.ScheduleTime);
        }
        else
        {
            _scheduleAt = null;
            _settings.ScheduleOn = false;   // 1回だけの予約は、実行したらOFFに戻す
            Plans.Reload();
        }
        Plans.ShowNext(_scheduleAt);

        if (now - at > TimeSpan.FromSeconds(60))
        {
            StatusText.Text = $"予約の時刻（{at:HH:mm:ss}）を過ぎていたため、予約録音は見送りました（パソコンがスリープしていた可能性があります）。";
            return;
        }
        if (_session != null || !StartButton.IsEnabled)
        {
            StatusText.Text = $"予約の時刻（{at:HH:mm:ss}）でしたが、録音中（または保存中）のため開始しませんでした。";
            return;
        }
        if (_standby) EndStandby(null);
        if (!StartRecording()) return;
        if (_settings.ScheduleSeconds > 0) _scheduledStopAt = DateTime.Now.AddSeconds(_settings.ScheduleSeconds);
        _notice = "予約録音です" + (_scheduledStopAt is { } stop ? $"（{stop:HH:mm:ss} に自動で停止します）" : "（手動で停止するまで続けます）");
        ShowRecordingStatus();
    }

    // ---- 無音での自動分割 ----
    private void FeedSilence(RecordingGroup s, float peak)
    {
        var now = DateTime.Now;
        var dt = Math.Min((now - _lastSilenceTick).TotalSeconds, 1.0);
        _lastSilenceTick = now;
        if (_silence == null || s.Paused) return;
        if (_silence.Feed(peak, dt)) _ = SplitAsync();
    }

    // ---- 音が出たら自動で録音開始（待機） ----
    private bool BeginStandby()
    {
        var inputs = Devices.Inputs;
        ClearErrors();
        if (!ValidateStart(inputs)) return false;
        StopCheckMonitor();
        try
        {
            var monitor = new LevelMonitor(_provider, inputs);
            monitor.Start();
            _monitor = monitor;
        }
        catch (Exception ex)
        {
            ShowError("待機を始められませんでした。" + ex.Message);
            return false;
        }
        _standby = true;
        SetStandbyUi(true);
        StatusText.Text = $"音を待っています（−{-_settings.AutoStartDb} dB を超えたら録音を始めます）。もう一度押すと待機をやめます。";
        return true;
    }

    /// <summary>待機をやめる（画面を戻す）。message があれば状態表示に出す。</summary>
    private void EndStandby(string? message)
    {
        if (!_standby) return;
        _standby = false;
        StopMonitor();
        SetStandbyUi(false);
        if (message != null) StatusText.Text = message;
    }

    private void StopMonitor()
    {
        var monitor = _monitor;
        _monitor = null;
        if (monitor == null) return;
        Task.Run(() =>
        {
            try { monitor.Stop(); } catch (Exception) { }
            monitor.Dispose();
        });
    }

    private void SetStandbyUi(bool on)
    {
        StartButton.IsEnabled = true;
        SetRecordButton(on);
        FolderBox.IsEnabled = SplitBox.IsEnabled = BrowseButton.IsEnabled = !on;
        Devices.SetRecording(on);
        Quality.IsEnabled = Detail.IsEnabled = Presets.IsEnabled = !on;
        StateText.Text = on ? "待機中（音を待っています）" : "待機中";
        SleepGuard.Prevent(on && _settings.PreventSleep);   // 待機中にスリープすると音を待てない
    }

    /// <summary>待機中の50msごと。しきい値を超えたら録音を始める（録音を始めてから、待機のほうを止める）。</summary>
    private void TickStandby()
    {
        if (_monitor is not { } monitor) return;
        var peaks = monitor.TakePeaks();
        var peak = peaks.Length == 0 ? 0f : peaks.Max();
        Devices.SetLevels(peaks);
        ShowMeter(peak);
        if (peak < SilenceDetector.ToLinear(_settings.AutoStartDb)) return;

        _standby = false;
        _monitor = null;
        var started = StartRecording();
        if (!started) SetStandbyUi(false);
        else
        {
            _notice = "音が出たため、自動で録音を始めました。";
            ShowRecordingStatus();
        }
        Task.Run(() =>
        {
            try { monitor.Stop(); } catch (Exception) { }
            monitor.Dispose();
        });
    }

    // ---- 入力チェック（録音していないあいだもメーターを動かす） ----
    private void InputCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.InputCheck = InputCheckBox.IsChecked == true;
        _nextCheckTry = DateTime.MinValue;
    }

    /// <summary>録音も待機もしていないときの50msごと。入力チェックがONなら、選んだデバイスの音量をメーターへ出す。</summary>
    private void TickInputCheck()
    {
        var inputs = _settings.InputCheck && IsVisible && WindowState != WindowState.Minimized
            ? Devices.Inputs
            : Array.Empty<InputSetting>();
        if (inputs.Count == 0)
        {
            StopCheckMonitor();
            Devices.SetLevels(Array.Empty<float>());
            Meter.Value = Math.Max(0, Meter.Value - 0.05);
            PeakText.Text = "";
            return;
        }
        var sig = string.Join("|", inputs.Select(i => i.Key));
        if (_checkMon != null && sig != _checkSig) StopCheckMonitor();   // 選ぶデバイスが変わった
        if (_checkMon == null)
        {
            if (DateTime.Now < _nextCheckTry) return;
            try
            {
                var monitor = new LevelMonitor(_provider, inputs);
                monitor.Start();
                _checkMon = monitor;
                _checkSig = sig;
            }
            catch (Exception)
            {
                _nextCheckTry = DateTime.Now.AddSeconds(5);   // 開けないとき（デバイスが無いなど）は、5秒おきに試す
                return;
            }
        }
        for (int i = 0; i < inputs.Count; i++) _checkMon.SetGain(i, inputs[i].GainDb);   // ゲインのスライダーも反映
        var peaks = _checkMon.TakePeaks();
        Devices.SetLevels(peaks);
        ShowMeter(peaks.Length == 0 ? 0f : peaks.Max());
    }

    private void StopCheckMonitor()
    {
        var monitor = _checkMon;
        _checkMon = null;
        _checkSig = "";
        if (monitor == null) return;
        Task.Run(() =>
        {
            try { monitor.Stop(); } catch (Exception) { }
            monitor.Dispose();
        });
    }
}
