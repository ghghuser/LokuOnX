using System.Text.RegularExpressions;
using Recorder.Core;

/// <summary>文言（Loc）の読み込みと、画面・コードが使うキーが Lang/ja.ini に全部あることの確認。</summary>
static class LocTests
{
    public static void Run(CheckFn check, Action<string> section)
    {
        section("Loc");
        Loc.Init(null, Path.GetTempPath(), "none");
        Loc.Load("; コメント\n[見出し]\na.b=こんにちは\nc=1行目\\n2行目\\\\終わり\nd=値{0}と{1}\n=空のキー\nno equal\n");
        check(Loc.Get("a.b") == "こんにちは", "キー=文 を読める");
        check(Loc.Get("c") == "1行目\n2行目\\終わり", "\\n は改行、\\\\ は \\");
        check(Loc.Get("d", "X", 2) == "値Xと2", "{0} {1} を置き換える");
        check(Loc.Get("missing.key") == "missing.key", "キーが無ければキー名を返す");
        check(Loc.Get("A.B") == "こんにちは", "キーの大文字小文字は区別しない");
        Loc.Load("a.b=上書き");
        check(Loc.Get("a.b") == "上書き", "あとから読んだ内容で上書きされる");
        Loc.Load("h.1=一\nh.2=二\nh.4=四\n");
        check(Loc.Block("h.") == "一\n二", "Block は番号が続くあいだだけつなぐ");

        var root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "src", "Lang", "ja.ini"))) root = Path.GetDirectoryName(root);
        if (root == null) { check(false, "src/Lang/ja.ini が見つからない"); return; }
        Loc.Init(null, Path.GetTempPath(), "none");
        Loc.Load(File.ReadAllText(Path.Combine(root, "src", "Lang", "ja.ini")));

        var dup = File.ReadAllLines(Path.Combine(root, "src", "Lang", "ja.ini")).Select(l => l.Trim())
            .Where(l => l.Length > 0 && l[0] != ';' && l[0] != '#' && l[0] != '[' && l.Contains('='))
            .Select(l => l[..l.IndexOf('=')].Trim().ToLowerInvariant()).GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        check(dup.Count == 0, "ja.ini のキーに重複がない（大文字小文字は区別しない）", string.Join(", ", dup));

        var missing = new List<string>();
        var xaml = new Regex(@"\{ui:T\s+([\w.]+)\}");
        var code = new Regex(@"Loc\.(?:Get|Has)\(""([\w.]+)""");
        var block = new Regex(@"Loc\.Block\(""([\w.]+)""");
        foreach (var f in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories))
        {
            if (f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
            if (f.EndsWith(".xaml"))
                foreach (Match x in xaml.Matches(File.ReadAllText(f))) if (!Loc.Has(x.Groups[1].Value)) missing.Add(x.Groups[1].Value);
            else if (f.EndsWith(".cs"))
            {
                var t = File.ReadAllText(f);
                foreach (Match c in code.Matches(t)) if (!Loc.Has(c.Groups[1].Value)) missing.Add(c.Groups[1].Value);
                foreach (Match b in block.Matches(t)) if (!Loc.Has(b.Groups[1].Value + "1")) missing.Add(b.Groups[1].Value + "1");
            }
        }
        check(missing.Count == 0, "画面・コードが使う文言のキーが ja.ini に全部ある", string.Join(", ", missing.Distinct()));
    }
}
