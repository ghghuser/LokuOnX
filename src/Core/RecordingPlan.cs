namespace Recorder.Core;

/// <summary>
/// 録音1回分の「保存先・仮名・最終名」の計画。録音開始時に作り、トラックを閉じるたびに最終名へ確定する。
/// 日付フォルダは録音開始日で固定。ファイル名は保存（閉じる）時に確定する（PROJECT.md §3-4）。
/// </summary>
public sealed class RecordingPlan
{
    public const string PresetName = "既定";

    private readonly NameContext _ctx;
    private readonly string _pattern;

    public string Folder { get; }
    public EncodeOptions Options { get; }

    private RecordingPlan(NameContext ctx, string pattern, string folder, EncodeOptions options)
    {
        _ctx = ctx;
        _pattern = pattern;
        Folder = folder;
        Options = options;
    }

    public static RecordingPlan Create(Settings s, string deviceLabel, DateTime now)
    {
        var options = s.Encode;
        var ctx = new NameContext(now, s.NextNo, s.TrackStart, TimeSpan.Zero, TimeSpan.Zero,
            FileNamer.CleanDevice(deviceLabel), PresetName, options.Fmt.Ext);
        var folder = FileNamer.Folder(s.Folder, s.DateFolder, s.DateFolderFormat, ctx);
        return new RecordingPlan(ctx, s.Pattern, folder, options);
    }

    /// <summary>画面表示用。いま録音を始めたら保存先になるフォルダ。</summary>
    public static string PreviewFolder(Settings s, DateTime now)
    {
        var ctx = new NameContext(now, s.NextNo, s.TrackStart, TimeSpan.Zero, TimeSpan.Zero,
            "", PresetName, s.Encode.Fmt.Ext);
        return FileNamer.Folder(s.Folder, s.DateFolder, s.DateFolderFormat, ctx);
    }

    /// <summary>index番目（0始まり）のトラックの {Track} の値。</summary>
    public int TrackNumber(int index) => _ctx.Track + index;

    /// <summary>index番目のトラックを録音中に書き込む仮名のパス。開始時刻・No・Track を含める（異常終了後の復旧用）。</summary>
    public string TempPathFor(int index) =>
        FileNamer.Unique(Folder, FileNamer.TempName(_ctx.Time, _ctx.No, TrackNumber(index), _ctx.Ext));

    /// <summary>トラックを閉じた後に呼ぶ。start / end（整数秒）から最終名を作り、仮名をリネームする。</summary>
    public FinalizeResult FinishTrack(string tempPath, int index, TimeSpan start, TimeSpan end, string ffmpegPath)
    {
        var name = FileNamer.FileName(_pattern, _ctx with { Track = TrackNumber(index), Start = start, End = end });
        return FileFinalizer.Run(tempPath, Folder, name, Options, ffmpegPath);
    }
}
