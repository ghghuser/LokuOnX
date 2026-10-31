using System.Diagnostics;

namespace Recorder.Core;

/// <summary>
/// 1回の録音。音声入力(IAudioSource)から受けたPCMを、無音補填(SilenceFiller)を通して FfmpegEncoder へ流す。
/// ピークと経過時間を保持する。
/// </summary>
public sealed class RecordingSession : IDisposable
{
    private readonly IAudioSource _source;
    private readonly FfmpegEncoder _encoder;
    private readonly SilenceFiller _filler;
    private readonly Stopwatch _clock = new();
    private System.Threading.Timer? _idleTimer;
    private float _peak;
    private bool _stopped;

    /// <summary>録音中に書き込んでいるファイル（仮名）。最終名への確定は RecordingPlan.Finish。</summary>
    public string OutputPath { get; }
    public AudioFormatInfo Format => _source.Format;
    public TimeSpan Elapsed => _clock.Elapsed;

    /// <summary>エンコード側の失敗理由（正常なら null）。</summary>
    public string? Failure => _encoder.Failure;

    /// <summary>入力側・エンコード側のどちらかが失敗したとき呼ばれる（別スレッドから）。</summary>
    public event Action<string>? Error;

    public RecordingSession(IAudioSource source, string ffmpegPath, string outputPath, EncodeOptions options)
    {
        _source = source;
        OutputPath = outputPath;
        _encoder = new FfmpegEncoder(ffmpegPath, outputPath, options, source.Format);
        _filler = new SilenceFiller(source.Format, () => _clock.Elapsed, _encoder.Write);
        _encoder.Failed += msg => Error?.Invoke(msg);
        _source.DataAvailable += OnData;
        _source.Stopped += OnSourceStopped;
    }

    public void Start()
    {
        _encoder.Start();
        _clock.Start();
        _source.Start();
        _idleTimer = new System.Threading.Timer(_ => _filler.FillIdle(), null, 100, 100);
    }

    private void OnData(byte[] buf, int count)
    {
        var p = PeakMeter.Compute(buf, count, _source.Format);
        if (p > _peak) _peak = p;
        _filler.Feed(buf, count);
    }

    private void OnSourceStopped(Exception? ex)
    {
        if (ex != null && !_stopped) Error?.Invoke("音声入力が停止しました: " + ex.Message);
    }

    /// <summary>前回取得以降のピークを返してリセットする。メーター表示用。</summary>
    public float TakePeak()
    {
        var p = _peak;
        _peak = 0;
        return p;
    }

    /// <summary>録音を止めてファイルを閉じる。UIスレッドで直接呼ばず Task.Run 経由で呼ぶこと。</summary>
    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        _clock.Stop();
        StopIdleTimer();
        _source.Stop();
        _filler.Finish();   // 停止時刻ちょうどまで無音で埋める
        _encoder.Finish();
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
        _encoder.Dispose();
    }
}
