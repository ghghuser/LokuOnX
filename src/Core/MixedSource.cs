using System.Diagnostics;

namespace Recorder.Core;

/// <summary>1台ぶんの指定（Key・表示名・ゲインdB）。</summary>
public sealed record MixInput(string Key, string Label, double GainDb);

/// <summary>
/// 複数のデバイスを壁時計に合わせてミックスし、1つの float PCM として流す音源（PROJECT.md §3-1）。
/// 1台だけのときも同じ経路を通る（ゲイン・時計ずれ補正・デバイス切断中の無音補填が共通で効く）。
/// 出力は壁時計より MarginSec だけ遅れて出る（各デバイスの無音補填が確定してから混ぜるため）。
/// WPF や Windows API には依存しない。
/// </summary>
public sealed class MixedSource : IAudioSource
{
    public const double MarginSec = 0.2;
    private const int TickMs = 20;
    private const int MaxChunkDivisor = 2;      // 1回に出す長さは最大 0.5 秒

    private readonly List<DeviceInput> _inputs = new();
    private readonly Stopwatch _clock = new();
    private readonly object _tickLock = new();
    private readonly int _rate;
    private readonly int _ch;
    private readonly float[] _mix;
    private readonly float[] _scratch;
    private System.Threading.Timer? _timer;
    private float _master = 1f;                 // 全体ゲイン（ミックス後に掛ける）
    private float _masterTarget = 1f;
    private long _mixed;                        // 出力したフレーム数
    private bool _stopped;

    public AudioFormatInfo Format { get; }

    public event Action<byte[], int>? DataAvailable;
    public event Action<Exception?>? Stopped;

    /// <summary>録音は続くが知らせたいこと（デバイスの切断・復帰など。別スレッドから）。</summary>
    public event Action<string>? Notice;

    public int DeviceCount => _inputs.Count;

    /// <summary>いまつながっているデバイスの数。</summary>
    public int ConnectedCount => _inputs.Count(i => i.IsConnected);

    /// <summary>録音中にデバイスが使えなくなった（別スレッドから。M7）。</summary>
    public event Action? DeviceLost;

    /// <summary>外れていたデバイスにつなぎ直した（別スレッドから。M7）。</summary>
    public event Action? DeviceReturned;

    /// <param name="all">デバイス一覧（共通のサンプリング周波数・チャンネル数を決めるのに使う）。</param>
    public MixedSource(IAudioDeviceProvider provider, IReadOnlyList<MixInput> inputs,
        IReadOnlyList<AudioDeviceInfo> all, bool autoResume)
    {
        if (inputs.Count == 0) throw new ArgumentException("入力デバイスがありません");
        var infos = inputs.Select(i => all.FirstOrDefault(d => d.Key == i.Key)).ToList();
        var rates = infos.Where(d => d != null && d.SampleRate > 0).Select(d => d!.SampleRate).ToList();
        var chans = infos.Where(d => d != null && d.Channels > 0).Select(d => d!.Channels).ToList();
        _rate = rates.Count > 0 ? rates.Max() : 48000;
        // 1台はそのままのチャンネル数。複数のミックスは最大ステレオまで
        int maxCh = chans.Count > 0 ? chans.Max() : 2;
        _ch = inputs.Count == 1 ? maxCh : Math.Min(2, maxCh);
        Format = new AudioFormatInfo(_rate, _ch, 32, true);

        int maxFrames = _rate / MaxChunkDivisor + 1;
        _mix = new float[maxFrames * _ch];
        _scratch = new float[maxFrames * _ch];

        foreach (var i in inputs)
        {
            var stream = new DeviceStream(_rate, _ch, Now, i.GainDb);
            var input = new DeviceInput(provider, i.Key, i.Label, stream, autoResume);
            input.Notice += m => Notice?.Invoke(m);
            input.Failed += m => Stopped?.Invoke(new InvalidOperationException(m));
            input.Lost += () => DeviceLost?.Invoke();
            input.Returned += () => DeviceReturned?.Invoke();
            _inputs.Add(input);
        }
    }

    private double Now() => _clock.Elapsed.TotalSeconds;

