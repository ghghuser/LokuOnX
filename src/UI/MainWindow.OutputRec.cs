using System.Windows;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>
/// メイン画面のうち、出力の録音（M13）。出力ごとの「録音に含める」（メインの録音と一緒に録る）と、単独録音（この出力だけ録る）。
/// 一緒に録る出力は RecordingGroup が持ち、単独録音は _outRecs が持つ。出力の録音の失敗は入力の録音を止めない。
/// </summary>
public partial class MainWindow
{
    private readonly Dictionary<OutputEngine, OutputRecording> _outRecs = new();   // 単独録音中の出力

    /// <summary>録音を始めるとき、メインの録音と一緒に録る出力の一覧（「録音に含める」がオンで、単独録音中でないもの）。</summary>
    private IReadOnlyList<(OutputEngine Engine, string Name, int Index)> OutputRecTargets()
    {
        var list = new List<(OutputEngine, string, int)>();
        for (int i = 0; i < _outCfgs.Count && i < _outHub.Count; i++)
            if (_settings.OutputRec(i) && !_outRecs.ContainsKey(_outHub[i])) list.Add((_outHub[i], _outCfgs[i].Name, i));
        return list;
    }

    /// <summary>単独録音中の出力のうち「録音に含める」がオンのものを、メインの録音へ引き継ぐ（メインの停止などで一緒に止まるようにする）。</summary>
    private void AdoptStandaloneOutputRecs(RecordingGroup group)
    {
        for (int i = 0; i < _outCfgs.Count && i < _outHub.Count; i++)
        {
            var engine = _outHub[i];
            if (!_settings.OutputRec(i) || !_outRecs.TryGetValue(engine, out var rec)) continue;
            _outRecs.Remove(engine);
            group.Adopt(rec);
        }
    }

    /// <summary>ファイル一覧の先頭の「● Recording…」行を、録音中のファイルの数だけ出す（入力の別ファイル録音・出力の録音・単独録音を含む）。</summary>
    private void UpdateRecordingRows()
    {
        var rows = new List<string>();
        if (_session != null)
            foreach (var (paused, track) in _session.States)
                rows.Add(Loc.Get(paused ? "files.recordingPaused" : "files.recording", track));
        foreach (var rec in _outRecs.Values)
            rows.Add(Loc.Get(rec.Session.Paused ? "files.recordingPaused" : "files.recording", rec.Session.CurrentTrackNumber));
        Files.SetRecordings(rows);
    }

    bool IOutputHost.OutputCaution(int index)
    {
        if (index < 0 || index >= _outCfgs.Count) return false;
        var id = _outCfgs[index].DeviceId;
        if (id.Length == 0 || ((IOutputHost)this).IsLoopOutput(id)) return false;
        var real = ((IOutputHost)this).RealOutputId(id);
        for (int i = 0; i < _outCfgs.Count; i++)
            if (i != index && _outCfgs[i].DeviceId.Length > 0 && ((IOutputHost)this).RealOutputId(_outCfgs[i].DeviceId) == real) return true;
        return false;
    }

    bool IOutputHost.OutputRec(int index) => _settings.OutputRec(index);

    void IOutputHost.SetOutputRec(int index, bool on)
    {
        _settings.SetOutputRec(index, on);
        if (!on) DetachOutputRec(index);   // メインの録音中にオフにしたら切り離す。以後は個別に止められる
    }

    int IOutputHost.OutputRecMode(int index)
    {
        if (index < 0 || index >= _outHub.Count) return 0;
        if (_outRecs.ContainsKey(_outHub[index])) return 1;
        return _session != null && _session.OutputIndexes.Contains(index) ? 2 : 0;
    }

    TimeSpan IOutputHost.OutputRecElapsed(int index)
    {
        if (index < 0 || index >= _outHub.Count) return TimeSpan.Zero;
        if (_outRecs.TryGetValue(_outHub[index], out var rec)) return rec.Session.Elapsed;
        return _session != null && _session.OutputIndexes.Contains(index) ? _session.Elapsed : TimeSpan.Zero;
    }

    void IOutputHost.ToggleOutputRecording(int index)
    {
        if (index < 0 || index >= _outHub.Count || index >= _outCfgs.Count) return;
        var engine = _outHub[index];
        if (_outRecs.TryGetValue(engine, out var running))
        {
            _ = StopOutputRecAsync(engine, running, null);
            return;
        }
        if (_session != null && _session.OutputIndexes.Contains(index)) return;   // メインの録音と一緒に録っている間は、メインの停止で止まる
        StartOutputRec(index, engine);
    }

    void IOutputHost.SetAllOutputRec(bool on)
    {
        for (int i = 0; i < _outCfgs.Count; i++) ((IOutputHost)this).SetOutputRec(i, on);
    }

