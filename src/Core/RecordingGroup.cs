namespace Recorder.Core;

/// <summary>
/// 1回の録音の全体（PROJECT.md §3-1）。
/// ・ミックス（既定）: 選んだ全デバイスを混ぜて1本のファイルへ（RecordingSession が1つ）。
/// ・デバイスごとに別ファイル: デバイスごとに RecordingSession を1つずつ。分割・一時停止・停止はすべて同時に行う。
/// WPF や Windows API には依存しない（§6-3）。
/// </summary>
public sealed class RecordingGroup : IDisposable
{
    private readonly List<RecordingSession> _sessions;
    private readonly List<MixedSource> _sources;
    private readonly List<OutputRecording> _outs;   // メインの録音と一緒に録る出力（M13）。メーター・波形・失敗の判定には含めない
    private readonly bool _separate;
    private readonly bool _waitForDevice;     // M7: すべてのデバイスが外れたら、戻るまで一時停止する
    private readonly bool _splitOnReturn;     // M7: デバイスが戻ったら別ファイルに分割する
    private readonly object _waitLock = new();
    private bool _autoPaused;                 // 外れたために自動で一時停止している（手動の一時停止は戻っても再開しない）

    /// <summary>入力側・エンコード側のどちらかが失敗したとき（別スレッドから）。</summary>
    public event Action<string>? Error;

    /// <summary>録音は続くが知らせたいこと（分割で保存した・デバイスの切断と復帰など。別スレッドから）。</summary>
    public event Action<string>? Notice;

    /// <summary>ファイルが保存された（別スレッドから）。録音履歴へ入れるために使う。</summary>
    public event Action<string>? Saved;

    /// <summary>一緒に録る指定だったが、出力が動いていなくて録れなかった出力の名前（画面で知らせる）。</summary>
    public IReadOnlyList<string> SkippedOutputs { get; private set; } = Array.Empty<string>();

    private RecordingGroup(List<RecordingSession> sessions, List<MixedSource> sources, List<OutputRecording> outs, bool separate,
        bool waitForDevice, bool splitOnReturn)
    {
        _sessions = sessions;
        _sources = sources;
        _outs = outs;
        _separate = separate;
        _waitForDevice = waitForDevice;
        _splitOnReturn = splitOnReturn;
        foreach (var x in sessions)
        {
            x.Error += m => Error?.Invoke(m);
            x.Notice += m => Notice?.Invoke(m);
            x.Saved += p => Saved?.Invoke(p);
        }
        foreach (var o in outs)
        {
            // 出力の録音の失敗は、入力の録音を止めずに知らせるだけにする
            // （Release で切り離したあとは、この録音の通知を受けない＝_outs に居るときだけ通す）
            o.Error += m => { if (_outs.Contains(o)) Notice?.Invoke(Loc.Get("outputrec.failed", o.Name, m)); };
            o.Notice += m => { if (_outs.Contains(o)) Notice?.Invoke(Loc.Get("outputrec.notice", o.Name, m)); };
            o.Saved += p => { if (_outs.Contains(o)) Saved?.Invoke(p); };
        }
        foreach (var x in sources)
        {
            x.Notice += m => Notice?.Invoke(m);
            x.DeviceLost += OnDeviceLost;
            x.DeviceReturned += OnDeviceReturned;
        }
    }

    /// <summary>デバイスが外れた（別スレッドから）。「戻るまで待機」がONで、全デバイスが外れたら一時停止する。</summary>
    private void OnDeviceLost()
    {
        if (!_waitForDevice) return;
        lock (_waitLock)
        {
            if (_autoPaused || Paused) return;
            if (_sources.Any(x => x.ConnectedCount > 0)) return;   // まだ録れているデバイスがあれば続ける
            _autoPaused = true;
            Pause();
        }
        Notice?.Invoke(Loc.Get("recordinggroup.s01"));
    }

