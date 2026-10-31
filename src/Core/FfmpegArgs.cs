namespace Recorder.Core;

/// <summary>FFmpeg に渡す引数を組み立てる（標準入力のPCM → flac / mp3 / aac）。</summary>
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
        switch (f.Key)
        {
            case "mp3":
                a.AddRange(new[] { "-c:a", "libmp3lame", "-b:a", $"{o.BitrateKbps}k", "-id3v2_version", "3" });
                break;
            case "m4a":
                // 録音中は断片化MP4（途中で落ちても再生できる）。停止後に通常のm4aへ整える（FileFinalizer）
                a.AddRange(new[] { "-c:a", "aac", "-b:a", $"{o.BitrateKbps}k",
                    "-movflags", "+empty_moov+default_base_moof", "-frag_duration", "1000000" });
                break;
            case "aac":
                a.AddRange(new[] { "-c:a", "aac", "-b:a", $"{o.BitrateKbps}k" });
                break;
            default:
                a.AddRange(new[] { "-c:a", "flac" });
                break;
        }
        // 拡張子に頼らず形式を明示する（仮名の拡張子に左右されない）
        a.AddRange(new[] { "-f", f.Muxer, outputPath });
        return a;
    }

    private static string InputFormatName(AudioFormatInfo f)
    {
        if (f.IsFloat) return f.BitsPerSample == 64 ? "f64le" : "f32le";
        return f.BitsPerSample switch { 16 => "s16le", 24 => "s24le", 32 => "s32le", _ => "s16le" };
    }
}
