namespace Gokko.Core;

/// <summary>パッドの鳴らし方。Once＝押すたびに頭から1回／Loop＝押すと開始・もう一度押すと停止／Hold＝押している間だけ。</summary>
public enum PadMode { Once, Loop, Hold }

/// <summary>パッド1つの設定。音量は dB（左端 <see cref="MinDb"/> ＝ミュート）。</summary>
public sealed record PadConfig(string Name, string FilePath, PadMode Mode, double VolumeDb, int Color)
{
    public const double MinDb = -30;
    public const double MaxDb = 10;
    public const int ColorCount = 8;
    public const int MaxNameLength = 24;

    public static PadConfig Empty { get; } = new("", "", PadMode.Once, 0, 0);

    public bool IsEmpty => FilePath.Length == 0;

    /// <summary>画面に出す名前。名前が空ならファイル名（拡張子なし）。</summary>
    public string Title => Name.Length > 0 ? Name : (FilePath.Length > 0 ? System.IO.Path.GetFileNameWithoutExtension(FilePath) : "");

    /// <summary>範囲外・壊れた値を範囲内へ直す。</summary>
    public PadConfig Normalize()
    {
        var name = (Name ?? "").Trim();
        if (name.Length > MaxNameLength) name = name[..MaxNameLength];
        var db = double.IsFinite(VolumeDb) ? Math.Clamp(Math.Round(VolumeDb * 2) / 2, MinDb, MaxDb) : 0;
        var mode = Enum.IsDefined(Mode) ? Mode : PadMode.Once;
        return new PadConfig(name, (FilePath ?? "").Trim().Trim('"'), mode, db, ((Color % ColorCount) + ColorCount) % ColorCount);
    }

    /// <summary>dB を倍率へ。左端（<see cref="MinDb"/> 以下）はミュート。</summary>
    public static float ToGain(double db) => db <= MinDb ? 0f : (float)Math.Pow(10, db / 20);
}
