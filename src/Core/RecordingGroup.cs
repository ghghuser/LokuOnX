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

    private RecordingGroup(List<RecordingSession> sessions, List<MixedSource> sources, bool separate,
        bool waitForDevice, bool splitOnReturn)
    {
        _sessions = sessions;
        _sources = sources;
        _separate = separate;
        _waitForDevice = waitForDevice;
        _splitOnReturn = splitOnReturn;
        foreach (var x in sessions)
        {
            x.Error += m => Error?.Invoke(m);
            x.Notice += m => Notice?.Invoke(m);
            x.Saved += p => Saved?.Invoke(p);
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
        Notice?.Invoke("デバイスが外れたため一時停止しました。戻ったら自動で再開します。");
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
        if (resumed) Notice?.Invoke("デバイスが戻ったので録音を再開しました。");
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
        IReadOnlyList<AudioDeviceInfo> devices, string ffmpegPath, DateTime now)
    {
        if (inputs.Count == 0) throw new InvalidOperationException("入力デバイスを選んでください。");
        // 「既定のデバイス」は、いま実際に使われるデバイスで重複を判定する（既定＋実際の同じデバイスだと音が二重になるため）
        string RealKey(string key)
        {
            try { return provider.Resolve(key) ?? key; }
            catch (Exception) { return key; }
        }
        if (inputs.Select(i => RealKey(i.Key)).Distinct().Count() != inputs.Count)
            throw new InvalidOperationException("同じデバイスが重複して選ばれています（「既定のデバイス」と、その実際のデバイスの組み合わせも重複です）。");
        if (inputs.Count(i => AsioKey.Is(i.Key)) > 1)
            throw new InvalidOperationException("ASIOは同時に1行しか使えません（ドライバを専有するため）。");

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
        try
        {
            for (int idx = 0; idx < lanes.Count; idx++)
            {
                var lane = lanes[idx];
                var mixInputs = lane.Select(i => new MixInput(i.Key, LabelOf(i.Key), i.GainDb)).ToList();
                var src = new MixedSource(hub, mixInputs, devices);
                src.SetMasterGain(s.MasterGainDb);
                sources.Add(src);
                var label = lane.Count == 1 ? LabelOf(lane[0].Key) : "ミックス";
                var plan = RecordingPlan.Create(s, label, now, separate ? idx : null, separate);
                if (idx == 0) Directory.CreateDirectory(plan.Folder);
                sessions.Add(new RecordingSession(src, ffmpegPath, plan, split));
            }
        }
        catch
        {
            foreach (var x in sessions) x.Dispose();
            foreach (var x in sources) x.Dispose();
            throw;
        }
        return new RecordingGroup(sessions, sources, separate, s.WaitForDevice, s.SplitOnReturn);
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
    }

    public void Pause()
    {
        foreach (var x in _sessions) x.Pause();
    }

    public void Resume()
    {
        foreach (var x in _sessions) x.Resume();
    }

    /// <summary>全ファイルを同時に分割する。どれかが分割できれば true。Task.Run 経由で呼ぶこと。</summary>
    public bool Split()
    {
        if (_sessions.Count == 1) return _sessions[0].Split();
        var ok = new bool[_sessions.Count];
        Parallel.For(0, _sessions.Count, i => ok[i] = _sessions[i].Split());
        return ok.Any(b => b);
    }

    /// <summary>止めて、すべてのファイルの名前を確定する。Task.Run 経由で呼ぶこと。</summary>
    public void Stop()
    {
        if (_sessions.Count == 1)
        {
            _sessions[0].Stop();
            return;
        }
        var tasks = _sessions.Select(x => Task.Run(x.Stop)).ToArray();
        try { Task.WaitAll(tasks); }
        catch (AggregateException ex) { throw ex.InnerException ?? ex; }
    }

    /// <summary>名前を確定できたファイル（デバイス順、各デバイスの中はトラック順）。</summary>
    public IReadOnlyList<FinalizeResult> Results => _sessions.SelectMany(x => x.Results).ToList();

    /// <summary>Results と同じ順の、実際に書いた音声の長さ（秒）。</summary>
    public IReadOnlyList<double> Seconds => _sessions.SelectMany(x => x.Seconds).ToList();

    /// <summary>エンコード側の失敗理由（正常なら null）。</summary>
    public string? Failure => _sessions.Select(x => x.Failure).FirstOrDefault(f => f != null);

    public void Dispose()
    {
        foreach (var x in _sessions) x.Dispose();
    }
}
