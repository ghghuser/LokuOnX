using System.Diagnostics;
using System.Text;

namespace Recorder.Core;

/// <summary>
/// 保存済みのファイルのタグ（メタデータ）を FFmpeg で読み書きする（M15。ファイル一覧の右クリック「タグの変更…」）。
/// 書き込みは音をそのまま写す（-c copy）ので音質は変わらない。いったん一時ファイルへ書き出してから置き換え、作成日時・更新日時は元のまま保つ。
/// WPF には依存しない。
/// </summary>
public static class TagEditor
{
    /// <summary>タグを持てる形式か。.aac（ADTS）はタグを持てない。</summary>
    public static bool CanTag(string path) => !string.Equals(Path.GetExtension(path), ".aac", StringComparison.OrdinalIgnoreCase);

    /// <summary>ffmpeg の ffmetadata 形式（-f ffmetadata の出力）から、タイトルなどを取り出す。最初のセクション（[STREAM] など）より前の、ファイル全体のタグだけを見る。</summary>
    public static TagInfo ParseFfmetadata(string text)
    {
        string title = "", artist = "", album = "", genre = "", year = "", comment = "";
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith('[')) break;   // ここから先はストリーム・チャプターごとのタグ
            if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
            while (EndsWithOddBackslash(line) && i + 1 < lines.Length) line = line + "\n" + lines[++i];   // 値の中の改行（行末の \）
            int eq = FindUnescapedEquals(line);
            if (eq <= 0) continue;
            var key = Unescape(line[..eq]).Trim().ToLowerInvariant();
            var value = Unescape(line[(eq + 1)..]);
            switch (key)
            {
                case "title": title = value; break;
                case "artist": artist = value; break;
                case "album": album = value; break;
                case "genre": genre = value; break;
                case "date" or "year": if (year.Length == 0 || key == "date") year = value; break;
                case "comment": comment = value; break;
            }
        }
        return new TagInfo(title, artist, album, genre, year, comment);
    }

    private static bool EndsWithOddBackslash(string s)
    {
        int n = 0;
        for (int i = s.Length - 1; i >= 0 && s[i] == '\\'; i--) n++;
        return n % 2 == 1;
    }

    private static int FindUnescapedEquals(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\') { i++; continue; }
            if (s[i] == '=') return i;
        }
        return -1;
    }

    private static string Unescape(string s)
    {
        if (s.IndexOf('\\') < 0) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length) i++;
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    /// <summary>ファイルの現在のタグを読む（呼び出しをブロックするので UI スレッドでは使わない）。読めなければ空のタグ。</summary>
    public static TagInfo Read(string ffmpegPath, string path)
    {
        if (!File.Exists(ffmpegPath) || !File.Exists(path)) return TagInfo.Empty;
        var psi = new ProcessStartInfo(ffmpegPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in new[] { "-hide_banner", "-nostdin", "-v", "error", "-i", path, "-f", "ffmetadata", "-" }) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi);
            if (p == null) return TagInfo.Empty;
            var stdout = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();   // 詰まらないように読み捨てる
            if (!p.WaitForExit(30000))
            {
                try { p.Kill(true); } catch (Exception) { }
                return TagInfo.Empty;
            }
            return ParseFfmetadata(stdout.GetAwaiter().GetResult());
        }
        catch (Exception) { return TagInfo.Empty; }
    }

    /// <summary>書き込みの引数（入力→一時ファイル）。空の項目は -metadata key= で消す。</summary>
    public static IReadOnlyList<string> WriteArgs(string input, string output, TagInfo tag)
    {
        var a = new List<string> { "-hide_banner", "-nostdin", "-y", "-v", "error", "-i", input, "-map", "0", "-c", "copy", "-map_metadata", "0" };
        if (string.Equals(Path.GetExtension(output), ".mp3", StringComparison.OrdinalIgnoreCase)) a.AddRange(new[] { "-id3v2_version", "3" });   // 日本語が化けないように ID3v2.3＋UTF-16（録音時と同じ）
        foreach (var (k, v) in new[] { ("title", tag.Title), ("artist", tag.Artist), ("album", tag.Album), ("genre", tag.Genre), ("date", tag.Year), ("comment", tag.Comment) })
            a.AddRange(new[] { "-metadata", $"{k}={v}" });
        a.Add(output);
        return a;
    }

    /// <summary>タグを書き込む。成功なら null、失敗なら理由（元のファイルはそのまま）。時間がかかることがあるので、UI スレッドでは使わない。</summary>
    public static string? Write(string ffmpegPath, string path, TagInfo tag)
    {
        if (!File.Exists(ffmpegPath)) return Loc.Get("tageditor.noffmpeg");
        if (!File.Exists(path)) return Loc.Get("tageditor.nofile");
        if (!CanTag(path)) return Loc.Get("tageditor.noaac");
        var folder = Path.GetDirectoryName(path) ?? "";
        var tmp = Path.Combine(folder, "_tag_" + Guid.NewGuid().ToString("N")[..8] + Path.GetExtension(path));
        var created = File.GetCreationTime(path);
        var written = File.GetLastWriteTime(path);
        try
        {
            var psi = new ProcessStartInfo(ffmpegPath)
            {
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var a in WriteArgs(path, tmp, tag)) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p == null) return Loc.Get("tageditor.start");
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(30 * 60 * 1000))   // 長時間の録音（数GB）でも終わる時間
            {
                try { p.Kill(true); } catch (Exception) { }
                Cleanup(tmp);
                return Loc.Get("tageditor.timeout");
            }
            if (p.ExitCode != 0 || !File.Exists(tmp) || new FileInfo(tmp).Length == 0)
            {
                var msg = LastLine(stderr.GetAwaiter().GetResult());
                Cleanup(tmp);
                return Loc.Get("tageditor.failed") + (msg.Length > 0 ? ": " + msg : "");
            }
            File.Move(tmp, path, true);
            try { File.SetCreationTime(path, created); File.SetLastWriteTime(path, written); } catch (Exception) { }   // 一覧の作成日時が変わらないように
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Cleanup(tmp);
            return Loc.Get("tageditor.failed") + ": " + ex.Message;
        }
    }

    private static void Cleanup(string tmp)
    {
        try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
    }

    private static string LastLine(string text) =>
        text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? "";
}
