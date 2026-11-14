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
    private readonly Fader _fader;
    private readonly object _pauseLock = new();
    private long _pauseAt;
    private readonly Stopwatch _clock = new();
    private readonly object _sinkLock = new();    // 書き込み先の切り替えと書き込みを直列にする
    private readonly object _splitLock = new();   // 分割と停止を直列にする
    private readonly List<Track> _tracks = new();
    private readonly List<Task> _closing = new();
    private Track _current = null!;
    private System.Threading.Timer? _idleTimer;
    private float _peak;
    private readonly WaveColumns _wave = new();
    private readonly float[] _chPeaks = new float[2];   // 出力のL/Rのピーク（メーター用）
    private volatile bool _stopped;
    private volatile bool _paused;    // 音を捨てている（フェードアウト完了後）
    private volatile bool _pausing;   // 一時停止が要求されている

    /// <summary>いま書き込んでいるファイル（仮名）。最終名への確定は FinishTrack。</summary>
    public string OutputPath => _current.TempPath;
    public AudioFormatInfo Format => _source.Format;

    /// <summary>録音全体の経過時間。一時停止中は進まない。</summary>
    public TimeSpan Elapsed => _clock.Elapsed;

    public bool Paused => _pausing;

    /// <summary>いまのファイルを始めてからの経過時間（分割するとリセット）。一時停止中は進まない。</summary>
    public TimeSpan TrackElapsed
    {
        get
        {
            var t = _clock.Elapsed - _splitter.TrackStart;
            return t < TimeSpan.Zero ? TimeSpan.Zero : t;
        }
    }

    /// <summary>分割した回数（0 = まだ分割していない）。</summary>
    public int SplitCount => _current.Index;

    /// <summary>いま録音しているトラックの {Track} 番号。</summary>
    public int CurrentTrackNumber => _plan.TrackNumber(_current.Index);

    /// <summary>入力側・現在のエンコード側のどちらかが失敗したとき呼ばれる（別スレッドから）。</summary>
    public event Action<string>? Error;

    /// <summary>録音は続くが知らせたいこと（分割で保存した・前のファイルの書き出し失敗など。別スレッドから）。</summary>
    public event Action<string>? Notice;

    /// <summary>ファイルを閉じて最終名が決まったとき（別スレッドから。分割・停止のどちらでも）。引数は最終のパス。</summary>
    public event Action<string>? Saved;

    public RecordingSession(IAudioSource source, string ffmpegPath, RecordingPlan plan, TimeSpan splitInterval)
    {
        _source = source;
        _ffmpeg = ffmpegPath;
        _plan = plan;
        _splitter = new Splitter(splitInterval);
        _fader = new Fader(source.Format);
        // 音源（MixedSource）が各デバイスを壁時計に合わせて無音補填するので、ここでは大きな欠落だけを補う
        _filler = new SilenceFiller(source.Format, () => _clock.Elapsed, Sink, toleranceSec: 1.0, idleMarginSec: 1.5);
        _source.DataAvailable += OnData;
        _source.Stopped += OnSourceStopped;
    }

    private void Sink(byte[] data)
    {
        lock (_sinkLock)
        {
            _current.Bytes += data.Length;
            _current.Encoder.Write(data);
        }
    }

    private Track NewTrack(int index, TimeSpan start, DateTime time)
    {
        var path = _plan.TempPathFor(index, time);
        var encoder = new FfmpegEncoder(_ffmpeg, path, _plan.OptionsFor(index), _source.Format);
        var track = new Track(index, path, encoder, start, time);
        encoder.Failed += msg =>
        {
            if (track == _current) Error?.Invoke(msg);
            else Notice?.Invoke((Loc.Get("recordingsession.s01") + " ") + msg);
        };
        return track;
    }

    public void Start()
    {
        var first = NewTrack(0, TimeSpan.Zero, _plan.StartTime);
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
        // 一時停止・再開のつなぎ目は短いフェードでプツッという音を防ぐ
        count = _fader.Apply(buf, count, out var faded);
        if (faded)
            lock (_pauseLock) { if (_pausing) _paused = true; }
        if (count <= 0) return;
        var p = PeakMeter.Compute(buf, count, _source.Format);
        if (p > _peak) _peak = p;
        PeakMeter.Channels(buf, count, _source.Format, _chPeaks);
        _wave.Feed(buf, count, _source.Format);
        _filler.Feed(buf, count);
    }

    private void OnSourceStopped(Exception? ex)
    {
        if (ex != null && !_stopped) Error?.Invoke((Loc.Get("recordingsession.s02") + " ") + ex.Message);
    }

    /// <summary>100msごと。データが来ない間の無音補填と、時間による自動分割。</summary>
    private void Tick()
    {
        if (_stopped || _paused) return;
        if (_pausing)
        {
            // データが来ない間（ループバックの無音）はフェードが終わらないので、少し待って止める
            if (Stopwatch.GetElapsedTime(_pauseAt).TotalMilliseconds > 100)
                lock (_pauseLock) { if (_pausing) _paused = true; }
            return;
        }
        try
        {
            _filler.FillIdle();
            var now = _clock.Elapsed;
            if (_pending == null && _splitter.Due(now + TimeSpan.FromSeconds(1))) PrepareAuto(now);
        }
        catch (Exception ex)
        {
            Notice?.Invoke((Loc.Get("recordingsession.s03") + " ") + ex.Message);
        }
    }

    /// <summary>前回取得以降のピークを返してリセットする。メーター表示用。</summary>
    public float TakePeak()
    {
        var p = _peak;
        _peak = 0;
        return p;
    }

    /// <summary>前回取得以降の、全体波形用の区間（0.04秒ごとの最小値・最大値）を返す。</summary>
    public (float Min, float Max)[] TakeWave() => _wave.Take();

    /// <summary>前回取得以降の出力のL/Rのピークを返してリセットする（モノはLと同じ）。メーター表示用。</summary>
    public (float L, float R) TakeChannelPeaks()
    {
        float l = _chPeaks[0], r = _chPeaks.Length > 1 && _source.Format.Channels > 1 ? _chPeaks[1] : l;
        _chPeaks[0] = _chPeaks[1] = 0;
        return (l, r);
    }

    // ---- 一時停止／再開 ----
    public void Pause()
    {
        if (_stopped) return;
        lock (_pauseLock)
        {
            if (_pausing) return;
            _pauseAt = Stopwatch.GetTimestamp();
            _fader.StartOut();
            _pausing = true;
            _clock.Stop();
        }
    }

    public void Resume()
    {
        if (_stopped) return;
        lock (_pauseLock)
        {
            if (!_pausing) return;
            _fader.StartIn();
            _clock.Start();
            _paused = false;
            _pausing = false;
        }
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

    private double SecondsOf(Track t)
    {
        var f = _source.Format;
        return t.Bytes / (double)Math.Max(1, f.SampleRate * f.Channels * f.BitsPerSample / 8);
    }

    /// <summary>Results と同じ順の、実際に書いた音声の長さ（秒）。</summary>
    public IReadOnlyList<double> Seconds
    {
        get
        {
            var f = _source.Format;
            double perSec = Math.Max(1, f.SampleRate * f.Channels * f.BitsPerSample / 8);
            lock (_splitLock) return _tracks.Where(t => t.Result != null).Select(t => t.Bytes / perSec).ToList();
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
        lock (_splitLock) CancelPending();   // 切り替え前の予約が残っていれば捨てる

        Track last;
        lock (_sinkLock) last = _current;
        last.End = Splitter.Floor(_clock.Elapsed);
        Close(last, announce: false);

        Task[] pending;
        lock (_closing) pending = _closing.ToArray();
        Task.WaitAll(pending);

        // 取り消した準備ファイルで消し損ねたものを、最後にもう一度消す
        string[] left;
        lock (_splitLock) left = _abandoned.ToArray();
        foreach (var path in left) TryDelete(path, 10);
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
