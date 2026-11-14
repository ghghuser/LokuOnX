using System.Text;

namespace Recorder.Core;

/// <summary>
/// 起動時に、Lang フォルダの文言ファイル（ja.ini など）が「配布されたままの内容」から書き換えられていたら、.bak に控えを残す。
/// 手で書き換えたあと、新しい ZIP から上書きして内容が消えるのを防ぐ（PROJECT.md §3-10）。
/// 配布されたままの内容は、exe に埋め込んだ既定（<paramref name="embedded"/>）。埋め込みの無いファイル（自作の言語ファイル）は対象外。
/// すでに同じ内容の .bak があるときは何もしない。比較は改行コードと末尾の空白を無視する。保存に失敗しても起動は続ける。
/// </summary>
public static class LangBackup
{
    /// <param name="langDir">exe の隣の Lang フォルダ。</param>
    /// <param name="embedded">ファイル名（例 "ja.ini"）→ 埋め込みの既定の内容。無ければ null。</param>
    /// <returns>控えを作った（更新した）ファイル名。</returns>
    public static List<string> Run(string langDir, Func<string, string?> embedded)
    {
        var made = new List<string>();
        try
        {
            if (!Directory.Exists(langDir)) return made;
            foreach (var path in Directory.EnumerateFiles(langDir, "*.ini"))
            {
                try
                {
                    var name = Path.GetFileName(path);
                    var shipped = embedded(name);
                    if (shipped == null) continue;
                    var current = File.ReadAllText(path, Encoding.UTF8);
                    if (Same(current, shipped)) continue;                       // 配布されたまま
                    var bak = path + ".bak";
                    if (File.Exists(bak) && Same(File.ReadAllText(bak, Encoding.UTF8), current)) continue;   // 控え済み
                    File.Copy(path, bak, overwrite: true);
                    made.Add(name);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return made;
    }

    private static string Norm(string s) => s.TrimStart('﻿').Replace("\r\n", "\n").TrimEnd();

    private static bool Same(string a, string b) => Norm(a) == Norm(b);
}