    /// <summary>デバイスが戻った（別スレッドから）。自動の一時停止なら再開し、「戻ったら別ファイルに分割」がONなら分割する。</summary>
    private void OnDeviceReturned()
    {
        bool resumed = false;
        lock (_waitLock)
        {
            if (_autoPaused)
            {
                _autoPaused = false;
                Resume();
                resumed = true;
            }
        }
        if (resumed) Notice?.Invoke(Loc.Get("recordinggroup.s02"));
        if (_splitOnReturn)
        {
            Task.Run(() =>
            {
                try { Split(); }
                catch (Exception) { }
            });
        }
    }

    /// <summary>画面で選ばれているデバイス（inputs）と設定から録音を組み立てる（まだ開始しない）。保存先フォルダも作る。</summary>
    public static RecordingGroup Create(InputHub hub, IAudioDeviceProvider provider, Settings s, IReadOnlyList<InputSetting> inputs,
        IReadOnlyList<AudioDeviceInfo> devices, string ffmpegPath, DateTime now,
        IReadOnlyList<(OutputEngine Engine, string Name, int Index)>? outputs = null)
    {
        if (inputs.Count == 0) throw new InvalidOperationException(Loc.Get("recordinggroup.s03"));
        // 「既定のデバイス」は、いま実際に使われるデバイスで重複を判定する（既定＋実際の同じデバイスだと音が二重になるため）
        string RealKey(string key)
        {
            try { return provider.Resolve(key) ?? key; }
            catch (Exception) { return key; }
        }
        if (inputs.Select(i => RealKey(i.Key)).Distinct().Count() != inputs.Count)
            throw new InvalidOperationException(Loc.Get("recordinggroup.s04"));
        if (inputs.Count(i => AsioKey.Is(i.Key)) > 1)
            throw new InvalidOperationException(Loc.Get("recordinggroup.s05"));

        bool separate = s.SeparateFiles && inputs.Count > 1;
        // ファイル名の {Device} には、既定のデバイスなら「いま実際のデバイス名」を使う（ファイルを始めた時点の名前）
        string LabelOf(string key) => devices.FirstOrDefault(d => d.Key == RealKey(key))?.Label
                                      ?? devices.FirstOrDefault(d => d.Key == key)?.Label ?? key;
        var lanes = separate
            ? inputs.Select(i => new List<InputSetting> { i }).ToList()
            : new List<List<InputSetting>> { inputs.ToList() };
        var split = TimeSpan.FromSeconds(s.SplitSeconds);

        var sessions = new List<RecordingSession>();
        var sources = new List<MixedSource>();
        var outs = new List<OutputRecording>();
        var skipped = new List<string>();
        try
        {
            for (int idx = 0; idx < lanes.Count; idx++)
            {
                var lane = lanes[idx];
                var mixInputs = lane.Select(i => new MixInput(i.Key, LabelOf(i.Key), i.GainDb)).ToList();
                var src = new MixedSource(hub, mixInputs, devices);
                src.SetMasterGain(s.MasterGainDb);
                sources.Add(src);
                var label = lane.Count == 1 ? LabelOf(lane[0].Key) : Loc.Get("recordinggroup.s06");
                var plan = RecordingPlan.Create(s, label, now, separate ? idx : null, separate);
                if (idx == 0) Directory.CreateDirectory(plan.Folder);
                sessions.Add(new RecordingSession(src, ffmpegPath, plan, split));
            }
            // 一緒に録る出力（動いていない出力は録れないので飛ばして知らせる）
            foreach (var (engine, name, index) in outputs ?? Array.Empty<(OutputEngine, string, int)>())
            {
                var rec = OutputRecording.Create(engine, name, index, s, ffmpegPath, now);
                if (rec == null) skipped.Add(name);
                else outs.Add(rec);
            }
        }
        catch
        {
            foreach (var x in sessions) x.Dispose();
            foreach (var x in sources) x.Dispose();
            foreach (var x in outs) x.Dispose();
            throw;
        }
        return new RecordingGroup(sessions, sources, outs, separate, s.WaitForDevice, s.SplitOnReturn) { SkippedOutputs = skipped };
    }

