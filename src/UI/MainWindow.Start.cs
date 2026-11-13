using System.Windows;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>メイン画面のうち、録音の開始（録音ボタン・確認・組み立て）。停止・一時停止・分割・表示の更新は MainWindow.Recording.cs。</summary>
public partial class MainWindow
{
    private static string AsioOneRowMessage => Loc.Get("start.err.asioOneRow");

    private static string DuplicateMessage => Loc.Get("start.err.duplicate");

    /// <summary>デバイスを選んだ（チェックを変えた）時点で、同じデバイスの重複を赤文字で知らせる（録音の開始時にも調べる）。</summary>
    private void CheckDuplicateRows()
    {
        var inputs = Devices.Inputs;
        if (inputs.Count > 1 && HasDuplicateDevice(inputs)) Devices.ShowError(DuplicateMessage);
    }

    /// <summary>ASIOの行が2つ以上選ばれたら、選んだ時点で赤文字を出す（録音開始時にも同じ確認をする）。</summary>
    private void CheckAsioRows()
    {
        if (Devices.Inputs.Count(i => AsioKey.Is(i.Key)) > 1) Devices.ShowError(AsioOneRowMessage);
    }

    // ---- 開始 ----
    private async void Start_Click(object sender, RoutedEventArgs e) => await ToggleAsync();

    /// <summary>録音ボタン（トレイ・ホットキーも同じ）。録音中は停止、待機中は待機をやめる、それ以外は開始（自動開始がONなら待機）。</summary>
    private async Task ToggleAsync()
    {
        if (_session != null)   // 録音中は、同じボタンが「停止」になる
        {
            await StopAsync(null);
            return;
        }
        if (!StartButton.IsEnabled) return;   // 保存中
        if (_standby)
        {
            EndStandby(Loc.Get("start.standbyCancelled"));
            return;
        }
        if (_settings.AutoStart) BeginStandby();
        else StartRecording();
    }

    /// <summary>入力デバイス・FFmpeg を調べる。問題があれば画面に赤文字で出して false。</summary>
    private bool ValidateStart(IReadOnlyList<InputSetting> inputs)
    {
        if (inputs.Count == 0)
        {
            Devices.ShowError(Loc.Get("start.err.noDevice"));
            return false;
        }
        if (HasDuplicateDevice(inputs))
        {
            Devices.ShowError(DuplicateMessage);
            return false;
        }
        if (inputs.Count(i => AsioKey.Is(i.Key)) > 1)
        {
            Devices.ShowError(AsioOneRowMessage);
            return false;
        }
        if (!File.Exists(AppPaths.Ffmpeg))
        {
            ShowError(Loc.Get("start.err.noFfmpeg", AppPaths.Ffmpeg));
            return false;
        }
        return true;
    }

    /// <summary>録音を始める。始められたら true（失敗は画面に赤文字で出す）。</summary>
    private bool StartRecording()
    {
        var inputs = Devices.Inputs;
        ClearErrors();
        if (!ValidateStart(inputs)) return false;
        StopCheckMonitor();   // 入力チェックで開いていたデバイスは、録音の前に手放す

        RecordingGroup? group = null;
        try
        {
            CommitInputs();
            AddRecentFolder(_settings.Folder);
            if (DiskSpace.FreeBytes(_settings.Folder) is { } free && free < DiskSpace.StopBytes)
            {
                ShowError(Loc.Get("start.err.diskLow", DiskSpace.Text(free)));
                return false;
            }
            var no = _settings.NextNo;
            group = RecordingGroup.Create(_hub, _provider, _settings, inputs, _devices, AppPaths.Ffmpeg, DateTime.Now);
            group.Error += msg => Dispatcher.BeginInvoke(new Action(async () => await StopAsync(msg)));
            group.Notice += OnNotice;
            group.Saved += path => Dispatcher.BeginInvoke(new Action(() =>
            {
                _settings.AddHistory(path);   // 録音履歴へ（空にするまで残る）
                Files.Refresh();
            }));
            group.Start();
            _session = group;
            if (!_settings.ReadOnly) _settings.NextNo = no + 1;
        }
        catch (Exception ex)
        {
            group?.Dispose();
            ShowError(Loc.Get("start.err.cannotStart", ex.Message));
            return false;
        }
        Devices.StartWave();
        OverallWave.Clear();
        _notice = "";
        _nextDiskCheck = DateTime.MinValue;   // 開始直後に1回、あとは30秒ごとに空き容量を調べる
        _scheduledStopAt = null;
        _silence = _settings.SilenceSplit ? new SilenceDetector(_settings.SilenceDb, _settings.SilenceSeconds) : null;
        _lastSilenceTick = DateTime.Now;
        SetRecording(true);
        Detail.Reload();   // {No} が進んだので画面へ反映
        UpdateInfo();
        ShowRecordingStatus();
        return true;
    }
}
