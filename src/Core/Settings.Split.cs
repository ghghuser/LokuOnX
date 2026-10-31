namespace Recorder.Core;

/// <summary>Settings のうち、分割に関する部分（PROJECT.md §3-3）。</summary>
public sealed partial class Settings
{
    /// <summary>自動分割の間隔（分）。0 = なし（24時間の強制分割だけ）。</summary>
    public int SplitMinutes
    {
        get => int.TryParse(Get("SplitMinutes", "0"), out var n) && n >= 0 && n <= 1440 ? n : 0;
        set => Set("SplitMinutes", value.ToString());
    }
}
