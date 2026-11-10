using NAudio.CoreAudioApi;

namespace Recorder.Core.Win;

/// <summary>再生デバイス（スピーカー・ヘッドホン・VB-CABLE など）の一覧と出力の作成（M11-2）。Windows専用。</summary>
public sealed class WasapiOutputProvider : IOutputDeviceProvider
{
    public IReadOnlyList<OutputDeviceInfo> List()
    {
        var list = new List<OutputDeviceInfo> { new(OutputDeviceInfo.DefaultId, "🔊 既定の出力デバイス") };
        using var en = new MMDeviceEnumerator();
        string? defaultId = null;
        try
        {
            using var def = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
            defaultId = def.ID;
        }
        catch (Exception) { }
        var found = new List<OutputDeviceInfo>();
        foreach (var dev in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (dev)
                found.Add(new OutputDeviceInfo(dev.ID, "🔊 " + dev.FriendlyName + (dev.ID == defaultId ? " ＊" : "")));
        }
        list.AddRange(found.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase));
        return list;
    }

    public IOutputDevice Create(string id) => new OutputDevice(id);
}
