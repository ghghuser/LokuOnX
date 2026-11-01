namespace Recorder.Core;

/// <summary>
/// 壁時計基準で無音を補う（PROJECT.md §5-2）。
/// ループバックは音が鳴っていないとデータが来ないため、受け取ったPCMの長さが経過時間に足りない分を
/// 無音(0)で埋め、ファイルの長さを実時間に一致させる。足りない分だけを補い、余った分は削らない。
/// </summary>
public sealed class SilenceFiller
{
    private const double ToleranceSec = 0.25;   // データ到着時：これ以内の遅れは通常の揺れとして補わない
    private const double IdleMarginSec = 0.3;   // 無音中の補填は少し手前まで（遅れて届くデータと順序が逆転しないように）
    private const double IdleToleranceSec = 0.1;

    private readonly object _lock = new();
    private readonly Action<byte[]> _sink;
    private readonly Func<TimeSpan> _elapsed;
    private readonly int _rate;
    private readonly int _bytesPerFrame;
    private readonly byte[] _oneSecond;   // 書き換えない無音1秒分。何度も使い回す
    private long _writtenFrames;
    private bool _closed;

    public SilenceFiller(AudioFormatInfo fmt, Func<TimeSpan> elapsed, Action<byte[]> sink)
    {
        _rate = Math.Max(1, fmt.SampleRate);
        _bytesPerFrame = Math.Max(1, fmt.Channels * fmt.BitsPerSample / 8);
        _elapsed = elapsed;
        _sink = sink;
        _oneSecond = new byte[_rate * _bytesPerFrame];
    }

    /// <summary>録音スレッドから。受け取ったPCMを、必要なら手前に無音を足して流す。</summary>
    public void Feed(byte[] buf, int count)
    {
        lock (_lock)
        {
            if (_closed) return;
            long frames = count / _bytesPerFrame;
            if (frames <= 0) return;
            // このデータは「いま」終わった音なので、その手前までの不足を無音で埋める
            FillTo(_elapsed().TotalSeconds - (double)frames / _rate, ToleranceSec);
            _sink(count == buf.Length ? buf : buf.AsSpan(0, count).ToArray());
            _writtenFrames += frames;
        }
    }

    /// <summary>タイマーから定期的に。データが来ない間（ループバックの無音）を埋める。</summary>
    public void FillIdle()
    {
        lock (_lock)
        {
            if (_closed) return;
            FillTo(_elapsed().TotalSeconds - IdleMarginSec, IdleToleranceSec);
        }
    }

    /// <summary>
    /// 分割用。boundary ちょうどのフレーム数まで無音で埋めてから swap を呼ぶ（フィラーのロック内なので、
    /// 前のファイルはちょうど boundary の長さになり、次のファイルはそこから始まる）。
    /// </summary>
    public void CutAt(TimeSpan boundary, Action swap)
    {
        lock (_lock)
        {
            if (!_closed) FillTo(boundary.TotalSeconds, 0);
            swap();
        }
    }

    /// <summary>停止時。経過時間ちょうどまで埋めて、以後は何も流さない。</summary>
    public void Finish()
    {
        lock (_lock)
        {
            if (_closed) return;
            FillTo(_elapsed().TotalSeconds, 0);
            _closed = true;
        }
    }

    private void FillTo(double targetSec, double toleranceSec)
    {
        long deficit = (long)(targetSec * _rate) - _writtenFrames;
        if (deficit <= (long)(toleranceSec * _rate)) return;
        for (long left = deficit; left > 0;)
        {
            long n = Math.Min(left, _rate);
            _sink(n == _rate ? _oneSecond : new byte[n * _bytesPerFrame]);
            left -= n;
        }
        _writtenFrames += deficit;
    }
}
