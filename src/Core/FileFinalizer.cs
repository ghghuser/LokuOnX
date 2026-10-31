using System.Diagnostics;
using System.Text;

namespace Recorder.Core;

/// <summary>名前の確定結果。Warning があれば画面に出す（録音自体は成功）。</summary>
public sealed record FinalizeResult(string Path, string? Warning);

/// <summary>
/// 録音を閉じた後に、仮名のファイルを最終名へ確定する（PROJECT.md §3-4）。
/// m4a は録音中が断片化MP4なので、ここで通常のm4aへ整える。整えるのに失敗しても断片化のまま残す（再生可能）。
/// </summary>
public static class FileFinalizer
{
    public static FinalizeResult Run(string tempPath, string folder, string fileName,
        EncodeOptions options, string ffmpegPath)
    {
        if (!File.Exists(tempPath))
            return new FinalizeResult(tempPath, "録音ファイルが見つかりませんでした。");

        string dest;
        try
        {
            Directory.CreateDirectory(folder);
            dest = FileNamer.Unique(folder, fileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FinalizeResult(tempPath, "保存先を用意できなかったため仮の名前のままです: " + ex.Message);
        }

        string? warning = null;
        if (options.Format == "m4a")
        {
            var err = Remux(ffmpegPath, tempPath, dest);
            if (err == null)
            {
                TryDelete(tempPath);
                return new FinalizeResult(dest, null);
            }
            warning = "m4aの整形に失敗したため、断片化MP4のまま保存しました（再生はできます）: " + err;
            TryDelete(dest);
        }

        try
        {
            File.Move(tempPath, dest);
            return new FinalizeResult(dest, warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FinalizeResult(tempPath, "名前を確定できなかったため仮の名前のままです: " + ex.Message);
        }
    }

    /// <summary>断片化MP4を通常のm4aへ作り直す（再エンコードなし）。成功なら null、失敗なら理由。</summary>
    private static string? Remux(string ffmpeg, string src, string dst)
    {
        var psi = new ProcessStartInfo(ffmpeg)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in new[] { "-hide_banner", "-loglevel", "error", "-y", "-i", src,
                     "-c", "copy", "-movflags", "+faststart", "-f", "mp4", dst })
            psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi);
            if (p == null) return "FFmpegを起動できませんでした";
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(600000))
            {
                try { p.Kill(true); } catch (Exception) { }
                return "タイムアウトしました";
            }
            stderr.Wait(2000);
            var detail = stderr.IsCompletedSuccessfully ? stderr.Result.Trim() : "";
            return p.ExitCode == 0 ? null : $"コード {p.ExitCode} {detail}".Trim();
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
