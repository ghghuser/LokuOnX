using Recorder.Core;

/// <summary>言語の一覧（Lang フォルダ＋埋め込み）のテスト。</summary>
static class LangListTests
{
    public static void Run(CheckFn check, Action<string> section)
    {
        section("LangList");
        var dir = Path.Combine(Path.GetTempPath(), "lokuonx-langlist-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string? Emb(string c) => c == "ja" ? "lang.name=日本語\nx=1\n" : null;
            check(LangList.IsValidCode("ja") && LangList.IsValidCode("en-US") && !LangList.IsValidCode("") && !LangList.IsValidCode("..\\x") && !LangList.IsValidCode(null), "言語コードの形を調べる");

            var none = LangList.Scan(Path.Combine(dir, "none"), new[] { "ja" }, Emb);
            check(none.Count == 1 && none[0].Code == "ja" && none[0].Name == "日本語", "フォルダが無くても、埋め込みの日本語が出る");

            File.WriteAllText(Path.Combine(dir, "en.ini"), "lang.name=English\n");
            File.WriteAllText(Path.Combine(dir, "fr.ini"), "a=1\n");
            File.WriteAllText(Path.Combine(dir, "ja.ini.bak"), "x\n");
            File.WriteAllText(Path.Combine(dir, "bad name.ini"), "x\n");
            var l = LangList.Scan(dir, new[] { "ja" }, Emb);
            check(l.Select(x => x.Code).SequenceEqual(new[] { "ja", "en", "fr" }), "日本語が先頭、あとはコード順（.bak や不正な名前は出ない）", string.Join(",", l.Select(x => x.Code)));
            check(l[1].Name == "English" && l[2].Name == "fr", "表示名は lang.name、無ければコード");

            File.WriteAllText(Path.Combine(dir, "ja.ini"), "lang.name=にほんご\n");
            check(LangList.Scan(dir, new[] { "ja" }, Emb)[0].Name == "にほんご", "ファイルの lang.name が埋め込みより優先される");
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
