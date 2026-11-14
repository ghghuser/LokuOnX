using System.Diagnostics;
using System.Text;

namespace Recorder.Core;

/// <summary>名前の確定結果。Warning があれば画面に出す（録音自体は成功）。</summary>
public sealed record FinalizeResult(string Path, string? Warning);

/// <summary>
/// 録音を閉じた後に、仮名のファイルを最終名へ確定する（PROJECT.md §3-4）。
/// m4a は録音中が断片化MP4なので、ここで通常のm4aへ整える。整えるのに失敗しても断片化のまま残す（再生可能）。
/// wav は異常終了すると長さ情報（ヘッダー）が壊れるので、復旧（repair = true）のときだけ作り直す。
/// </summary>
public static class FileFinalizer
{
    public static FinalizeResult Run(string tempPath, string folder, string fileName,
        EncodeOptions options, string ffmpegPath, bool repair = false)
    {
        if (!File.Exists(tempPath))
            return new FinalizeResult(tempPath, Loc.Get("filefinalizer.s01"));

        string dest;
        try
        {
            Directory.CreateDirectory(folder);
            dest = FileNamer.Unique(folder, fileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FinalizeResult(tempPath, (Loc.Get("filefinalizer.s02") + " ") + ex.Message);
        }

        string? warning = null;
        if (options.Format == "m4a" || (repair && options.Format == "wav"))
        {
            var fmt = options.Fmt;
            var err = Remux(ffmpegPath, tempPath, dest, fmt.Muxer, fmt.Key == "m4a");
            if (err == null)
            {
                TryDelete(tempPath);
                return new FinalizeResult(dest, null);
            }
            warning = options.Format == "wav"
                ? (Loc.Get("filefinalizer.s03") + " ") + err
                : (Loc.Get("filefinalizer.s04") + " ") + err;
            TryDelete(dest);
        }

        try
        {
            File.Move(tempPath, dest);
            return new FinalizeResult(dest, warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FinalizeResult(tempPath, (Loc.Get("filefinalizer.s05") + " ") + ex.Message);
        }
    }

    /// <summary>ファイルを再エンコードなしで作り直す（m4a は通常のm4aへ、wav はヘッダーを正しく）。成功なら null、失敗なら理由。</summary>
    private static string? Remux(string ffmpeg, string src, string dst, string muxer, bool faststart)
    {
        var psi = new ProcessStartInfo(ffmpeg)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-i", src, "-c", "copy" };
        if (faststart) args.AddRange(new[] { "-movflags", "+faststart" });
        else if (muxer == "wav") args.AddRange(new[] { "-rf64", "auto" });
        args.AddRange(new[] { "-f", muxer, dst });
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi);
            if (p == null) return Loc.Get("filefinalizer.s06");
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(600000))
            {
                try { p.Kill(true); } catch (Exception) { }
                return Loc.Get("filefinalizer.s07");
            }
            stderr.Wait(2000);
            var detail = stderr.IsCompletedSuccessfully ? stderr.Result.Trim() : "";
            return p.ExitCode == 0 ? null : Loc.Get("filefinalizer.s08", p.ExitCode, detail).Trim();
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
    }
}
