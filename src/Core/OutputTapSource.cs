using System.Collections.Concurrent;

namespace Recorder.Core;

/// <summary>
/// 出力の録音（M13）の音源。出力1台の最終ミックス（全体音量・エフェクトを掛けたあと）を、出力デバイスのコールバック（OutputRenderer.Sink）から受け取り、
/// 録音（RecordingSession）へ float PCM として流す。デバイスのコールバックは止めてはいけないので、受けたデータはいったんキューへ入れ、
/// 別のタイマー（20ms）で DataAvailable を呼ぶ。出力デバイスが止まっている間はデータが来ないが、録音側（SilenceFiller）が壁時計に合わせて無音を補う。
/// 形式（周波数・チャンネル数）は録音を始めたときの出力デバイスのもの。途中で出力デバイスが別の形式で開き直されたときは、その間の音は録らない（Notice で知らせる）。
/// WPF や Windows API には依存しない（§6-3）。
/// </summary>
public sealed class OutputTapSource : IAudioSource
{
    private const int MaxQueuedBytes = 2 * 1024 * 1024;   // これを超えたら古いほうを捨てる（エンコードが詰まっても出力を止めない）
    private const int PumpMs = 20;

    private readonly Action<Action<float[], int, int, int>?> _attach;
    private readonly ConcurrentQueue<byte[]> _queue = new();
    private long _queued;
    private System.Threading.Timer? _timer;
    private readonly object _pumpLock = new();
    private volatile bool _running;
    private long _dropped;
    private bool _mismatchNoticed;

    public AudioFormatInfo Format { get; }

    public event Action<byte[], int>? DataAvailable;
#pragma warning disable CS0067   // IAudioSource が求めるイベント。出力の音は自分から止まらないので、ここでは発生させない
    public event Action<Exception?>? Stopped;
#pragma warning restore CS0067

    /// <summary>録音は続くが知らせたいこと（形式が変わった・詰まって捨てたなど。別スレッドから）。</summary>
    public event Action<string>? Notice;

    /// <summary>捨てたバイト数（形式違い・詰まり。診断用）。</summary>
    public long DroppedBytes => Interlocked.Read(ref _dropped);

    /// <param name="attach">出力エンジンへ「このシンクを使う」と渡す口（Start で自分の Feed を、Stop で null を渡す）。</param>
    public OutputTapSource(int rate, int channels, Action<Action<float[], int, int, int>?> attach)
    {
        Format = new AudioFormatInfo(rate, Math.Max(1, channels), 32, true);
        _attach = attach;
    }

    public void Start()
    {
        _running = true;
        _timer = new System.Threading.Timer(_ => Pump(), null, PumpMs, PumpMs);
        _attach(Feed);
    }

    /// <summary>出力デバイスのコールバックから呼ぶ（止めない・待たない）。data の先頭から frames フレームをコピーしてためる。</summary>
    public void Feed(float[] data, int frames, int channels, int rate)
    {
        if (!_running || frames <= 0) return;
        if (rate != Format.SampleRate || channels != Format.Channels)
        {
            Interlocked.Add(ref _dropped, (long)frames * channels * 4);
            if (!_mismatchNoticed)
            {
                _mismatchNoticed = true;
                Notice?.Invoke(Loc.Get("outputrec.formatChanged"));
            }
            return;
        }
        var bytes = new byte[frames * channels * 4];
        Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
        _queue.Enqueue(bytes);
        if (Interlocked.Add(ref _queued, bytes.Length) > MaxQueuedBytes)
        {
            while (Interlocked.Read(ref _queued) > MaxQueuedBytes / 2 && _queue.TryDequeue(out var old))
            {
                Interlocked.Add(ref _queued, -old.Length);
                Interlocked.Add(ref _dropped, old.Length);
            }
        }
    }

    private void Pump()
    {
        if (!Monitor.TryEnter(_pumpLock)) return;
        try
        {
            while (_queue.TryDequeue(out var b))
            {
                Interlocked.Add(ref _queued, -b.Length);
                DataAvailable?.Invoke(b, b.Length);
            }
        }
        catch (Exception ex)
        {
            Notice?.Invoke(ex.Message);
        }
        finally { Monitor.Exit(_pumpLock); }
    }

    /// <summary>止めて、ためたぶんを流し終えてから戻る。UIスレッドで直接呼ばず Task.Run 経由で呼ぶこと。</summary>
    public void Stop()
    {
        if (!_running) return;
        _running = false;
        try { _attach(null); } catch (Exception) { }
        var t = _timer;
        _timer = null;
        if (t != null)
        {
            using var done = new ManualResetEvent(false);
            t.Dispose(done);
            done.WaitOne(2000);
        }
        lock (_pumpLock)
        {
            while (_queue.TryDequeue(out var b))
            {
                Interlocked.Add(ref _queued, -b.Length);
                DataAvailable?.Invoke(b, b.Length);
            }
        }
    }

    public void Dispose()
    {
        _running = false;
        try { _attach(null); } catch (Exception) { }
        _timer?.Dispose();
        _timer = null;
    }
}
