namespace Recorder.Core;

/// <summary>音質指定。SampleRate / Channels が 0 のときは「デバイスに合わせる（自動）」。</summary>
public sealed record EncodeOptions(string Format, int BitrateKbps, int SampleRate, int Channels)
{
    public static readonly int[] Bitrates = { 64, 96, 128, 160, 192, 256, 320 };
    public static readonly int[] SampleRates = { 22050, 32000, 44100, 48000, 88200, 96000 };

    public OutputFormat Fmt => OutputFormat.Get(Format);

    /// <summary>
    /// 実際に書き出すサンプリング周波数とチャンネル数。デバイス側と違っても設定値へ変換する。
    /// 形式が扱えない値（mp3の48kHz超、mp3/aacの3ch以上）は上限に丸める。
    /// </summary>
    public (int Rate, int Channels) Resolve(int deviceRate, int deviceChannels)
    {
        var f = Fmt;
        var rate = SampleRate > 0 ? SampleRate : deviceRate;
        var ch = Channels > 0 ? Channels : deviceChannels;
        if (rate > f.MaxSampleRate) rate = f.MaxSampleRate;
        if (ch > f.MaxChannels) ch = f.MaxChannels;
        return (rate, ch);
    }
}
