namespace Recorder.Core;

/// <summary>一覧のファイルに対する操作のうち、WPFにもWindowsにも依存しないもの（名前の変更）。</summary>
public static class FileOps
{
    /// <summary>拡張子を除いた名前。</summary>
    public static string Stem(string path) => Path.GetFileNameWithoutExtension(path);

    /// <summary>
    /// 名前を変えたあとのパスを返す。変えられないときは null を返して error に理由を入れる。
    /// 禁止文字は FileNamer.Sanitize と同じ規則（コロンは全角）で置き換え、拡張子は元のまま。
    /// </summary>
    public static string? PlanRename(string path, string typed, out string error)
    {
        error = "";
        var ext = Path.GetExtension(path);
        var stem = typed.Trim();
        if (stem.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) stem = stem[..^ext.Length];   // 拡張子まで書いた場合
        if (stem.Trim().Length == 0)
        {
            error = Loc.Get("fileops.s01");
            return null;
        }
        var folder = Path.GetDirectoryName(path) ?? "";
        var target = Path.Combine(folder, FileNamer.Sanitize(stem) + ext);
        if (target.Length > 259)
        {
            error = Loc.Get("fileops.s02");
            return null;
        }
        // 大文字小文字だけの変更は、同じファイル自身なので許す
        if (!string.Equals(target, path, StringComparison.OrdinalIgnoreCase) && (File.Exists(target) || Directory.Exists(target)))
        {
            error = Loc.Get("fileops.s03");
            return null;
        }
        return target;
    }

    /// <summary>PlanRename で得たパスへ名前を変える。失敗したら理由を返す（成功なら null）。</summary>
    public static string? Rename(string path, string target)
    {
        try
        {
            if (string.Equals(path, target, StringComparison.Ordinal)) return null;
            File.Move(path, target);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Loc.Get("fileops.s04") + ex.Message;
        }
    }
}
