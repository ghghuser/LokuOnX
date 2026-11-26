namespace Gokko.Core;

/// <summary>SEのフォルダ（exe の隣の SE）。ファイルを選ぶときの最初の場所。</summary>
public static class SeFolder
{
    public static string Path => System.IO.Path.Combine(AppContext.BaseDirectory, "SE");

    /// <summary>ファイル選択の最初のフォルダ。いま選んでいるファイルのフォルダがあればそこ、なければ SE、それも無ければ空（Windows の既定）。</summary>
    public static string Initial(string currentFile)
    {
        try
        {
            var dir = string.IsNullOrWhiteSpace(currentFile) ? "" : System.IO.Path.GetDirectoryName(currentFile) ?? "";
            if (dir.Length > 0 && Directory.Exists(dir)) return dir;
            if (Directory.Exists(Path)) return Path;
        }
        catch (Exception) { }
        return "";
    }

    /// <summary>初回起動で割り当てる、同梱のサンプル（ファイル名, 表示名）。</summary>
    public static readonly (string File, string Name)[] Samples =
    {
        ("chime.wav", "チャイム"), ("drum.wav", "ドラム"), ("zap.wav", "ビーム"), ("applause.wav", "拍手"), ("buzzer.wav", "ブザー"),
    };
}
