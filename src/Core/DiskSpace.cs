namespace Recorder.Core;

/// <summary>保存先のドライブの空き容量（PROJECT.md §3-1）。取れないとき（ネットワークの場所など）は null。</summary>
public static class DiskSpace
{
    /// <summary>この量を下回ったら画面に警告を出す（1GB）。</summary>
    public const long WarnBytes = 1L * 1024 * 1024 * 1024;

    /// <summary>この量を下回ったら、データを守るために録音を止めて保存する（100MB）。</summary>
    public const long StopBytes = 100L * 1024 * 1024;

    public static long? FreeBytes(string folder)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(folder));
            if (string.IsNullOrEmpty(root)) return null;
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    public static string Text(long bytes) =>
        bytes >= 1024L * 1024 * 1024 ? $"{bytes / 1024.0 / 1024 / 1024:0.0} GB" : $"{bytes / 1024.0 / 1024:0} MB";
}
