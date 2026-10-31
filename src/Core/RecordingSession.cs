using System.Diagnostics;

namespace Recorder.Core;

/// <summary>
/// 1回の録音。音声入力(IAudioSource)から受けたPCMを、無音補填(SilenceFiller)を通して FfmpegEncoder へ流す。
/// 分割（手動・時間・24時間強制）では、次のFFmpegを先に起動してから出力先を切り替えるので途切れない（PROJECT.md §5-4）。
/// 一時停止中は音も時間もファイルに入れない（§3-1）。
/// </summary>
public sealed partial class RecordingSession : IDisposable
{
    private readonly IAudioSource _source;
    private readonly string _ffmpeg;
    private readonly RecordingPlan _plan;
    private readonly SilenceFiller _filler;
    private readonly Splitter _splitter;
    private readonly Stopwatch _clock = new();
    private readonly object _sinkLock = new();    // 書き込み先の切り替えと書き込みを直列にする
    private readonly object _splitLock = new();   // 分割と停止を直列にする
    private readonly List<Track> _tracks = new();
    private readonly List<Task> _closing = new();
    private Track _current = null!;
    private System.Threading.Timer? _idleTimer;
    private float _peak;
    private volatile bool _stopped;
    private volatile bool _paused;

    /// <summary>いま書き込んでいるファイル（仮名）。最終名への確定は FinishTrack。</summary>
    public string OutputPath => _current.TempPath;
    public AudioFormatInfo Format => _source.Format;

    /// <summary>録音全体の経過時間。一時停止中は進まない。</summary>
    public TimeSpan Elapsed => _clock.Elapsed;

    public bool Paused => _paused;

    /// <summary>いま録音しているトラックの {Track} 番号。</summary>
    public int CurrentTrackNumber => _plan.TrackNumber(_current.Index);

    /// <summary>入力側・現在のエンコード側のどちらかが失敗したとき呼ばれる（別スレッドから）。</summary>
    public event Action<string>? Error;

    /// <summary>録音は続くが知らせたいこと（分割で保存した・前のファイルの書き出し失敗など。別スレッドから）。</summary>
    public event Action<string>? Notice;

    public RecordingSession(IAudioSource source, string ffmpegPath, RecordingPlan plan, TimeSpan splitInterval)
    {
        _source = source;
        _ffmpeg = ffmpegPath;
        _plan = plan;
        _splitter = new Splitter(splitInterval);
        _filler = new SilenceFiller(source.Format, () => _clock.Elapsed, Sink);
        _source.DataAvailable += OnData;
        _source.Stopped += OnSourceStopped;
    }

    private void Sink(byte[] data)
    {
        lock (_sinkLock) _current.Encoder.Write(data);
    }

    private Track NewTrack(int index, TimeSpan start)
    {
        var path = _plan.TempPathFor(index);
        var encoder = new FfmpegEncoder(_ffmpeg, path, _plan.Options, _source.Format);
        var track = new Track(index, path, encoder, start);
        encoder.Failed += msg =>
        {
            if (track == _current) Error?.Invoke(msg);
            else Notice?.Invoke("前のファイルの書き出しでエラーが起きました: " + msg);
        };
        return track;
    }

    public void Start()
    {
        var first = NewTrack(0, TimeSpan.Zero);
        _tracks.Add(first);
        _current = first;
        first.Encoder.Start();
        _clock.Start();
        _source.Start();
        _idleTimer = new System.Threading.Timer(_ => Tick(), null, 100, 100);
    }

    private void OnData(byte[] buf, int count)
    {
        if (_paused) return;
        var p = PeakMeter.Compute(buf, count, _source.Format);
        if (p > _peak) _peak = p;
        _filler.Feed(buf, count);
    }

    private void OnSourceStopped(Exception? ex)
    {
        if (ex != null && !_stopped) Error?.Invoke("音声入力が停止しました: " + ex.Message);
    }

    /// <summary>100msごと。データが来ない間の無音補填と、時間による自動分割。</summary>
    private void Tick()
    {
        if (_stopped || _paused) return;
        try
        {
            _filler.FillIdle();
            if (_splitter.Due(_clock.Elapsed)) RollOver(manual: false);
        }
        catch (Exception ex)
        {
            Notice?.Invoke("内部エラー: " + ex.Message);
        }
    }

    /// <summary>前回取得以降のピークを返してリセットする。メーター表示用。</summary>
    public float TakePeak()
    {
        var p = _peak;
        _peak = 0;
        return p;
    }

    // ---- 一時停止／再開 ----
    public void Pause()
    {
        if (_paused || _stopped) return;
        _paused = true;
        _clock.Stop();
    }

    public void Resume()
    {
        if (!_paused || _stopped) return;
        _clock.Start();
        _paused = false;
    }

    // ---- 結果 ----
    /// <summary>名前を確定できたファイル（トラック順）。</summary>
    public IReadOnlyList<FinalizeResult> Results
    {
        get
        {
            lock (_splitLock) return _tracks.Where(t => t.Result != null).Select(t => t.Result!).ToList();
        }
    }

    /// <summary>エンコード側の失敗理由（正常なら null）。</summary>
    public string? Failure
    {
        get
        {
            lock (_splitLock)
                return _tracks.Select(t => t.Failure ?? t.Encoder.Failure).FirstOrDefault(f => f != null);
        }
    }

    /// <summary>録音を止めてファイルを閉じ、すべてのファイルの名前を確定する。UIスレッドで直接呼ばず Task.Run 経由で呼ぶこと。</summary>
    public void Stop()
    {
        lock (_splitLock)
        {
            if (_stopped) return;
            _stopped = true;
        }
        _clock.Stop();
        StopIdleTimer();
        _source.Stop();
        _filler.Finish();   // 停止時刻ちょうどまで無音で埋める

        Track last;
        lock (_sinkLock) last = _current;
        last.End = Splitter.Floor(_clock.Elapsed);
        Close(last, announce: false);

        Task[] pending;
        lock (_closing) pending = _closing.ToArray();
        Task.WaitAll(pending);
    }

    private void StopIdleTimer()
    {
        var t = _idleTimer;
        _idleTimer = null;
        if (t == null) return;
        using var done = new ManualResetEvent(false);
        t.Dispose(done);
        done.WaitOne(2000);
    }

    public void Dispose()
    {
        _source.DataAvailable -= OnData;
        _source.Stopped -= OnSourceStopped;
        _idleTimer?.Dispose();
        _source.Dispose();
        lock (_splitLock)
            foreach (var t in _tracks) t.Encoder.Dispose();
    }
}
