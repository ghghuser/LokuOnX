namespace Recorder.UI;

/// <summary>変数1つの説明。Token は入力欄にそのまま挿入される文字列。</summary>
public sealed record VarInfo(string Token, string Description);

/// <summary>「？」ボタンで開く編集ウィンドウに出す変数の一覧（PROJECT.md §3-4）。</summary>
public static class VariableHelp
{
    private static readonly VarInfo[] DateParts =
    {
        new("{yyyy}", "年（4桁）"), new("{yy}", "年（下2桁）"), new("{MM}", "月（2桁）"),
        new("{dd}", "日（2桁）"), new("{HH}", "時（24時間・2桁）"), new("{mm}", "分（2桁）"), new("{ss}", "秒（2桁）"),
        new("{Date}", "日付。{yyyy}_{MM}_{dd} と同じ"), new("{Time}", "時刻。{HH}_{mm}_{ss} と同じ"),
    };

    /// <summary>ファイル名パターンで使える変数。</summary>
    public static readonly IReadOnlyList<VarInfo> Pattern = new[]
    {
        new VarInfo("{No}", "通し番号。録音のたびに +1、終了後も維持"),
        new VarInfo("{Track}", "トラック番号（3桁）。新しい録音ごとに開始値へ戻り、分割ごとに +1"),
        new VarInfo("{Start}", "このファイルの開始時間（録音全体の0：00から）"),
        new VarInfo("{End}", "このファイルの終了時間（録音全体の0：00から）"),
        new VarInfo("{Length}", "このファイルの長さ（End − Start）"),
        new VarInfo("{Device}", "入力デバイスの名前"),
        new VarInfo("{Preset}", "プリセット名"),
        new VarInfo("{ext}", "拡張子（flac / mp3 / m4a / aac / wav / ogg / opus）"),
        new VarInfo("{Start:min}", "開始時間を分だけで表す（例 125：30）"),
        new VarInfo("{End:min}", "終了時間を分だけで表す"),
        new VarInfo("{Length:min}", "長さを分だけで表す"),
    }.Concat(DateParts).ToArray();

    /// <summary>日付フォルダの名前で使える変数（日時の部品のみ）。</summary>
    public static readonly IReadOnlyList<VarInfo> Date = DateParts;
}
