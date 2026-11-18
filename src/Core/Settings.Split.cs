namespace Recorder.Core;

/// <summary>Settings のうち、分割に関する部分（PROJECT.md §3-3）。</summary>
public sealed partial class Settings
{
    /// <summary>自動分割の間隔（秒）。0 = 無効（24時間の強制分割だけ）。最短10秒、最長24時間。</summary>
    public int SplitSeconds
    {
        get
        {
            return int.TryParse(Get("SplitSeconds", "0"), out var s) ? SplitTime.Clamp(s) : 0;
        }
        set => Set("SplitSeconds", SplitTime.Clamp(value).ToString());
    }
}
