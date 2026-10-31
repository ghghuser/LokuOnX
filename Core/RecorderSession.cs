using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Recorder.Core;

/// <summary>1回の録音（マイク→WASAPI→FFmpeg→ファイル）。使い終わったら Dispose。</summary>
public sealed class RecorderSession : IDisposable
{
    private readonly MMDevice _device;
    private readonly WasapiCapture _capture;
    private readonly FfmpegEncoder _encoder;
    private readonly string _inputFormat;
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _stopping;
    private float _peak;

    public string OutputPath { get; }
    public bool EncoderFailed => _encoder.Failed;
    public string EncoderLog => _encoder.LastLog;
    public int DroppedBuffers => _encoder.DroppedBuffers;

    /// <summary>デバイス切断などで録音が予期せず止まったとき（引数はメッセージ）。UIスレッドで呼ばれる。</summary>
    public event Action<string>? Faulted;

    public RecorderSession(string? deviceId, string ffmpegPath, string outputPath, OutputFormat format)
    {
        OutputPath = outputPath;
        _device = DeviceList.Open(deviceId);
        try
        {
            _capture = new WasapiCapture(_device);
            var wf = _capture.WaveFormat;
            _inputFormat = GetInputFormat(wf);
            _encoder = new FfmpegEncoder(ffmpegPath, outputPath, format, _inputFormat, wf.SampleRate, wf.Channels);
        }
        catch
        {
            _capture?.Dispose();
            _device.Dispose();
            throw;
        }
        _capture.DataAvailable += OnData;
        _capture.RecordingStopped += OnStopped;
    }

    private static string GetInputFormat(WaveFormat wf)
    {
        var std = wf is WaveFormatExtensible ext ? ext.ToStandardWaveFormat() : wf;
        if (std.Encoding == WaveFormatEncoding.IeeeFloat && std.BitsPerSample == 32) return "f32le";
        if (std.Encoding == WaveFormatEncoding.Pcm)
        {
            switch (std.BitsPerSample)
            {
                case 16: return "s16le";
                case 24: return "s24le";
                case 32: return "s32le";
            }
        }
        throw new NotSupportedException("このデバイスの音声形式には未対応です。");
    }

    public void Start() => _capture.StartRecording();

    /// <summary>直近の区間のピーク（0〜）を返して0に戻す。メーター用。</summary>
    public float TakePeak()
    {
        var p = _peak;
        _peak = 0;
        return p;
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;
        _encoder.Write(e.Buffer, e.BytesRecorded);
        var p = LevelMeter.Peak(e.Buffer, e.BytesRecorded, _inputFormat);
        if (p > _peak) _peak = p;
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        _stopped.TrySetResult();
        if (!_stopping && e.Exception != null)
            Faulted?.Invoke(e.Exception.Message);
    }

    /// <summary>録音を止め、ファイルを閉じるまで待つ。</summary>
    public async Task StopAsync()
    {
        _stopping = true;
        try { _capture.StopRecording(); }
        catch { _stopped.TrySetResult(); }
        await Task.WhenAny(_stopped.Task, Task.Delay(3000));
        await Task.Run(_encoder.Finish);
    }

    public void Dispose()
    {
        _capture.Dispose();
        _device.Dispose();
        _encoder.Dispose();
    }
}
