using System.Text.RegularExpressions;

namespace Recorder.Core;

/// <summary>選べる言語（コード＋表示名）。</summary>
public sealed record LangInfo(string Code, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Lang フォルダの *.ini と、exe に埋め込んだ言語から、選べる言語の一覧を作る（PROJECT.md §3-10）。
/// コードはファイル名（ja.ini → "ja"）。表示名は、ファイル（無ければ埋め込み）の「lang.name=」。無ければコードをそのまま使う。
/// </summary>
public static class LangList
{
    private static readonly Regex CodePattern = new(@"^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$", RegexOptions.Compiled);

    /// <summary>設定ファイルの値として使えるコードか。</summary>
    public static bool IsValidCode(string? code) => code != null && CodePattern.IsMatch(code);

    /// <param name="langDir">exe の隣の Lang フォルダ。</param>
    /// <param name="embeddedCodes">exe に埋め込んである言語のコード。</param>
    /// <param name="readEmbedded">コード → 埋め込みの内容（無ければ null）。</param>
    public static List<LangInfo> Scan(string langDir, IEnumerable<string> embeddedCodes, Func<string, string?> readEmbedded)
    {
        var codes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in embeddedCodes) if (IsValidCode(c)) codes.Add(c);
        try
        {
            if (Directory.Exists(langDir))
                foreach (var f in Directory.EnumerateFiles(langDir, "*.ini"))
                {
                    var code = Path.GetFileNameWithoutExtension(f);
                    if (IsValidCode(code)) codes.Add(code);
                }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        var list = new List<LangInfo>();
        foreach (var code in codes)
        {
            string? text = null;
            try
            {
                var path = Path.Combine(langDir, code + ".ini");
                if (File.Exists(path)) text = File.ReadAllText(path, System.Text.Encoding.UTF8);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            var name = NameIn(text) ?? NameIn(readEmbedded(code)) ?? code;
            list.Add(new LangInfo(code, name));
        }
        // 日本語（既定）を先頭に、あとはコード順
        return list.OrderBy(l => l.Code.Equals("ja", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(l => l.Code, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? NameIn(string? text)
    {
        if (text == null) return null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("lang.name", StringComparison.OrdinalIgnoreCase)) continue;
            var i = line.IndexOf('=');
            if (i < 0 || line[..i].Trim() != "lang.name") continue;
            var v = line[(i + 1)..].Trim();
            if (v.Length > 0) return v;
        }
        return null;
    }
}
