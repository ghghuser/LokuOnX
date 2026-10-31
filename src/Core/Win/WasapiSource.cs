using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Recorder.Core.Win;

/// <summary>NAudio(WASAPI)による入力。マイク（キャプチャ）と出力デバイスのループバックの両方を扱う。Windows専用。</summary>
public sealed class WasapiSource : IAudioSource
{
    private readonly WasapiCapture _capture;
    private readonly ManualResetEventSlim _stopped = new(false);
    private bool _started;

    public AudioFormatInfo Format { get; }

    public event Action<byte[], int>? DataAvailable;
    public event Action<Exception?>? Stopped;

    public WasapiSource(MMDevice device, bool loopback)
    {
        _capture = loopback ? new WasapiLoopbackCapture(device) : new WasapiCapture(device);
        var wf = _capture.WaveFormat;
        if (wf is WaveFormatExtensible ext) wf = ext.ToStandardWaveFormat();
        Format = new AudioFormatInfo(wf.SampleRate, wf.Channels, wf.BitsPerSample,
            wf.Encoding == WaveFormatEncoding.IeeeFloat);
        _capture.DataAvailable += OnData;
        _capture.RecordingStopped += OnStopped;
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;
        var copy = new byte[e.BytesRecorded];
        Buffer.BlockCopy(e.Buffer, 0, copy, 0, e.BytesRecorded);
        DataAvailable?.Invoke(copy, copy.Length);
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        _stopped.Set();
        Stopped?.Invoke(e.Exception);
    }

    public void Start()
    {
        _capture.StartRecording();
        _started = true;
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        try { _capture.StopRecording(); }
        catch (Exception) { return; }
        _stopped.Wait(TimeSpan.FromSeconds(3));
    }

    public void Dispose()
    {
        _capture.DataAvailable -= OnData;
        _capture.RecordingStopped -= OnStopped;
        _capture.Dispose();
        _stopped.Dispose();
    }
}
