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
            new CompressionSpec("圧縮レベル", 0, 12, 5, null, "大きいほどファイルが小さくなりますが、処理が重くなります（音質は変わりません）")),
        new OutputFormat("mp3", "mp3", "mp3", "mp3", true, 48000, 2, 0,
            new CompressionSpec("LAME品質", 0, 9, -1, "変更しない", "LAMEの品質設定。0＝高品質（遅い）〜9＝低品質（速い）。通常は「変更しない」で十分です")),
        new OutputFormat("m4a", "aac (.m4a)", "m4a", "mp4", true, 96000, 2),
        new OutputFormat("aac", "aac (.aac)", "aac", "adts", true, 96000, 2),
        new OutputFormat("wav", "wav", "wav", "wav", false, 192000, 8),
        new OutputFormat("ogg", "ogg (Vorbis)", "ogg", "ogg", true, 96000, 2, 0,
            new CompressionSpec("Vorbis品質", 0, 10, -1, "ビットレートで指定", "Vorbisの品質（大きいほど高音質）。選ぶとビットレートの指定は使われません")),
        new OutputFormat("opus", "opus (.opus)", "opus", "ogg", true, 48000, 2, 48000,
            new CompressionSpec("圧縮レベル", 0, 10, 10, null, "大きいほど同じビットレートで高音質になりますが、処理が重くなります")),
    };

    /// <summary>Key から形式を返す。不明なら flac（既定）。</summary>
    public static OutputFormat Get(string? key) => All.FirstOrDefault(f => f.Key == key) ?? All[0];

    /// <summary>この形式でビットレートを指定できるか（wav・flac は不可）。</summary>
    public bool UsesBitrate => Lossy;

    public override string ToString() => Label;
}
