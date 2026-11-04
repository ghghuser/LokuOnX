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
        _name = System.IO.Path.GetFileName(path);
        Size = size;
        Created = created;
    }

    /// <summary>このアプリを起動した時刻。これ以後に作られたファイルを「起動後のファイル」として目立たせる。</summary>
    public static readonly DateTime SessionStart = System.Diagnostics.Process.GetCurrentProcess().StartTime;

    /// <summary>録音中を示す先頭の仮の行（ファイルではない）。</summary>
    public static LibraryFile Recording(string name) =>
        new(string.Empty, 0, DateTime.Now) { IsRecording = true, _name = name };

    private string _name = "";
    public bool IsRecording { get; private init; }
    public string Path { get; }
    public string Name
    {
        get => _name;
    }

    public void SetName(string name)
    {
        if (_name == name) return;
        _name = name;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
    }

    public long Size { get; }
    public DateTime Created { get; }
    public bool IsNew => !IsRecording && Created >= SessionStart;

    /// <summary>行頭の印（●）。起動後に作られたファイルと、録音中の行に付く。</summary>
    public string Mark => IsRecording ? "●" : IsNew ? "*" : "";

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

    public string LengthText => IsRecording ? "" : _length is { } t ? Clock(t) : "読込中";
    public string SizeText => IsRecording ? "" : SizeFormat(Size);
    public string CreatedText => IsRecording ? "" : Created.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);

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

    /// <summary>録音履歴のパスから一覧を作る。ファイルが無くなっているものは出さない（履歴には残す）。</summary>
    public static List<LibraryFile> FromPaths(IEnumerable<string> paths)
    {
        var list = new List<LibraryFile>();
        foreach (var path in paths)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Exists) list.Add(new LibraryFile(path, info.Length, info.CreationTime));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException) { }
        }
        return list;
    }

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

/// <summary>一覧の並べ替え。録音中の仮の行は、どの並べ方でも一番上に置く。</summary>
public sealed class LibrarySort : System.Collections.IComparer
{
    private readonly string _key;
    private readonly bool _desc;

    public LibrarySort(string key, bool desc)
    {
        _key = key;
        _desc = desc;
    }

    public int Compare(object? x, object? y)
    {
        if (x is not LibraryFile a || y is not LibraryFile b) return 0;
        if (a.IsRecording != b.IsRecording) return a.IsRecording ? -1 : 1;
        var r = _key switch
        {
            "Name" => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase),
            "LengthSeconds" => a.LengthSeconds.CompareTo(b.LengthSeconds),
            "Size" => a.Size.CompareTo(b.Size),
            _ => a.Created.CompareTo(b.Created),
        };
        return _desc ? -r : r;
    }
}
