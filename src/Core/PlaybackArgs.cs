using System.Globalization;

namespace Recorder.Core;

/// <summary>
/// 再生用の FFmpeg 引数。ファイルを読み、必要なら速度を変え（atempo＝音程は変わらない）、
/// 44.1kHz・16bit・ステレオの生PCMを標準出力へ流す。WPF には依存しない（PROJECT.md §3-7）。
/// </summary>
public static class PlaybackArgs
{
    public const double MinSpeed = 0.5;
    public const double MaxSpeed = 2.0;
    public const int SampleRate = 44100;
    public const int Channels = 2;

    public static double ClampSpeed(double speed) =>
        double.IsNaN(speed) ? 1.0 : Math.Round(Math.Clamp(speed, MinSpeed, MaxSpeed), 2);

    /// <param name="startSeconds">元のファイルの何秒目から再生するか。</param>
    public static List<string> Build(string path, double startSeconds, double speed)
    {
        var inv = CultureInfo.InvariantCulture;
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin" };
        if (startSeconds > 0.01) args.AddRange(new[] { "-ss", startSeconds.ToString("0.###", inv) });
        args.AddRange(new[] { "-i", path, "-vn", "-sn", "-dn" });
        var s = ClampSpeed(speed);
        if (Math.Abs(s - 1.0) > 0.001) args.AddRange(new[] { "-af", "atempo=" + s.ToString("0.##", inv) });
        args.AddRange(new[]
        {
            "-f", "s16le", "-acodec", "pcm_s16le",
            "-ar", SampleRate.ToString(inv), "-ac", Channels.ToString(inv), "pipe:1",
        });
        return args;
    }
}
