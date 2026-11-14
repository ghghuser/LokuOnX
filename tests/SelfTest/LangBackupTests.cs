using Recorder.Core;

/// <summary>文言ファイルの書き換え検知（.bak の作成）のテスト。</summary>
static class LangBackupTests
{
    public static void Run(CheckFn check, Action<string> section)
    {
        section("LangBackup");
        var dir = Path.Combine(Path.GetTempPath(), "lokuonx-lang-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            const string shipped = "a=1\nb=2\n";
            string? Emb(string n) => n == "ja.ini" ? shipped : null;
            var ja = Path.Combine(dir, "ja.ini");
            var bak = ja + ".bak";

            File.WriteAllText(ja, shipped);
            check(LangBackup.Run(dir, Emb).Count == 0 && !File.Exists(bak), "配布されたままなら控えを作らない");
            File.WriteAllText(ja, "a=1\r\nb=2\r\n");
            check(LangBackup.Run(dir, Emb).Count == 0, "改行コードだけの違いは書き換えとみなさない");

            File.WriteAllText(ja, "a=10\nb=2\n");
            check(LangBackup.Run(dir, Emb).SequenceEqual(new[] { "ja.ini" }) && File.ReadAllText(bak) == "a=10\nb=2\n", "書き換えられていたら .bak に控える");
            File.SetLastWriteTimeUtc(bak, new DateTime(2000, 1, 1));
            check(LangBackup.Run(dir, Emb).Count == 0 && File.GetLastWriteTimeUtc(bak).Year == 2000, "控え済みの内容なら何もしない");

            File.WriteAllText(ja, shipped);   // 新しい ZIP から上書き
            check(LangBackup.Run(dir, Emb).Count == 0 && File.ReadAllText(bak) == "a=10\nb=2\n", "配布の内容に戻っても、書き換えた版の控えは残る");

            File.WriteAllText(ja, "a=11\nb=2\n");
            check(LangBackup.Run(dir, Emb).Count == 1 && File.ReadAllText(bak) == "a=11\nb=2\n", "もう一度書き換えたら、控えも新しい版になる");

            File.WriteAllText(Path.Combine(dir, "my.ini"), "x=1\n");
            check(LangBackup.Run(dir, Emb).Count == 0 && !File.Exists(Path.Combine(dir, "my.ini.bak")), "配布の無いファイル（自作）は対象外");
            check(LangBackup.Run(Path.Combine(dir, "none"), Emb).Count == 0, "フォルダが無くてもエラーにならない");
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