    private RecordingSession First => _sessions[0];

    /// <summary>デバイスごとに別ファイルにしているか。</summary>
    public bool Separate => _separate;

    public TimeSpan Elapsed => First.Elapsed;
    public TimeSpan TrackElapsed => First.TrackElapsed;
    public int SplitCount => First.SplitCount;
    public int CurrentTrackNumber => First.CurrentTrackNumber;
    public bool Paused => First.Paused;

    /// <summary>いま書き込んでいるファイル（仮名）。別ファイルのときは1行ずつ。</summary>
    public string OutputText => string.Join("\n", _sessions.Select(x => x.OutputPath));

    /// <summary>前回取得以降の最大ピーク（ミックス後。別ファイルのときは全ファイルの最大）。</summary>
    public float TakePeak() => _sessions.Max(x => x.TakePeak());

    /// <summary>前回取得以降の出力のL/Rのピーク（別ファイルのときは全ファイルの最大）。</summary>
    public (float L, float R) TakeChannelPeaks()
    {
        float l = 0, r = 0;
        foreach (var x in _sessions)
        {
            var (a, b) = x.TakeChannelPeaks();
            if (a > l) l = a;
            if (b > r) r = b;
        }
        return (l, r);
    }

    /// <summary>全体波形用の区間（0.04秒ごとの最小値・最大値）。別ファイルのときは先頭のファイルの音（残りは捨てる）。</summary>
    public (float Min, float Max)[] TakeWave()
    {
        var first = _sessions[0].TakeWave();
        for (int i = 1; i < _sessions.Count; i++) _sessions[i].TakeWave();
        return first;
    }

    /// <summary>録音中に全体ゲインを変える。</summary>
    public void SetMasterGain(double gainDb)
    {
        foreach (var x in _sources) x.SetMasterGain(gainDb);
    }

    /// <summary>デバイスごと（選んだ順）の、ゲインを掛けたあとのピーク。</summary>
    public float[] TakeDevicePeaks() => _sources.SelectMany(x => x.TakeDevicePeaks()).ToArray();

    /// <summary>デバイスごと（選んだ順）の、左右のピーク。</summary>
    public (float L, float R)[] TakeDevicePeakPairs() => _sources.SelectMany(x => x.TakeDevicePeakPairs()).ToArray();

    /// <summary>録音中にゲインを変える（index は選んだ順）。</summary>
    public void SetGain(int index, double gainDb)
    {
        if (_separate)
        {
            if (index >= 0 && index < _sources.Count) _sources[index].SetGain(0, gainDb);
        }
        else
        {
            _sources[0].SetGain(index, gainDb);
        }
    }

    public void Start()
    {
        int started = 0;
        try
        {
            foreach (var x in _sessions)
            {
                x.Start();
                started++;
            }
        }
        catch
        {
            for (int i = 0; i < started; i++)
            {
                try { _sessions[i].Stop(); } catch (Exception) { }
            }
            throw;
        }
        // 出力の録音は、始められなくても入力の録音は続ける
        foreach (var o in _outs.ToList())
        {
            try { o.Start(); }
            catch (Exception ex)
            {
                _outs.Remove(o);
                try { o.Dispose(); } catch (Exception) { }
                Notice?.Invoke(Loc.Get("outputrec.failed", o.Name, ex.Message));
            }
        }
    }

    /// <summary>すでに単独で録音中の出力を、このグループへ引き継ぐ（以後、一時停止・分割・停止はメインに従う）。保存の記録は呼び出し側が持つ。</summary>
    public void Adopt(OutputRecording rec)
    {
        rec.Error += m => { if (_outs.Contains(rec)) Notice?.Invoke(Loc.Get("outputrec.failed", rec.Name, m)); };
        rec.Notice += m => { if (_outs.Contains(rec)) Notice?.Invoke(Loc.Get("outputrec.notice", rec.Name, m)); };
        _outs.Add(rec);
    }