    /// <summary>全体ゲイン（dB。最小＝ミュート）。録音中も変えられる（1回の処理の中で滑らかに切り替わる）。</summary>
    public void SetMasterGain(double gainDb) => _masterTarget = GainMath.DbToLinear(gainDb);

    /// <summary>録音中にゲインを変える（index は inputs の順）。</summary>
    public void SetGain(int index, double gainDb)
    {
        if (index >= 0 && index < _inputs.Count) _inputs[index].Stream.SetGainDb(gainDb);
    }

    /// <summary>デバイスごとの、ゲインを掛けたあとの前回取得以降のピーク。</summary>
    public float[] TakeDevicePeaks() => _inputs.Select(i => i.Stream.TakePeak()).ToArray();

    /// <summary>デバイスごとの、ゲインを掛けたあとの前回取得以降の左右のピーク。</summary>
    public (float L, float R)[] TakeDevicePeakPairs() => _inputs.Select(i => i.Stream.TakePeakLR()).ToArray();

    public void Start()
    {
        _clock.Start();
        var errors = new List<string>();
        int connected = 0;
        foreach (var input in _inputs)
        {
            if (input.Start()) connected++;
            else errors.Add($"{input.Label}: {input.LastError}");
        }
        if (connected == 0)
        {
            foreach (var input in _inputs) input.Stop();
            throw new InvalidOperationException("入力デバイスを開けませんでした。\n" + string.Join("\n", errors));
        }
        foreach (var e in errors)
            Notice?.Invoke("デバイスを開けませんでした（つながったら自動で再開します）: " + e);
        _timer = new System.Threading.Timer(_ => Tick(), null, TickMs, TickMs);
    }

    private void Tick()
    {
        if (!Monitor.TryEnter(_tickLock)) return;
        try
        {
            if (_stopped) return;
            Pull((long)((Now() - MarginSec) * _rate), flush: false);
        }
        catch (Exception ex)
        {
            Notice?.Invoke("ミックス処理でエラーが起きました: " + ex.Message);
        }
        finally
        {
            Monitor.Exit(_tickLock);
        }
    }

    /// <summary>出力済みの位置から target フレームまで、全デバイスを混ぜて流す。</summary>
    private void Pull(long target, bool flush)
    {
        int maxChunk = _rate / MaxChunkDivisor;
        while (target > _mixed)
        {
            int n = (int)Math.Min(target - _mixed, maxChunk);
            if (!flush && n < _rate / 200) break;   // 5ms 未満はまとめて次の周期で
            int floats = n * _ch;
            Array.Clear(_mix, 0, floats);
            foreach (var input in _inputs) input.Stream.MixInto(_mix, n, _scratch);

            float g0 = _master, g1 = _masterTarget;
            _master = g1;
            for (int i = 0; i < floats; i++)
            {
                float v = g0 == g1 ? _mix[i] * g1 : _mix[i] * (g0 + (g1 - g0) * (i / _ch) / n);
                if (float.IsNaN(v)) v = 0f;
                else if (v > 1f) v = 1f;
                else if (v < -1f) v = -1f;
                _mix[i] = v;
            }
            var bytes = new byte[floats * 4];
            Buffer.BlockCopy(_mix, 0, bytes, 0, bytes.Length);
            _mixed += n;
            DataAvailable?.Invoke(bytes, bytes.Length);
        }
    }

    /// <summary>止めて、停止時刻ちょうどまでを流し終えてから戻る。UIスレッドで直接呼ばず Task.Run 経由で呼ぶこと。</summary>
    public void Stop()
    {
        System.Threading.Timer? t;
        lock (_tickLock)
        {
            if (_stopped) return;
            _stopped = true;
            t = _timer;
            _timer = null;
        }
        t?.Dispose();
        var stopAt = _clock.Elapsed.TotalSeconds;
        foreach (var input in _inputs) input.Stop();
        lock (_tickLock) Pull((long)(stopAt * _rate), flush: true);
        _clock.Stop();
    }

    public void Dispose()
    {
        Stop();
        foreach (var input in _inputs) input.Dispose();
    }
}
