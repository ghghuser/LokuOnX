namespace Recorder.Core;

/// <summary>
/// アプリランチャー（M17）に登録するアプリの一覧の整え方。WPF・Windows API に依存しない純粋ロジック。
/// 最大5個。前後の空白と引用符（"…"）を取り、空・長すぎるもの・使えない文字を含むものは捨てる。
/// </summary>
public static class LauncherList
{
    public const int Max = 5;
    public const int MaxPathLength = 1000;

    /// <summary>登録できる形に整える（順番は保つ。Max 個まで）。</summary>
    public static List<string> Clean(IEnumerable<string?> paths)
    {
        var list = new List<string>();
        var bad = Path.GetInvalidPathChars();
        foreach (var raw in paths)
        {
            var t = (raw ?? "").Trim().Trim('"').Trim();
            if (t.Length == 0 || t.Length > MaxPathLength || t.IndexOfAny(bad) >= 0) continue;
            list.Add(t);
            if (list.Count >= Max) break;
        }
        return list;
    }

    /// <summary>ツールチップなどに出す名前（フォルダと拡張子を除いたファイル名。取れなければパスそのまま）。</summary>
    public static string Title(string path)
    {
        var s = path.TrimEnd('\\', '/');
        int i = s.LastIndexOfAny(new[] { '\\', '/' });
        var name = i >= 0 ? s[(i + 1)..] : s;
        name = Path.GetFileNameWithoutExtension(name);
        return name.Length > 0 ? name : path;
    }
}