    /// <summary>このグループで録っている出力（出力カードの番号 index）を切り離して返す（なければ null）。以後、その録音は単独録音として続き、
    /// メインの一時停止・分割・停止には従わない（ファイルはそのまま続く）。「録音に含める」をオフにしたときに使う（M14）。</summary>
    public OutputRecording? Release(int index)
    {
        var rec = _outs.FirstOrDefault(o => o.Index == index);
        if (rec != null) _outs.Remove(rec);
        return rec;
    }

    /// <summary>録音中のファイルごと（入力の別ファイル・一緒に録る出力を含む）の、一時停止中かどうかと現在のトラック番号。</summary>
    public IReadOnlyList<(bool Paused, int Track)> States =>
        _sessions.Concat(_outs.Select(o => o.Session)).Select(x => (x.Paused, x.CurrentTrackNumber)).ToList();

    /// <summary>メインの録音と一緒に録っている出力の数。</summary>
    public int OutputCount => _outs.Count;

    /// <summary>メインの録音と一緒に録っている出力の番号（0始まり。出力カードの順）。</summary>
    public IReadOnlyList<int> OutputIndexes => _outs.Select(o => o.Index).ToList();

    public void Pause()
    {
        foreach (var x in _sessions) x.Pause();
        foreach (var o in _outs) o.Session.Pause();
    }

    public void Resume()
    {
        foreach (var x in _sessions) x.Resume();
        foreach (var o in _outs) o.Session.Resume();
    }

    /// <summary>全ファイルを同時に分割する。どれかが分割できれば true。Task.Run 経由で呼ぶこと。</summary>
    public bool Split()
    {
        var all = _sessions.Concat(_outs.Select(o => o.Session)).ToList();
        if (all.Count == 1) return all[0].Split();
        var ok = new bool[all.Count];
        Parallel.For(0, all.Count, i => ok[i] = all[i].Split());
        return ok.Any(b => b);
    }

    /// <summary>止めて、すべてのファイルの名前を確定する。Task.Run 経由で呼ぶこと。</summary>
    public void Stop()
    {
        // 出力の録音の失敗は例外にしない（入力の録音の結果を優先して返す）
        var outTasks = _outs.Select(o => Task.Run(() => { try { o.Stop(); } catch (Exception ex) { Notice?.Invoke(Loc.Get("outputrec.failed", o.Name, ex.Message)); } })).ToArray();
        try
        {
            if (_sessions.Count == 1) _sessions[0].Stop();
            else
            {
                var tasks = _sessions.Select(x => Task.Run(x.Stop)).ToArray();
                try { Task.WaitAll(tasks); }
                catch (AggregateException ex) { throw ex.InnerException ?? ex; }
            }
        }
        finally { Task.WaitAll(outTasks); }
    }

    /// <summary>名前を確定できたファイル（デバイス順、各デバイスの中はトラック順）。</summary>
    public IReadOnlyList<FinalizeResult> Results => _sessions.SelectMany(x => x.Results).Concat(_outs.SelectMany(o => o.Session.Results)).ToList();

    /// <summary>Results と同じ順の、実際に書いた音声の長さ（秒）。</summary>
    public IReadOnlyList<double> Seconds => _sessions.SelectMany(x => x.Seconds).Concat(_outs.SelectMany(o => o.Session.Seconds)).ToList();

    /// <summary>エンコード側の失敗理由（正常なら null）。</summary>
    public string? Failure => _sessions.Select(x => x.Failure).FirstOrDefault(f => f != null);

    public void Dispose()
    {
        foreach (var x in _sessions) x.Dispose();
        foreach (var o in _outs) o.Dispose();
    }
}
