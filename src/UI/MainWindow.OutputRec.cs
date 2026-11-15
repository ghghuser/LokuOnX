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

    bool IOutputHost.OutputRec(int index) => _settings.OutputRec(index);

    void IOutputHost.SetOutputRec(int index, bool on) => _settings.SetOutputRec(index, on);

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
        for (int i = 0; i < _outCfgs.Count; i++) _settings.SetOutputRec(i, on);
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
            rec.Error += msg => Dispatcher.BeginInvoke(new Action(() => _ = StopOutputRecAsync(engine, rec!, msg)));
            rec.Saved += path => Dispatcher.BeginInvoke(new Action(() =>
            {
                _settings.AddHistory(path);
                Files.Refresh();
            }));
            rec.Start();
            _outRecs[engine] = rec;
            if (!_settings.ReadOnly) _settings.NextNo = no + 1;
            Detail.Reload();   // {No} が進んだので画面へ反映
        }
        catch (Exception ex)
        {
            rec?.Dispose();
            ShowError(Loc.Get("outputrec.err.cannotStart", name, ex.Message));
        }
    }

    private async Task StopOutputRecAsync(OutputEngine engine, OutputRecording rec, string? error)
    {
        if (!_outRecs.Remove(engine)) return;
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
