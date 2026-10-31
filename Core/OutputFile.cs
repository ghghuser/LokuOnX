using System.IO;

namespace Recorder.Core;

/// <summary>
/// M1用の簡易ファイル名。M2で FileNamer（テンプレート・日付フォルダ）に置き換える。
/// 例: 2026_10_31 08_39_47.flac ／同名があれば " (2)" を付ける。
/// </summary>
public static class OutputFile
{
    public static string Create(string folder, OutputFormat format)
    {
        Directory.CreateDirectory(folder);
        var ext = format == OutputFormat.Flac ? ".flac" : ".mp3";
        var baseName = DateTime.Now.ToString("yyyy_MM_dd HH_mm_ss");
        var path = Path.Combine(folder, baseName + ext);
        for (int n = 2; File.Exists(path); n++)
            path = Path.Combine(folder, $"{baseName} ({n}){ext}");
        return path;
    }
}
