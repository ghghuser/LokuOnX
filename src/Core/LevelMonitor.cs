namespace Recorder.Core;

/// <summary>
/// 録音していないあいだ、選んだデバイスの音量だけを見る（「音が出たら自動で録音開始」の待機と、「待機中もレベル表示」用。PROJECT.md §3-8）。
/// ファイルには何も書かない。音量はゲインを掛けたあとの値（デバイスは inputs の順）。
/// M11-2: デバイスは InputHub から借りる（出力デバイスが同じ入力を使っていても、開き直さず・ASIOでも競合せずに共有できる）。
/// 音量は DeviceStream が測ったピークを読むだけ。WPF や Windows API には依存しない（§6-3）。
/// </summary>
public sealed class LevelMonitor : IDisposable
{
    private readonly InputHub _hub;
    private readonly IReadOnlyList<InputSetting> _inputs;
    private InputHub.Entry[] _entries = Array.Empty<InputHub.Entry>();
    private readonly float[] _peaks;
    private readonly float[] _peakL, _peakR;   // デバイスごとの左右のピーク（ゲイン後）
    private readonly object _lock = new();
    private float _master = 1f;
    private float _chL, _chR;   // 出力のL/Rのピーク（デバイスごとのゲインを掛けたあとの最大。全体ゲインは取り出すときに掛ける）
    private bool _released;

    public LevelMonitor(InputHub hub, IReadOnlyList<InputSetting> inputs)
    {
        _hub = hub;
        _inputs = inputs;
        _peaks = new float[inputs.Count];
        _peakL = new float[inputs.Count];
        _peakR = new float[inputs.Count];
    }

    /// <summary>デバイスを借りる。つながった台数を返す。1台もつながらなければ例外（借りたものは返す）。</summary>
    public int Start()
    {
        var acquired = new List<InputHub.Entry>();
        try
        {
            foreach (var i in _inputs) acquired.Add(_hub.Acquire(i.Key, i.GainDb));
        }
        catch
        {
            foreach (var e in acquired) _hub.Release(e);
            throw;
        }
        int connected = acquired.Count(e => e.Input.IsConnected);
        if (connected == 0)
        {
            var errors = acquired.Select(e => e.Input.LastError).Where(m => !string.IsNullOrEmpty(m));
            foreach (var e in acquired) _hub.Release(e);
            throw new InvalidOperationException("入力デバイスを開けませんでした。\n" + string.Join("\n", errors));
        }
        _entries = acquired.ToArray();
        Drain();   // 出力などが先に開いていた入力にたまっている古いピークは、メーターに出さない
        lock (_lock)
        {
            Array.Clear(_peaks); Array.Clear(_peakL); Array.Clear(_peakR);
            _chL = _chR = 0;
        }
        return connected;
    }

    /// <summary>録音中でなくても、ゲインの変更を反映する（index は inputs の順）。</summary>
    public void SetGain(int index, double gainDb)
    {
        var e = _entries;
        if (index >= 0 && index < e.Length) e[index].Stream.SetGainDb(gainDb);   // 最小＝ミュート（0）
    }

    /// <summary>全体ゲイン（dB。最小＝ミュート）。出力のL/Rのメーターに反映する。</summary>
    public void SetMasterGain(double gainDb) => _master = GainMath.DbToLinear(gainDb);

    /// <summary>DeviceStream が測ったピークを、このモニターの集計へ移す（3つの Take から呼ぶ。取り出しが重ならないよう、ここで一本化する）。</summary>
    private void Drain()
    {
        var entries = _entries;
        lock (_lock)
        {
            for (int i = 0; i < entries.Length && i < _peaks.Length; i++)
            {
                var s = entries[i].Stream;
                var p = s.TakePeak();
                var (l, r) = s.TakePeakLR();
                if (p > _peaks[i]) _peaks[i] = p;
                if (l > _peakL[i]) _peakL[i] = l;
                if (r > _peakR[i]) _peakR[i] = r;
                if (l > _chL) _chL = l;
                if (r > _chR) _chR = r;
            }
        }
    }

    /// <summary>出力のL/R（全デバイスの最大に全体ゲインを掛けたもの）の、前回取得以降のピークを返してリセットする。</summary>
    public (float L, float R) TakeChannelPeaks()
    {
        Drain();
        lock (_lock)
        {
            var l = _chL * _master;
            var r = _chR * _master;
            _chL = _chR = 0;
            return (l, r);
        }
    }

    /// <summary>デバイスごとの、前回取得以降の左右のピーク（inputs の順）を返してリセットする。</summary>
    public (float L, float R)[] TakePeakPairs()
    {
        Drain();
        lock (_lock)
        {
            var copy = new (float L, float R)[_peakL.Length];
            for (int i = 0; i < copy.Length; i++) copy[i] = (_peakL[i], _peakR[i]);
            Array.Clear(_peakL, 0, _peakL.Length);
            Array.Clear(_peakR, 0, _peakR.Length);
            return copy;
        }
    }

    /// <summary>デバイスごとの、前回取得以降の最大音量（inputs の順）を返してリセットする。</summary>
    public float[] TakePeaks()
    {
        Drain();
        lock (_lock)
        {
            var copy = (float[])_peaks.Clone();
            Array.Clear(_peaks, 0, _peaks.Length);
            return copy;
        }
    }

    /// <summary>デバイスを手放す（ほかで使っていなければ閉じる）。UIスレッドで直接呼ばず Task.Run 経由で呼ぶこと。</summary>
    public void Stop()
    {
        InputHub.Entry[] entries;
        lock (_lock)
        {
            if (_released) return;
            _released = true;
            entries = _entries;
            _entries = Array.Empty<InputHub.Entry>();
        }
        foreach (var e in entries)
        {
            try { _hub.Release(e); } catch (Exception) { }
        }
    }

    public void Dispose() => Stop();
}
