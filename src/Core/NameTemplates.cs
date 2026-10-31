namespace Recorder.Core;

/// <summary>ファイル名テンプレート1件。</summary>
public sealed record NameTemplate(string Name, string Pattern);

/// <summary>初回起動時に登録するサンプル（削除・編集可。設定の「初期テンプレートを復元」で戻せる）。</summary>
public static class NameTemplates
{
    public const string DefaultName = "日時形式";
    public const string TrackName = "トラック形式";

    public static IReadOnlyList<NameTemplate> Defaults { get; } = new[]
    {
        new NameTemplate(DefaultName, FileNamer.DefaultPattern),
        new NameTemplate(TrackName, "No.{No}-Track{Track}_{Start}-{End}({Length}).{ext}"),
    };
}
