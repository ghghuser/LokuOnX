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

    /// <summary>入力側・エンコード側のどちらかが失敗したとき（別スレッドから）。</summary>
    public event Action<string>? Error;

    /// <summary>録音は続くが知らせたいこと（分割で保存した・デバイスの切断と復帰など。別スレッドから）。</summary>
    public event Action<string>? Notice;

    private RecordingGroup(List<RecordingSession> sessions, List<MixedSource> sources, bool separate)
    {
        _sessions = sessions;
        _sources = sources;
        _separate = separate;
        foreach (var x in sessions)
        {
            x.Error += m => Error?.Invoke(m);
            x.Notice += m => Notice?.Invoke(m);
        }
        foreach (var x in sources) x.Notice += m => Notice?.Invoke(m);
    }

    /// <summary>画面で選ばれているデバイス（inputs）と設定から録音を組み立てる（まだ開始しない）。保存先フォルダも作る。</summary>
    public static RecordingGroup Create(IAudioDeviceProvider provider, Settings s, IReadOnlyList<InputSetting> inputs,
        IReadOnlyList<AudioDeviceInfo> devices, string ffmpegPath, DateTime now)
    {
        if (inputs.Count == 0) throw new InvalidOperationException("入力デバイスを選んでください。");
        if (inputs.Select(i => i.Key).Distinct().Count() != inputs.Count)
            throw new InvalidOperationException("同じデバイスが重複して選ばれています。");

        bool separate = s.SeparateFiles && inputs.Count > 1;
        string LabelOf(string key) => devices.FirstOrDefault(d => d.Key == key)?.Label ?? key;
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
                var src = new MixedSource(provider, mixInputs, devices, s.AutoResume);
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
        return new RecordingGroup(sessions, sources, separate);
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

    /// <summary>デバイスごと（選んだ順）の、ゲインを掛けたあとのピーク。</summary>
    public float[] TakeDevicePeaks() => _sources.SelectMany(x => x.TakeDevicePeaks()).ToArray();

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
