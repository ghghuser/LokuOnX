namespace Recorder.Core;

/// <summary>
/// 圧縮率の設定欄1種類分（形式ごと）。Value が -1 のときは「指定しない」（NoneLabel の表示）。
/// 範囲は Min〜Max、既定は Default（PROJECT.md §3-1）。
/// </summary>
public sealed record CompressionSpec(string Label, int Min, int Max, int Default, string? NoneLabel, string Tooltip);

/// <summary>出力形式1種類。Key は settings.ini に保存する名前。</summary>
public sealed record OutputFormat(string Key, string Label, string Ext, string Muxer,
    bool Lossy, int MaxSampleRate, int MaxChannels, int ForcedSampleRate = 0, CompressionSpec? Compression = null)
{
    public static readonly IReadOnlyList<OutputFormat> All = new[]
    {
        new OutputFormat("flac", "flac", "flac", "flac", false, 192000, 8, 0,
            new CompressionSpec(Loc.Get("outputformat.s01"), 0, 12, 5, null, Loc.Get("outputformat.s02"))),
        new OutputFormat("mp3", "mp3", "mp3", "mp3", true, 48000, 2, 0,
            new CompressionSpec(Loc.Get("outputformat.s03"), 0, 9, -1, Loc.Get("outputformat.s04"), Loc.Get("outputformat.s05"))),
        new OutputFormat("m4a", "aac (.m4a)", "m4a", "mp4", true, 96000, 2),
        new OutputFormat("aac", "aac (.aac)", "aac", "adts", true, 96000, 2),
        new OutputFormat("wav", "wav", "wav", "wav", false, 192000, 8),
        new OutputFormat("ogg", "ogg (Vorbis)", "ogg", "ogg", true, 96000, 2, 0,
            new CompressionSpec(Loc.Get("outputformat.s06"), 0, 10, -1, Loc.Get("outputformat.s07"), Loc.Get("outputformat.s08"))),
        new OutputFormat("opus", "opus (.opus)", "opus", "ogg", true, 48000, 2, 48000,
            new CompressionSpec(Loc.Get("outputformat.s01"), 0, 10, 10, null, Loc.Get("outputformat.s09"))),
    };

    /// <summary>Key から形式を返す。不明なら flac（既定）。</summary>
    public static OutputFormat Get(string? key) => All.FirstOrDefault(f => f.Key == key) ?? All[0];

    /// <summary>この形式でビットレートを指定できるか（wav・flac は不可）。</summary>
    public bool UsesBitrate => Lossy;

    public override string ToString() => Label;
}
