namespace Recorder.Core;

/// <summary>
/// M1用の簡易ファイル名。既定の日時形式 `{Date} {Time} (No.{No}).{ext}` だけを作る。
/// テンプレート・日付フォルダ・禁止文字置換を含む本格的な FileNamer は M2 で作る。
/// </summary>
public static class OutputName
{
    public static string Build(string folder, int no, string ext, DateTime now)
    {
        var name = $"{now:yyyy_MM_dd HH_mm_ss} (No.{no})";
        var path = Path.Combine(folder, $"{name}.{ext}");
        for (int n = 2; File.Exists(path); n++)
            path = Path.Combine(folder, $"{name} ({n}).{ext}");
        return path;
    }
}
