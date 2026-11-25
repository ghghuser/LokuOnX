using Gokko.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Gokko.Win;

/// <summary>出力デバイスの1件。</summary>
public sealed record OutputDeviceInfo(string Id, string Name, bool IsDefault);

/// <summary><see cref="PadMixer"/> の音を、選んだ出力デバイス（共有モード）へ流す。デバイスを替えるときは <see cref="Open"/> をやり直す。</summary>
public sealed class PadOutput : IDisposable
{
    private sealed class MixerProvider : ISampleProvider
    {
        private readonly PadMixer _mixer;
        public MixerProvider(PadMixer mixer) => _mixer = mixer;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(PadClip.SampleRate, 2);
        public int Read(float[] buffer, int offset, int count) => _mixer.Read(buffer, offset, count);
    }

    private readonly PadMixer _mixer;
    private WasapiOut? _out;

    public PadOutput(PadMixer mixer) => _mixer = mixer;

    public bool Running => _out != null;

    /// <summary>使える出力デバイスの一覧（先頭は Windows の既定）。</summary>
    public static List<OutputDeviceInfo> List()
    {
        var list = new List<OutputDeviceInfo>();
        using var en = new MMDeviceEnumerator();
        string defId = "";
        try { using var d = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); defId = d.ID; }
        catch (Exception) { }
        foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (d) list.Add(new OutputDeviceInfo(d.ID, d.FriendlyName, d.ID == defId));
        }
        return list.OrderByDescending(x => x.IsDefault).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>出力を開いて鳴らし始める。deviceId が空・見つからなければ Windows の既定。失敗したら例外。</summary>
    public void Open(string deviceId)
    {
        Close();
        using var en = new MMDeviceEnumerator();
        MMDevice? dev = null;
        try
        {
            if (deviceId.Length > 0)
            {
                try { dev = en.GetDevice(deviceId); }
                catch (Exception) { dev = null; }
                if (dev != null && dev.State != DeviceState.Active) { dev.Dispose(); dev = null; }
            }
            dev ??= en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var wo = new WasapiOut(dev, AudioClientShareMode.Shared, true, 60);
            try
            {
                wo.Init(new MixerProvider(_mixer));
                wo.Play();
            }
            catch
            {
                wo.Dispose();
                throw;
            }
            _out = wo;
        }
        finally { dev?.Dispose(); }
    }

    public void Close()
    {
        var o = _out;
        _out = null;
        if (o == null) return;
        try { o.Stop(); } catch (Exception) { }
        try { o.Dispose(); } catch (Exception) { }
    }

    public void Dispose() => Close();
}
