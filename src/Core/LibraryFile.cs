using System.ComponentModel;
using System.Globalization;

namespace Recorder.Core;

/// <summary>ファイル一覧の1行。長さは後から（バックグラウンドで）入るので、変更を通知する。</summary>
public sealed class LibraryFile : INotifyPropertyChanged
{
    private TimeSpan? _length;

    public LibraryFile(string path, long size, DateTime created)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);
        Size = size;
        Created = created;
    }

    /// <summary>このアプリを起動した時刻。これ以後に作られたファイルを「起動後のファイル」として目立たせる。</summary>
    public static readonly DateTime SessionStart = System.Diagnostics.Process.GetCurrentProcess().StartTime;

    public string Path { get; }
    public string Name { get; }
    public long Size { get; }
    public DateTime Created { get; }
    public bool IsNew => Created >= SessionStart;

    public TimeSpan? Length
    {
        get => _length;
        set
        {
            if (_length == value) return;
            _length = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Length)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LengthText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LengthSeconds)));
        }
    }

    /// <summary>並べ替え用（未取得は -1）。</summary>
    public double LengthSeconds => _length?.TotalSeconds ?? -1;

    public string LengthText => _length is { } t ? Clock(t) : "読込中";
    public string SizeText => SizeFormat(Size);
    public string CreatedText => Created.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>画面の経過時間と同じ表記（1：05：30）。</summary>
    public static string Clock(TimeSpan t)
    {
        var s = (long)Math.Round(t.TotalSeconds);
        return $"{s / 3600}：{s / 60 % 60:00}：{s % 60:00}";
    }

    public static string SizeFormat(long bytes) =>
        bytes >= 1024L * 1024 * 1024 ? $"{bytes / 1024.0 / 1024 / 1024:0.00} GB"
        : bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024:0.0} MB"
        : $"{Math.Max(1, bytes / 1024)} KB";
}

/// <summary>保存先フォルダ（日付フォルダの中も）にある音声ファイルを探す。WPF には依存しない。</summary>
public static class AudioLibrary
{
    public static readonly string[] Extensions = { ".flac", ".mp3", ".m4a", ".aac", ".wav", ".ogg", ".opus" };

    /// <summary>録音中の仮名（_rec_*）は除く。新しい順。</summary>
    public static List<LibraryFile> Scan(string folder)
    {
        var list = new List<LibraryFile>();
        try
        {
            if (!Directory.Exists(folder)) return list;
            var opt = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = 3,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive,
            };
            foreach (var path in Directory.EnumerateFiles(folder, "*", opt))
            {
                var name = Path.GetFileName(path);
                if (name.StartsWith("_rec_", StringComparison.OrdinalIgnoreCase)) continue;
                if (!Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) continue;
                try
                {
                    var info = new FileInfo(path);
                    list.Add(new LibraryFile(path, info.Length, info.CreationTime));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        list.Sort((a, b) => b.Created.CompareTo(a.Created));
        return list;
    }
}
