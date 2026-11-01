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
    private readonly int _bpf2;
    private long _writtenFrames;
    private bool _closed;
    private long _cutAt = -1;       // このフレーム数に達したところで onCut を呼ぶ（-1 = 予約なし）
    private Action? _onCut;

    public SilenceFiller(AudioFormatInfo fmt, Func<TimeSpan> elapsed, Action<byte[]> sink)
    {
        _rate = Math.Max(1, fmt.SampleRate);
        _bytesPerFrame = Math.Max(1, fmt.Channels * fmt.BitsPerSample / 8);
        _bpf2 = _bytesPerFrame;
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
            Emit(count == buf.Length ? buf : buf.AsSpan(0, count).ToArray(), frames);
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

    /// <summary>秒 → フレーム数。</summary>
    public long FramesFor(TimeSpan t) => (long)Math.Round(t.TotalSeconds * _rate);

    /// <summary>
    /// 分割の予約。書き込んだフレーム数がちょうど frames に達したところ（バッファの途中でも）で onCut を呼ぶ。
    /// onCut はこのクラスのロック内で呼ばれる。これでファイルの長さが1フレーム単位で正確になる。
    /// </summary>
    public void ScheduleCut(long frames, Action onCut)
    {
        lock (_lock) { _cutAt = frames; _onCut = onCut; }
    }

    /// <summary>手動分割。いま書き込んだ分までで切り替える（onCut をロック内で呼ぶ）。</summary>
    public void CutNow(Action onCut)
    {
        lock (_lock)
        {
            _cutAt = -1; _onCut = null;
            onCut();
        }
    }

    /// <summary>予約を取り消す。まだ切り替わっていなければ true（すでに切り替わった後なら false）。</summary>
    public bool CancelCut()
    {
        lock (_lock)
        {
            var had = _cutAt >= 0;
            _cutAt = -1; _onCut = null;
            return had;
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
            Emit(n == _rate ? _oneSecond : new byte[n * _bytesPerFrame], n);
            left -= n;
        }
    }

    /// <summary>1か所の書き込み口。分割の予約があれば、ちょうどのフレームで切り替える。</summary>
    private void Emit(byte[] data, long frames)
    {
        if (_cutAt >= 0 && _writtenFrames + frames >= _cutAt)
        {
            long first = Math.Max(0, _cutAt - _writtenFrames);
            if (first > 0)
            {
                _sink(first == frames ? data : data.AsSpan(0, (int)(first * _bpf2)).ToArray());
                _writtenFrames += first;
            }
            var cb = _onCut;
            _cutAt = -1; _onCut = null;
            cb?.Invoke();
            long rest = frames - first;
            if (rest > 0)
            {
                _sink(first == 0 ? data : data.AsSpan((int)(first * _bpf2), (int)(rest * _bpf2)).ToArray());
                _writtenFrames += rest;
            }
            return;
        }
        _sink(data);
        _writtenFrames += frames;
    }
}
