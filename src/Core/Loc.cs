using System.Text;

namespace Recorder.Core;

/// <summary>
/// 画面の文言（Lang/*.ini の「キー=文」）の読み込みと取得。WPF・Windows API に依存しない（PROJECT.md §6-3）。
/// 先に読んだ内容へ、あとから読んだ内容が上書きされる（exe 内の既定 → exe の隣の Lang フォルダ）。
/// キーが無いときはキー名をそのまま返す（欠けている場所が画面で分かる）。
/// </summary>
public static class Loc
{
    private static readonly Dictionary<string, string> Texts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>ini 形式のテキストを読み込む。空行・「;」「#」で始まる行・「[見出し]」の行は無視。値の「\n」は改行、「\s」は半角空白、「\\」は「\」。</summary>
    public static void Load(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') continue;
            var i = line.IndexOf('=');
            if (i <= 0) continue;
            Texts[line[..i].Trim()] = Unescape(line[(i + 1)..].Trim());
        }
    }

    public static void Load(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        Load(reader.ReadToEnd());
    }

    /// <summary>exe 内に埋め込んだ既定（任意）を読み、続けて exe の隣の Lang/{lang}.ini があれば上書きする。</summary>
    public static void Init(Stream? embedded, string dir, string lang)
    {
        Texts.Clear();
        if (embedded != null) Load(embedded);
        try
        {
            var path = Path.Combine(dir, "Lang", lang + ".ini");
            if (File.Exists(path)) Load(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static string Get(string key) => Texts.TryGetValue(key, out var v) ? v : key;

    /// <summary>{0} {1} … を引数で置き換える。</summary>
    public static string Get(string key, params object[] args)
    {
        var s = Get(key);
        try { return string.Format(s, args); }
        catch (FormatException) { return s; }
    }

    public static bool Has(string key) => Texts.ContainsKey(key);

    /// <summary>「prefix1」「prefix2」…と、番号が続くあいだ読み、改行でつなぐ（ヘルプの項目など）。</summary>
    public static string Block(string prefix)
    {
        var lines = new List<string>();
        for (var n = 1; Texts.TryGetValue(prefix + n, out var v); n++) lines.Add(v);
        return string.Join("\n", lines);
    }

    private static string Unescape(string s)
    {
        if (s.IndexOf('\\') < 0) return s;
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                var c = s[i + 1];
                if (c == 'n') { sb.Append('\n'); i++; continue; }
                if (c == '\\') { sb.Append('\\'); i++; continue; }
                if (c == 's') { sb.Append(' '); i++; continue; }   // 行末などの半角空白（ini では前後の空白が消えるため）
            }
            sb.Append(s[i]);
        }
        return sb.ToString();
    }
}
