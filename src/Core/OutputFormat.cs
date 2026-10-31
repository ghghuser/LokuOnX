namespace Recorder.Core;

/// <summary>出力形式1種類。Key は settings.ini に保存する名前。</summary>
public sealed record OutputFormat(string Key, string Label, string Ext, string Muxer,
    bool Lossy, int MaxSampleRate, int MaxChannels)
{
    public static readonly IReadOnlyList<OutputFormat> All = new[]
    {
        new OutputFormat("flac", "flac", "flac", "flac", false, 192000, 8),
        new OutputFormat("mp3", "mp3", "mp3", "mp3", true, 48000, 2),
        new OutputFormat("m4a", "aac (.m4a)", "m4a", "mp4", true, 96000, 2),
        new OutputFormat("aac", "aac (.aac)", "aac", "adts", true, 96000, 2),
    };

    /// <summary>Key から形式を返す。不明なら flac（既定）。</summary>
    public static OutputFormat Get(string? key) => All.FirstOrDefault(f => f.Key == key) ?? All[0];
}
