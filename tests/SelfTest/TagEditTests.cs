using Recorder.Core;

/// <summary>保存済みファイルのタグの読み書き（M15。TagEditor）の、FFmpeg を使わない部分のテスト。</summary>
static class TagEditTests
{
    public static void Run(CheckFn check, Action<string> section)
    {
        section("タグの変更（M15）");
        {
            var t = TagEditor.ParseFfmetadata(";FFMETADATA1\ntitle=朝の録音\nartist=わたし\ndate=2026\ncomment=a\\=b\\;c\\\\d\nencoder=Lavf60\n[STREAM]\ntitle=無視される\n");
            check(t.Title == "朝の録音" && t.Artist == "わたし" && t.Year == "2026", "ffmetadata からタイトル・アーティスト・年を読む");
            check(t.Comment == "a=b;c\\d", "= ; \\ のエスケープを戻す", t.Comment);
            check(t.Album == "" && t.Genre == "", "無い項目は空");
            var m = TagEditor.ParseFfmetadata(";FFMETADATA1\ncomment=1行目\\\n2行目\n");
            check(m.Comment == "1行目\n2行目", "行末の \\ は値の中の改行", m.Comment);
            check(TagEditor.ParseFfmetadata("").Title == "", "空の出力でも落ちない");
            check(!TagEditor.CanTag("a.aac") && TagEditor.CanTag("a.mp3") && TagEditor.CanTag("a.FLAC"), ".aac だけタグを持てない");
            var args = TagEditor.WriteArgs("in.mp3", "out.mp3", new TagInfo("T", "", "", "", "", "")).ToList();
            check(args.Contains("copy") && args.Contains("title=T") && args.Contains("artist=") && args.Contains("3"), "音は写し、空の項目は key= で消す。mp3 は ID3v2.3");
            check(args[^1] == "out.mp3", "最後の引数が出力先");
        }
    }
}