    void IOutputHost.SetAllOutputRecording(bool start)
    {
        for (int i = 0; i < _outCfgs.Count && i < _outHub.Count; i++)
        {
            var engine = _outHub[i];
            if (start)
            {
                if (!_outRecs.ContainsKey(engine) && engine.Running && !(_session != null && _session.OutputIndexes.Contains(i)))
                    StartOutputRec(i, engine);
            }
            else if (_outRecs.TryGetValue(engine, out var rec))
            {
                _ = StopOutputRecAsync(engine, rec, null);
            }
        }
    }

    private void StartOutputRec(int index, OutputEngine engine)
    {
        var name = _outCfgs[index].Name;
        OutputRecording? rec = null;
        try
        {
            if (!File.Exists(AppPaths.Ffmpeg))
            {
                ShowError(Loc.Get("start.err.noFfmpeg", AppPaths.Ffmpeg));
                return;
            }
            if (DiskSpace.FreeBytes(_settings.Folder) is { } free && free < DiskSpace.StopBytes)
            {
                ShowError(Loc.Get("start.err.diskLow", DiskSpace.Text(free)));
                return;
            }
            var no = _settings.NextNo;
            rec = OutputRecording.Create(engine, name, index, _settings, AppPaths.Ffmpeg, DateTime.Now);
            if (rec == null)
            {
                ShowError(Loc.Get("outputrec.err.notRunning", name));
                return;
            }
            WireStandalone(engine, rec);
            rec.Start();
            _outRecs[engine] = rec;
            if (!_settings.ReadOnly) _settings.NextNo = no + 1;
            Detail.Reload();   // {No} が進んだので画面へ反映
            OutLamps.Refresh(this, _session != null);
        }
        catch (Exception ex)
        {
            rec?.Dispose();
            ShowError(Loc.Get("outputrec.err.cannotStart", name, ex.Message));
        }
    }

    private readonly HashSet<OutputRecording> _wiredRecs = new();

    /// <summary>単独録音として扱う録音に、失敗時の停止・保存時の履歴追加をつなぐ（1つの録音につき1回だけ）。</summary>
    private void WireStandalone(OutputEngine engine, OutputRecording rec)
    {
        if (!_wiredRecs.Add(rec)) return;
        rec.Error += msg => Dispatcher.BeginInvoke(new Action(() => _ = StopOutputRecAsync(engine, rec, msg)));
        rec.Saved += path => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_session != null && _session.OutputIndexes.Contains(rec.Index)) return;   // メインの録音に含まれている間は、グループ側が記録する
            _settings.AddHistory(path);
            Files.Refresh();
        }));
    }

    /// <summary>メインの録音中に「録音に含める」をオフにしたとき、その出力の録音をメインから切り離して単独録音にする（M14）。</summary>
    private void DetachOutputRec(int index)
    {
        if (_session == null || index < 0 || index >= _outHub.Count) return;
        var rec = _session.Release(index);
        if (rec == null) return;
        var engine = _outHub[index];
        WireStandalone(engine, rec);
        _outRecs[engine] = rec;
        UpdateRecordingRows();
        OutLamps.Refresh(this, _session != null);
    }

    private async Task StopOutputRecAsync(OutputEngine engine, OutputRecording rec, string? error)
    {
        if (!_outRecs.Remove(engine)) return;
        OutLamps.Refresh(this, _session != null);
        string? failure = error;
        await Task.Run(() =>
        {
            try { rec.Stop(); } catch (Exception ex) { failure ??= ex.Message; }
        });
        failure ??= rec.Session.Failure;
        var results = rec.Session.Results;
        var seconds = rec.Session.Seconds;
        var name = rec.Name;
        rec.Dispose();
        if (_session == null && !_closeAfterStop)   // メインの録音中は、その表示を邪魔しない
            StatusText.Text = failure != null
                ? Loc.Get("outputrec.err.failed", name, failure)
                : Loc.Get("outputrec.saved", name) + "\n" + SavedText(results, seconds);
        else if (failure != null) ShowError(Loc.Get("outputrec.err.failed", name, failure));
        Files.Refresh();
    }

    /// <summary>終了時・出力カードの削除時に、単独録音をすべて止める（別スレッドで止めて待つ）。</summary>
    private void StopAllOutputRecsSync()
    {
        var list = _outRecs.ToList();
        _outRecs.Clear();
        if (list.Count == 0) return;
        try
        {
            Task.WaitAll(list.Select(x => Task.Run(() =>
            {
                try { x.Value.Stop(); } catch (Exception) { }
                foreach (var r in x.Value.Session.Results) _settings.AddHistory(r.Path);
                x.Value.Dispose();
            })).ToArray(), 15000);
        }
        catch (Exception) { }
    }
}
