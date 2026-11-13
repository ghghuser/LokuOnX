using System.Globalization;

namespace Recorder.Core;

/// <summary>
/// 形式・音質の設定を1行にまとめた文と、1時間ぶんのファイルサイズの目安（PROJECT.md §3-1）。
/// 例: 「flac / 16bit / 96.0kHz / ステレオ / 圧縮レベル 5 / 1時間 約 570 MB」。
/// サイズは目安（flac は内容で大きく変わるため PCM の約6割で見積もる。ogg の品質指定は代表的なビットレートで見積もる）。WPF には依存しない。
/// </summary>
public static class FormatSummary
{
    // Vorbis 品質 0〜10 のおおよそのビットレート(kbps)
    private static readonly int[] VorbisKbps = { 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 500 };

    /// <summary>サンプリング周波数の表記。小数点第一位まで必ず出す（48.0 / 44.1 / 96.0）。22.05 のように第二位が要るものはそのまま。</summary>
    public static string KHz(int rate) => (rate / 1000.0).ToString("0.0#", CultureInfo.InvariantCulture) + "kHz";

    public static string Describe(EncodeOptions o, int deviceRate, int deviceChannels)
    {
        var f = o.Fmt;
        var (rate, ch) = o.Resolve(deviceRate > 0 ? deviceRate : 48000, deviceChannels > 0 ? deviceChannels : 2);
        var parts = new List<string> { f.Label };
        if (f.Key is "flac" or "wav") parts.Add("16bit");   // 書き出しは16bit（wav＝pcm_s16le、flac＝FFmpegの既定の16bit）
        parts.Add(KHz(rate));
        parts.Add(ChannelText(ch));

        double kbps;
        bool vorbisQuality = f.Key == "ogg" && o.Compression >= 0;
        if (f.Key == "wav") kbps = rate * ch * 16 / 1000.0;
        else if (f.Key == "flac") kbps = rate * ch * 16 / 1000.0 * 0.6;
        else if (vorbisQuality) kbps = VorbisKbps[Math.Clamp(o.Compression, 0, 10)];
        else kbps = o.BitrateKbps;

        if (f.UsesBitrate && !vorbisQuality) parts.Add($"{o.BitrateKbps}kbps");
        if (f.Compression is { } spec && o.Compression >= 0) parts.Add($"{spec.Label} {o.Compression}");

        var bytes = kbps * 1000 / 8 * 3600;
        parts.Add(Loc.Get("formatsummary.s01", SizeText(bytes)));
        return string.Join(" / ", parts);
    }

    private static string ChannelText(int ch) => ch switch { 1 => Loc.Get("formatsummary.s02"), 2 => Loc.Get("formatsummary.s03"), _ => $"{ch}ch" };

    private static string SizeText(double bytes)
    {
        var mb = bytes / 1024 / 1024;
        return mb >= 1024
            ? (mb / 1024).ToString("0.0", CultureInfo.InvariantCulture) + " GB"
            : Math.Round(mb).ToString("0", CultureInfo.InvariantCulture) + " MB";
    }
}
