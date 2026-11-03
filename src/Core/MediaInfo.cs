using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Recorder.Core;

/// <summary>
/// 音声ファイルの長さを FFmpeg（ffmpeg -i の出力の Duration 行）から読む。ffprobe は同梱しないため。
/// 同じファイル（パス・サイズ・更新時刻が同じ）は結果を覚えておく。WPF には依存しない。
/// </summary>
public sealed class MediaInfo
{
    private static readonly Regex DurationLine = new(@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.Compiled);
    private readonly ConcurrentDictionary<string, TimeSpan?> _cache = new();
    private readonly string _ffmpeg;

    public MediaInfo(string ffmpegPath) => _ffmpeg = ffmpegPath;

    /// <summary>ffmpeg -i の標準エラー出力から長さを取り出す。"N/A" や見つからないときは null。</summary>
    public static TimeSpan? ParseDuration(string text)
    {
        var m = DurationLine.Match(text);
        if (!m.Success) return null;
        var inv = CultureInfo.InvariantCulture;
        var h = int.Parse(m.Groups[1].Value, inv);
        var min = int.Parse(m.Groups[2].Value, inv);
        var sec = double.Parse(m.Groups[3].Value, inv);
        return TimeSpan.FromSeconds(h * 3600 + min * 60 + sec);
    }

    /// <summary>長さを返す（呼び出しをブロックするので、UIスレッドでは使わない）。読めなければ null。</summary>
    public TimeSpan? Get(string path)
    {
        string key;
        try
        {
            var info = new FileInfo(path);
            key = $"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }
        catch (Exception) { return null; }
        if (_cache.TryGetValue(key, out var cached)) return cached;
        var length = Read(path);
        if (length != null) _cache[key] = length;
        return length;
    }

    private TimeSpan? Read(string path)
    {
        if (!File.Exists(_ffmpeg)) return null;
        var psi = new ProcessStartInfo(_ffmpeg)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in new[] { "-hide_banner", "-nostdin", "-i", path }) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi);
            if (p == null) return null;
            var stderr = p.StandardError.ReadToEndAsync();
            // 出力先を指定していないため、FFmpeg は情報を出して終了コード1で終わる（正常）
            if (!p.WaitForExit(15000))
            {
                try { p.Kill(true); } catch (Exception) { }
                return null;
            }
            return stderr.Wait(3000) ? ParseDuration(stderr.Result) : null;
        }
        catch (Exception) { return null; }
    }
}
