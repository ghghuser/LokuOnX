using NAudio.CoreAudioApi;

namespace Recorder.Core.Win;

/// <summary>
/// 入力デバイス（マイク）と出力デバイス（ループバック＝PC内部音）を同じ一覧にまとめる。Windows専用。
/// 初版でスピーカーが一覧に出なかった不具合の再発防止として、出力側は必ず列挙する。
/// </summary>
public sealed class WasapiDeviceProvider : IAudioDeviceProvider
{
    private const string CapturePrefix = "capture:";
    private const string RenderPrefix = "render:";

    public IReadOnlyList<AudioDeviceInfo> List()
    {
        var result = new List<AudioDeviceInfo>();
        using var enumerator = new MMDeviceEnumerator();
        Add(result, enumerator, DataFlow.Capture);
        Add(result, enumerator, DataFlow.Render);
        return result;
    }

    private static void Add(List<AudioDeviceInfo> list, MMDeviceEnumerator enumerator, DataFlow flow)
    {
        var loopback = flow == DataFlow.Render;
        foreach (var dev in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (dev)
            {
                var (rate, ch) = ReadMixFormat(dev);
                var label = loopback ? $"🔊 {dev.FriendlyName}（PC音）" : $"🎤 {dev.FriendlyName}";
                var key = (loopback ? RenderPrefix : CapturePrefix) + dev.ID;
                list.Add(new AudioDeviceInfo(key, label, loopback, rate, ch));
            }
        }
    }

    private static (int rate, int ch) ReadMixFormat(MMDevice dev)
    {
        try
        {
            var f = dev.AudioClient.MixFormat;
            return (f.SampleRate, f.Channels);
        }
        catch (Exception)
        {
            return (0, 0);
        }
    }

    public IAudioSource Create(string key)
    {
        var loopback = key.StartsWith(RenderPrefix, StringComparison.Ordinal);
        if (!loopback && !key.StartsWith(CapturePrefix, StringComparison.Ordinal))
            throw new ArgumentException("不明なデバイスです: " + key);
        var id = key.Substring(loopback ? RenderPrefix.Length : CapturePrefix.Length);

        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDevice(id);
        return new WasapiSource(device, loopback);
    }
}
