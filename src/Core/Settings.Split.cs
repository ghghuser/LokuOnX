namespace Recorder.Core;

/// <summary>Settings のうち、分割に関する部分（PROJECT.md §3-3）。</summary>
public sealed partial class Settings
{
    /// <summary>自動分割の間隔（秒）。0 = 無効（24時間の強制分割だけ）。最短10秒、最長24時間。旧版の SplitMinutes（分）も読む。</summary>
    public int SplitSeconds
    {
        get
        {
            var v = Get("SplitSeconds", "");
            if (v.Length > 0) return int.TryParse(v, out var s) ? SplitTime.Clamp(s) : 0;
            return int.TryParse(Get("SplitMinutes", "0"), out var m) ? SplitTime.Clamp(m * 60L) : 0;
        }
        set => Set("SplitSeconds", SplitTime.Clamp(value).ToString());
    }
}
