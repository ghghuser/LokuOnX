namespace Recorder.Core;

/// <summary>
/// 録音1回分の「保存先・仮名・最終名」の計画。録音開始時に作り、停止時に最終名へ確定する。
/// 日付フォルダは録音開始日で固定。ファイル名は保存（閉じる）時に確定する（PROJECT.md §3-4）。
/// </summary>
public sealed class RecordingPlan
{
    public const string PresetName = "既定";

    private readonly NameContext _ctx;
    private readonly string _pattern;

    public string Folder { get; }
    public string TempPath { get; }
    public EncodeOptions Options { get; }

    private RecordingPlan(NameContext ctx, string pattern, string folder, string tempPath, EncodeOptions options)
    {
        _ctx = ctx;
        _pattern = pattern;
        Folder = folder;
        TempPath = tempPath;
        Options = options;
    }

    public static RecordingPlan Create(Settings s, string deviceLabel, DateTime now)
    {
        var options = s.Encode;
        var ctx = new NameContext(now, s.NextNo, s.TrackStart, TimeSpan.Zero, TimeSpan.Zero,
            FileNamer.CleanDevice(deviceLabel), PresetName, options.Fmt.Ext);
        var folder = FileNamer.Folder(s.Folder, s.DateFolder, s.DateFolderFormat, ctx);
        var temp = FileNamer.Unique(folder, FileNamer.TempName(now, ctx.No, ctx.Ext));
        return new RecordingPlan(ctx, s.Pattern, folder, temp, options);
    }

    /// <summary>画面表示用。いま録音を始めたら保存先になるフォルダ。</summary>
    public static string PreviewFolder(Settings s, DateTime now)
    {
        var ctx = new NameContext(now, s.NextNo, s.TrackStart, TimeSpan.Zero, TimeSpan.Zero,
            "", PresetName, s.Encode.Fmt.Ext);
        return FileNamer.Folder(s.Folder, s.DateFolder, s.DateFolderFormat, ctx);
    }

    /// <summary>録音を閉じた後に呼ぶ。経過時間（整数秒）から最終名を作り、仮名をリネームする。</summary>
    public FinalizeResult Finish(TimeSpan elapsed, string ffmpegPath)
    {
        var end = TimeSpan.FromSeconds(Math.Floor(elapsed.TotalSeconds));
        var name = FileNamer.FileName(_pattern, _ctx with { End = end });
        return FileFinalizer.Run(TempPath, Folder, name, Options, ffmpegPath);
    }
}
