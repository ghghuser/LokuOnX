using System.Text;

namespace Recorder.Core;

/// <summary>
/// 想定外のエラーの記録（exe と同じフォルダの lokuonx-crash.log。書けなければ %LocalAppData%\LokuOnX）。
/// 記録の失敗で本来の動作を止めない。大きくなりすぎたら古い半分を捨てる。WPF や Windows API には依存しない（PROJECT.md §6-3）。
/// </summary>
public static class CrashLog
{
    public static string FileName => AppInfo.Name.ToLowerInvariant() + "-crash.log";

    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();

    public static void Write(string text)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}";
        var dirs = new[]
        {
            AppPaths.Dir,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Name),
        };
        lock (Gate)
        {
            foreach (var dir in dirs)
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    var path = Path.Combine(dir, FileName);
                    Trim(path);
                    File.AppendAllText(path, line, new UTF8Encoding(false));
                    return;
                }
                catch (Exception) { /* 次の場所を試す */ }
            }
        }
    }

    /// <summary>ファイルが MaxBytes を超えていたら、後ろの半分だけ残す。</summary>
    private static void Trim(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= MaxBytes) return;
        var bytes = File.ReadAllBytes(path);
        int start = bytes.Length / 2;
        while (start < bytes.Length && bytes[start - 1] != (byte)'\n') start++;   // 行の途中から始めない
        File.WriteAllBytes(path, bytes[start..]);
    }
}
