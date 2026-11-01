using System.Globalization;
using System.Text.RegularExpressions;

namespace Recorder.Core;

/// <summary>
/// 異常終了で閉じられなかった（名前が仮名のままの）録音ファイルを見つけて整える（PROJECT.md §3-1, §5-6）。
/// 仮名は FileNamer.TempName が作る `_rec_日時_No番号_T番号.拡張子`。WPF には依存しない。
/// </summary>
public static class Recovery
{
    private static readonly Regex TempName = new(
        @"^_rec_(\d{8})_(\d{6})_No(\d+)(?:_T(\d+))?\.([A-Za-z0-9]+)$", RegexOptions.Compiled);

    /// <summary>保存先フォルダ（日付フォルダの中も）から、整える対象を探す。空ファイルは消す。ほかのプロセスが開いているものは除く。</summary>
    public static IReadOnlyList<string> Find(string baseFolder)
    {
        var found = new List<string>();
        try
        {
            if (!Directory.Exists(baseFolder)) return found;
            var opt = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = 3,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive,
            };
            foreach (var path in Directory.EnumerateFiles(baseFolder, "_rec_*", opt))
            {
                if (!TempName.IsMatch(Path.GetFileName(path))) continue;
                if (InUse(path)) continue;
                if (new FileInfo(path).Length == 0)
                {
                    try { File.Delete(path); } catch (Exception) { }
                    continue;
                }
                found.Add(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return found;
    }

    /// <summary>録音中のファイル（FFmpegが開いている）は、排他で開けないので対象外にできる。</summary>
    private static bool InUse(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// 仮名のファイルを「日付 時刻 (No.n-Trackxxx)＿restored.拡張子」へリネームする（m4aは通常のm4aへ整える）。
    /// 終了時刻などは分からないので、ユーザーのパターンは使わない。
    /// </summary>
    public static IReadOnlyList<FinalizeResult> Repair(IEnumerable<string> files, string ffmpegPath)
    {
        var results = new List<FinalizeResult>();
        foreach (var path in files)
        {
            var m = TempName.Match(Path.GetFileName(path));
            if (!m.Success) continue;
            var time = DateTime.TryParseExact(m.Groups[1].Value + m.Groups[2].Value, "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : File.GetCreationTime(path);
            int.TryParse(m.Groups[3].Value, out var no);
            var track = 1;
            if (m.Groups[4].Success && !int.TryParse(m.Groups[4].Value, out track)) track = 1;
            var ext = m.Groups[5].Value.ToLowerInvariant();
            var fmt = OutputFormat.All.FirstOrDefault(f => f.Ext == ext) ?? OutputFormat.All[0];

            var pattern = m.Groups[4].Success
                ? "{Date} {Time} (No.{No}-Track{Track})＿restored.{ext}"
                : "{Date} {Time} (No.{No})＿restored.{ext}";
            var ctx = new NameContext(time, no, track, TimeSpan.Zero, TimeSpan.Zero, "", RecordingPlan.PresetName, ext);
            var name = FileNamer.FileName(pattern, ctx);
            var folder = Path.GetDirectoryName(path) ?? "";
            results.Add(FileFinalizer.Run(path, folder, name, new EncodeOptions(fmt.Key, 192, 0, 0), ffmpegPath));
        }
        return results;
    }
}
