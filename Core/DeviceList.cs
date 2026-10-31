using NAudio.CoreAudioApi;

namespace Recorder.Core;

/// <summary>入力デバイス1件。Id が null の場合は「既定のデバイス」。</summary>
public sealed record DeviceItem(string? Id, string Name);

public static class DeviceList
{
    /// <summary>先頭に「既定のデバイス」、続けて有効なマイク等の入力デバイスを返す。</summary>
    public static List<DeviceItem> GetCaptureDevices()
    {
        var list = new List<DeviceItem> { new(null, "既定のデバイス") };
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            list.Add(new DeviceItem(device.ID, device.FriendlyName));
            device.Dispose();
        }
        return list;
    }

    /// <summary>デバイスを開く。呼び出し側が Dispose する。</summary>
    public static MMDevice Open(string? id)
    {
        using var enumerator = new MMDeviceEnumerator();
        return id == null
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console)
            : enumerator.GetDevice(id);
    }
}
