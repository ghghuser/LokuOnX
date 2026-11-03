namespace Recorder.Core;

/// <summary>
/// タグ（メタデータ）。既定はすべて空。TrackNumber が true なら、分割した録音の全ファイルにトラック番号タグを付ける（PROJECT.md §3-5）。
/// WPF には依存しない。
/// </summary>
public sealed record TagInfo(string Title = "", string Artist = "", string Album = "",
    string Genre = "", string Year = "", string Comment = "", bool TrackNumber = false)
{
    public static readonly TagInfo Empty = new();

    /// <summary>FFmpeg の -metadata に渡す (キー, 値) の一覧。空の項目は含めない。</summary>
    public IEnumerable<(string Key, string Value)> Pairs()
    {
        if (Title.Length > 0) yield return ("title", Title);
        if (Artist.Length > 0) yield return ("artist", Artist);
        if (Album.Length > 0) yield return ("album", Album);
        if (Genre.Length > 0) yield return ("genre", Genre);
        if (Year.Length > 0) yield return ("date", Year);
        if (Comment.Length > 0) yield return ("comment", Comment);
    }
}
