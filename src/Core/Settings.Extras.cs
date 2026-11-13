namespace Recorder.Core;

/// <summary>Settings のうち、圧縮率・タグ・プロファイル名・見た目・スリープ防止（PROJECT.md §3-1, §3-5, §3-6）。</summary>
public sealed partial class Settings
{
    private const string TagSection = "Tags";

    /// <summary>
    /// 圧縮率（いま選んでいる形式の分。形式ごとに ini の Compression_flac のように保存）。
    /// -1＝指定しない。圧縮率の設定がない形式（wav・aac）は常に -1。
    /// </summary>
    public int Compression
    {
        get => CompressionFor(Format);
        set => SetCompressionFor(Format, value);
    }

    public int CompressionFor(string format)
    {
        var spec = OutputFormat.Get(format).Compression;
        if (spec == null) return -1;
        if (!int.TryParse(Get("Compression_" + format, spec.Default.ToString()), out var n)) return spec.Default;
        if (n == -1 && spec.NoneLabel != null) return -1;
        return n >= spec.Min && n <= spec.Max ? n : spec.Default;
    }

    public void SetCompressionFor(string format, int value) => Set("Compression_" + format, value.ToString());

    /// <summary>タグ（タイトル・アーティスト等）。既定は空。ini の [Tags] に保存。</summary>
    public TagInfo Tags
    {
        get => new(_ini.Get(TagSection, "Title", ""), _ini.Get(TagSection, "Artist", ""),
            _ini.Get(TagSection, "Album", ""), _ini.Get(TagSection, "Genre", ""),
            _ini.Get(TagSection, "Year", ""), _ini.Get(TagSection, "Comment", ""),
            _ini.Get(TagSection, "TrackNumber", "0") == "1");
        set
        {
            _ini.Set(TagSection, "Title", value.Title);
            _ini.Set(TagSection, "Artist", value.Artist);
            _ini.Set(TagSection, "Album", value.Album);
            _ini.Set(TagSection, "Genre", value.Genre);
            _ini.Set(TagSection, "Year", value.Year);
            _ini.Set(TagSection, "Comment", value.Comment);
            _ini.Set(TagSection, "TrackNumber", value.TrackNumber ? "1" : "0");
            Save();
        }
    }

    /// <summary>いま使っているプロファイルの名前（{Profile} 変数）。未選択なら「既定」。</summary>
    public string CurrentProfile
    {
        get { var p = Get("Profile", ""); return p.Length > 0 ? p : RecordingPlan.ProfileName; }
        set => Set("Profile", value);
    }

    /// <summary>"auto"（Windowsの設定に合わせる・既定）/ "light" / "dark" / "soft" / "softdark"（試作）。</summary>
    public string Theme
    {
        get { var t = Get("Theme", "auto"); return t is "light" or "dark" or "soft" or "softdark" ? t : "auto"; }
        set => Set("Theme", value);
    }

    /// <summary>録音中にスリープさせない（既定ON）。</summary>
    public bool PreventSleep { get => Get("PreventSleep", "1") != "0"; set => Set("PreventSleep", value ? "1" : "0"); }
}
