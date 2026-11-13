using Recorder.Core;
namespace Recorder.UI;

/// <summary>変数1つの説明。Token は入力欄にそのまま挿入される文字列。</summary>
public sealed record VarInfo(string Token, string Description);

/// <summary>「？」ボタンで開く編集ウィンドウに出す変数の一覧（PROJECT.md §3-4）。</summary>
public static class VariableHelp
{
    private static readonly VarInfo[] DateParts =
    {
        new("{yyyy}", Loc.Get("var.year4")), new("{yy}", Loc.Get("var.year2")), new("{MM}", Loc.Get("var.month")),
        new("{dd}", Loc.Get("var.day")), new("{HH}", Loc.Get("var.hour")), new("{mm}", Loc.Get("var.minute")), new("{ss}", Loc.Get("var.second")),
        new("{Date}", Loc.Get("var.Date")), new("{Time}", Loc.Get("var.Time")),
    };

    /// <summary>ファイル名パターンで使える変数。</summary>
    public static readonly IReadOnlyList<VarInfo> Pattern = new[]
    {
        new VarInfo("{No}", Loc.Get("var.No")),
        new VarInfo("{Track}", Loc.Get("var.Track")),
        new VarInfo("{Start}", Loc.Get("var.Start")),
        new VarInfo("{End}", Loc.Get("var.End")),
        new VarInfo("{Length}", Loc.Get("var.Length")),
        new VarInfo("{Device}", Loc.Get("var.Device")),
        new VarInfo("{Profile}", Loc.Get("var.Profile")),
        new VarInfo("{ext}", Loc.Get("var.ext")),
        new VarInfo("{Start:min}", Loc.Get("var.Start_min")),
        new VarInfo("{End:min}", Loc.Get("var.End_min")),
        new VarInfo("{Length:min}", Loc.Get("var.Length_min")),
    }.Concat(DateParts).ToArray();

    /// <summary>日付フォルダの名前で使える変数（日時の部品のみ）。</summary>
    public static readonly IReadOnlyList<VarInfo> Date = DateParts;
}
