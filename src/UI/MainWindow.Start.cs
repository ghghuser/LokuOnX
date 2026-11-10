using System.Windows;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>メイン画面のうち、録音の開始（録音ボタン・確認・組み立て）。停止・一時停止・分割・表示の更新は MainWindow.Recording.cs。</summary>
public partial class MainWindow
{
    private const string AsioOneRowMessage =
        "ASIOは同時に1行しか使えません（ドライバを専有するため）。他の行はマイクやスピーカー（WASAPI）にするか、チェックを外してください。";

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
            EndStandby("待機をやめました。");
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
            Devices.ShowError("入力デバイスが選ばれていません。「（未選択）」ではなくデバイスを選び、左のチェックを入れてください。");
            return false;
        }
        if (HasDuplicateDevice(inputs))
        {
            Devices.ShowError("同じデバイスが重複しています（「既定のデバイス（変更に追従）」と、その実際のデバイスも同じ扱いです）。どちらかを外してください。");
            return false;
        }
        if (inputs.Count(i => AsioKey.Is(i.Key)) > 1)
        {
            Devices.ShowError(AsioOneRowMessage);
            return false;
        }
        if (!File.Exists(AppPaths.Ffmpeg))
        {
            ShowError($"FFmpeg が見つかりません: {AppPaths.Ffmpeg}");
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
                ShowError($"保存先のドライブの空きが {DiskSpace.Text(free)} しかないため、録音を始められません。空き容量を増やすか、保存先を変えてください。");
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
            ShowError("録音を開始できませんでした。" + ex.Message);
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
