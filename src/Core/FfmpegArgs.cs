namespace Recorder.Core;

/// <summary>FFmpeg に渡す引数を組み立てる（標準入力のPCM → flac / mp3 / aac / wav / ogg / opus）。</summary>
public static class FfmpegArgs
{
    public static IReadOnlyList<string> Build(EncodeOptions o, AudioFormatInfo input, string outputPath)
    {
        var f = o.Fmt;
        var (rate, ch) = o.Resolve(input.SampleRate, input.Channels);
        var a = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-f", InputFormatName(input), "-ar", input.SampleRate.ToString(),
            "-ac", input.Channels.ToString(), "-i", "pipe:0", "-vn",
            // ここから出力側。デバイスと違っても設定値へ変換する
            "-ar", rate.ToString(), "-ac", ch.ToString(),
        };
        var q = o.Compression;
        var b = $"{o.BitrateKbps}k";
        switch (f.Key)
        {
            case "mp3":
                a.AddRange(new[] { "-c:a", "libmp3lame", "-b:a", b, "-id3v2_version", "3" });
                if (q >= 0) a.AddRange(new[] { "-compression_level", q.ToString() });
                break;
            case "m4a":
                // 録音中は断片化MP4（途中で落ちても再生できる）。停止後に通常のm4aへ整える（FileFinalizer）
                a.AddRange(new[] { "-c:a", "aac", "-b:a", b,
                    "-movflags", "+empty_moov+default_base_moof", "-frag_duration", "1000000" });
                break;
            case "aac":
                a.AddRange(new[] { "-c:a", "aac", "-b:a", b });
                break;
            case "wav":
                // 16bit PCM。4GBを超える長時間は RF64 で続ける
                a.AddRange(new[] { "-c:a", "pcm_s16le", "-rf64", "auto" });
                break;
            case "ogg":
                a.AddRange(new[] { "-c:a", "libvorbis" });
                if (q >= 0) a.AddRange(new[] { "-q:a", q.ToString() });
                else a.AddRange(new[] { "-b:a", b });
                break;
            case "opus":
                a.AddRange(new[] { "-c:a", "libopus", "-b:a", b });
                if (q >= 0) a.AddRange(new[] { "-compression_level", q.ToString() });
                break;
            default:
                a.AddRange(new[] { "-c:a", "flac" });
                if (q >= 0) a.AddRange(new[] { "-compression_level", q.ToString() });
                break;
        }
        AddTags(a, o);
        // 拡張子に頼らず形式を明示する（仮名の拡張子に左右されない）
        a.AddRange(new[] { "-f", f.Muxer, outputPath });
        return a;
    }

    /// <summary>タグ。入力ファイルのタグは引き継がない。日本語もそのまま渡す（mp3 は ID3v2.3＋UTF-16）。</summary>
    private static void AddTags(List<string> a, EncodeOptions o)
    {
        if (o.Tags == null && o.TrackNumber <= 0) return;
        foreach (var (k, v) in (o.Tags ?? TagInfo.Empty).Pairs())
            a.AddRange(new[] { "-metadata", $"{k}={v}" });
        if (o.TrackNumber > 0) a.AddRange(new[] { "-metadata", $"track={o.TrackNumber}" });
    }

    private static string InputFormatName(AudioFormatInfo f)
    {
        if (f.IsFloat) return f.BitsPerSample == 64 ? "f64le" : "f32le";
        return f.BitsPerSample switch { 16 => "s16le", 24 => "s24le", 32 => "s32le", _ => "s16le" };
    }
}
