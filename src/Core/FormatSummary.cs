using System.Globalization;

namespace Recorder.Core;

/// <summary>
/// 形式・音質の設定を1行にまとめた文と、1時間ぶんのファイルサイズの目安（PROJECT.md §3-1）。
/// サイズは目安（flac は内容で大きく変わるため PCM の約6割で見積もる。ogg の品質指定は代表的なビットレートで見積もる）。WPF には依存しない。
/// </summary>
public static class FormatSummary
{
    // Vorbis 品質 0〜10 のおおよそのビットレート(kbps)
    private static readonly int[] VorbisKbps = { 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 500 };

    public static string Describe(EncodeOptions o, int deviceRate, int deviceChannels)
    {
        var f = o.Fmt;
        var (rate, ch) = o.Resolve(deviceRate > 0 ? deviceRate : 48000, deviceChannels > 0 ? deviceChannels : 2);
        var parts = new List<string> { f.Label, (rate / 1000.0).ToString("0.##", CultureInfo.InvariantCulture) + " kHz", ChannelText(ch) };

        double kbps;
        bool vorbisQuality = f.Key == "ogg" && o.Compression >= 0;
        if (f.Key == "wav") kbps = rate * ch * 16 / 1000.0;
        else if (f.Key == "flac") kbps = rate * ch * 16 / 1000.0 * 0.6;
        else if (vorbisQuality) kbps = VorbisKbps[Math.Clamp(o.Compression, 0, 10)];
        else kbps = o.BitrateKbps;

        if (f.UsesBitrate && !vorbisQuality) parts.Add($"{o.BitrateKbps} kbps");
        if (f.Compression is { } spec && o.Compression >= 0) parts.Add($"{spec.Label} {o.Compression}");

        var bytes = kbps * 1000 / 8 * 3600;
        return string.Join(" ・ ", parts) + $"　1時間の目安 約 {SizeText(bytes)}";
    }

    private static string ChannelText(int ch) => ch switch { 1 => "モノ", 2 => "ステレオ", _ => $"{ch}ch" };

    private static string SizeText(double bytes)
    {
        var mb = bytes / 1024 / 1024;
        return mb >= 1024
            ? (mb / 1024).ToString("0.0", CultureInfo.InvariantCulture) + " GB"
            : Math.Round(mb).ToString("0", CultureInfo.InvariantCulture) + " MB";
    }
}
