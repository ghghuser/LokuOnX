namespace Recorder.Core;

/// <summary>
/// デバイス Key の約束事。"capture:default" / "render:default" は「Windowsの既定のデバイスに追従する」擬似デバイス（PROJECT.md §3-2）。
/// WPF や Windows API には依存しない（§6-3）。
/// </summary>
public static class AudioKeys
{
    public const string CaptureDefault = "capture:default";
    public const string RenderDefault = "render:default";

    public static bool IsDefault(string key) => key == CaptureDefault || key == RenderDefault;

    /// <summary>出力デバイス（PC内部音のループバック）の Key か。</summary>
    public static bool IsLoopback(string key) => key.StartsWith("render:", StringComparison.Ordinal);

    /// <summary>擬似デバイスなら、いまの既定のデバイスの実際の Key へ置き換える。見つからなければ null。</summary>
    public static string? Resolve(this IAudioDeviceProvider provider, string key) => key switch
    {
        CaptureDefault => provider.DefaultCaptureKey(),
        RenderDefault => provider.DefaultRenderKey(),
        _ => key,
    };
}
