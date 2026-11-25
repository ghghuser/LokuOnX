using Gokko.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Gokko.Win;

/// <summary>出力デバイス（WASAPI 共有モード）へ <see cref="PadMixer"/> の音を流す。</summary>
public sealed class PadPlayer : IDisposable
{
    private sealed class MixerProvider : ISampleProvider
    {
        private readonly PadMixer _mixer;
        public MixerProvider(PadMixer mixer) => _mixer = mixer;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(PadClip.SampleRate, 2);
        public int Read(float[] buffer, int offset, int count) => _mixer.Read(buffer, offset, count);
    }

    private WasapiOut? _out;

    public PadMixer Mixer { get; } = new();

    public sealed record Device(string Id, string Name);

    public static List<Device> Devices()
    {
        var list = new List<Device>();
        try
        {
            using var en = new MMDeviceEnumerator();
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                list.Add(new Device(d.ID, d.FriendlyName));
        }
        catch (Exception) { }
        return list.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>出力を開始する（すでに動いていれば作り直す）。deviceId が空なら既定の出力。失敗したら理由を返す（成功は null）。</summary>
    public string? Start(string deviceId)
    {
        Stop();
        try
        {
            using var en = new MMDeviceEnumerator();
            MMDevice dev;
            try { dev = deviceId.Length > 0 ? en.GetDevice(deviceId) : en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); }
            catch (Exception) { dev = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); }
            var o = new WasapiOut(dev, AudioClientShareMode.Shared, true, 60);
            o.Init(new MixerProvider(Mixer));
            o.Play();
            _out = o;
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public void Stop()
    {
        var o = _out;
        _out = null;
        if (o == null) return;
        try { o.Stop(); } catch (Exception) { }
        try { o.Dispose(); } catch (Exception) { }
    }

    public void Dispose() => Stop();
}
