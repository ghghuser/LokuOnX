namespace Recorder.Core;

/// <summary>
/// 無音の検出（PROJECT.md §3-3, §3-8）。音量（ピーク）と経過時間を順に渡すと、
/// 「音があったあとに、しきい値以下が指定の秒数続いた」ときに1回だけ true を返す。
/// そのあと次の音が来るまでは再び true を返さない（長い無音で何度も分割しない）。
/// WPF や Windows API には依存しない（§6-3）。
/// </summary>
public sealed class SilenceDetector
{
    private readonly float _threshold;
    private readonly double _seconds;
    private double _silent;
    private bool _armed;

    /// <param name="thresholdDb">しきい値（dB。0 に近いほど大きい音。例 -50）。</param>
    /// <param name="seconds">この秒数ずっと しきい値以下なら無音とする。</param>
    public SilenceDetector(double thresholdDb, double seconds)
    {
        _threshold = ToLinear(thresholdDb);
        _seconds = Math.Max(0.1, seconds);
    }

    public static float ToLinear(double db) => (float)Math.Pow(10.0, db / 20.0);

    /// <summary>peak = この区間の最大音量（0〜1）、elapsed = この区間の長さ（秒）。無音と判定した瞬間だけ true。</summary>
    public bool Feed(float peak, double elapsed)
    {
        if (peak >= _threshold)
        {
            _silent = 0;
            _armed = true;
            return false;
        }
        _silent += Math.Max(0, elapsed);
        if (!_armed || _silent < _seconds) return false;
        _armed = false;
        return true;
    }

    public void Reset()
    {
        _silent = 0;
        _armed = false;
    }
}
