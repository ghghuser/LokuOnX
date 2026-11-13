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
        var found = new List<AudioDeviceInfo>();
        var defaultId = DefaultId(enumerator, flow);
        foreach (var dev in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (dev)
            {
                var (rate, ch) = ReadMixFormat(dev);
                // 出力側（PC音）は先頭のアイコンで区別する
                var label = loopback ? $"🔊 {dev.FriendlyName}" : $"🎤 {dev.FriendlyName}";
                var key = (loopback ? RenderPrefix : CapturePrefix) + dev.ID;
                found.Add(new AudioDeviceInfo(key, label, loopback, rate, ch, dev.ID == defaultId));
            }
        }
        // 先頭は「既定のデバイスに追従」の擬似デバイス（Windowsの既定が変わったら自動で切り替わる）
        var def = found.FirstOrDefault(d => d.IsDefault);
        list.Add(new AudioDeviceInfo(loopback ? AudioKeys.RenderDefault : AudioKeys.CaptureDefault,
            loopback ? Loc.Get("wasapideviceprovider.s01") : Loc.Get("wasapideviceprovider.s02"),
            loopback, def?.SampleRate ?? 0, def?.Channels ?? 0));
        // それぞれ名前順（同じ側のラベルは先頭のアイコンが同じなので、そのまま並べれば名前順になる）
        list.AddRange(found.OrderBy(d => d.Label, StringComparer.CurrentCultureIgnoreCase));
    }

    /// <summary>Windowsの既定のデバイスのID。1つも無いなど取得できなければ null。</summary>
    private static string? DefaultId(MMDeviceEnumerator enumerator, DataFlow flow)
    {
        try
        {
            using var dev = enumerator.GetDefaultAudioEndpoint(flow, Role.Console);
            return dev.ID;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public string? DefaultCaptureKey()
    {
        using var enumerator = new MMDeviceEnumerator();
        var id = DefaultId(enumerator, DataFlow.Capture);
        return id == null ? null : CapturePrefix + id;
    }

    public string? DefaultRenderKey()
    {
        using var enumerator = new MMDeviceEnumerator();
        var id = DefaultId(enumerator, DataFlow.Render);
        return id == null ? null : RenderPrefix + id;
    }

    public string Signature()
    {
        using var enumerator = new MMDeviceEnumerator();
        var sb = new System.Text.StringBuilder();
        foreach (var flow in new[] { DataFlow.Capture, DataFlow.Render })
        {
            sb.Append(DefaultId(enumerator, flow)).Append('>');
            foreach (var dev in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
                using (dev) sb.Append(dev.ID).Append(';');
            sb.Append('/');
        }
        return sb.ToString();
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
            throw new ArgumentException((Loc.Get("wasapideviceprovider.s03") + " ") + key);
        var id = key.Substring(loopback ? RenderPrefix.Length : CapturePrefix.Length);

        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDevice(id);
        return new WasapiSource(device, loopback);
    }
}
