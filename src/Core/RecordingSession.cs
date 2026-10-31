using System.Diagnostics;

namespace Recorder.Core;

/// <summary>1回の録音。音声入力(IAudioSource)から受けたPCMを FfmpegEncoder へ流し、ピークと経過時間を保持する。</summary>
public sealed class RecordingSession : IDisposable
{
    private readonly IAudioSource _source;
    private readonly FfmpegEncoder _encoder;
    private readonly Stopwatch _clock = new();
    private float _peak;
    private bool _stopped;

    public string OutputPath { get; }
    public AudioFormatInfo Format => _source.Format;
    public TimeSpan Elapsed => _clock.Elapsed;

    /// <summary>エンコード側の失敗理由（正常なら null）。</summary>
    public string? Failure => _encoder.Failure;

    /// <summary>入力側・エンコード側のどちらかが失敗したとき呼ばれる（別スレッドから）。</summary>
    public event Action<string>? Error;

    public RecordingSession(IAudioSource source, string ffmpegPath, string outputPath, string format)
    {
        _source = source;
        OutputPath = outputPath;
        _encoder = new FfmpegEncoder(ffmpegPath, outputPath, format, source.Format);
        _encoder.Failed += msg => Error?.Invoke(msg);
        _source.DataAvailable += OnData;
        _source.Stopped += OnSourceStopped;
    }

    public void Start()
    {
        _encoder.Start();
        _source.Start();
        _clock.Start();
    }

    private void OnData(byte[] buf, int count)
    {
        var p = PeakMeter.Compute(buf, count, _source.Format);
        if (p > _peak) _peak = p;
        _encoder.Write(buf);
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
        _source.Stop();
        _encoder.Finish();
    }

    public void Dispose()
    {
        _source.DataAvailable -= OnData;
        _source.Stopped -= OnSourceStopped;
        _source.Dispose();
        _encoder.Dispose();
    }
}
