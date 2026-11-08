namespace Recorder.Core;

/// <summary>
/// 録音していないあいだ、選んだデバイスの音量だけを見る（「音が出たら自動で録音開始」の待機と、「入力チェック」用。PROJECT.md §3-8）。
/// ファイルには何も書かない。音量はゲインを掛けたあとの値（デバイスは inputs の順）。WPF や Windows API には依存しない（§6-3）。
/// </summary>
public sealed class LevelMonitor : IDisposable
{
    private readonly IAudioDeviceProvider _provider;
    private readonly IReadOnlyList<InputSetting> _inputs;
    private readonly List<IAudioSource> _sources = new();
    private readonly float[] _gain;
    private readonly float[] _peaks;
    private readonly float[] _peakL, _peakR;   // デバイスごとの左右のピーク（ゲイン後）
    private readonly object _lock = new();
    private float _master = 1f;
    private float _chL, _chR;   // 出力のL/Rのピーク（デバイスごとのゲインを掛けたあとの最大。全体ゲインは取り出すときに掛ける）

    public LevelMonitor(IAudioDeviceProvider provider, IReadOnlyList<InputSetting> inputs)
    {
        _provider = provider;
        _inputs = inputs;
        _gain = inputs.Select(i => GainMath.DbToLinear(i.GainDb)).ToArray();   // 最小＝ミュート（0）
        _peaks = new float[inputs.Count];
        _peakL = new float[inputs.Count];
        _peakR = new float[inputs.Count];
    }

    /// <summary>デバイスを開く。開けた台数を返す。1台も開けなければ例外。</summary>
    public int Start()
    {
        var errors = new List<string>();
        for (int i = 0; i < _inputs.Count; i++)
        {
            int index = i;
            IAudioSource? src = null;
            try
            {
                var real = _provider.Resolve(_inputs[i].Key) ?? throw new InvalidOperationException("既定のデバイスが見つかりません");
                src = _provider.Create(real);
                var format = src.Format;
                var ch = new float[2];
                src.DataAvailable += (buf, count) =>
                {
                    Array.Clear(ch, 0, ch.Length);
                    PeakMeter.Channels(buf, count, format, ch);
                    var g = _gain[index];
                    float l = ch[0] * g, r = (format.Channels > 1 ? ch[1] : ch[0]) * g;
                    var p = Math.Max(l, r);
                    lock (_lock)
                    {
                        if (p > _peaks[index]) _peaks[index] = p;
                        if (l > _peakL[index]) _peakL[index] = l;
                        if (r > _peakR[index]) _peakR[index] = r;
                        if (l > _chL) _chL = l;
                        if (r > _chR) _chR = r;
                    }
                };
                src.Start();
                _sources.Add(src);
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
                try { src?.Dispose(); } catch (Exception) { }
            }
        }
        if (_sources.Count == 0)
            throw new InvalidOperationException("入力デバイスを開けませんでした。\n" + string.Join("\n", errors));
        return _sources.Count;
    }

    /// <summary>録音中でなくても、ゲインの変更を反映する（index は inputs の順）。</summary>
    public void SetGain(int index, double gainDb)
    {
        if (index >= 0 && index < _gain.Length) _gain[index] = GainMath.DbToLinear(gainDb);
    }

    /// <summary>全体ゲイン（dB。最小＝ミュート）。出力のL/Rのメーターに反映する。</summary>
    public void SetMasterGain(double gainDb) => _master = GainMath.DbToLinear(gainDb);

    /// <summary>出力のL/R（全デバイスの最大に全体ゲインを掛けたもの）の、前回取得以降のピークを返してリセットする。</summary>
    public (float L, float R) TakeChannelPeaks()
    {
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
        lock (_lock)
        {
            var copy = (float[])_peaks.Clone();
            Array.Clear(_peaks, 0, _peaks.Length);
            return copy;
        }
    }

    /// <summary>デバイスを止める。UIスレッドで直接呼ばず Task.Run 経由で呼ぶこと。</summary>
    public void Stop()
    {
        foreach (var s in _sources)
        {
            try { s.Stop(); } catch (Exception) { }
        }
    }

    public void Dispose()
    {
        foreach (var s in _sources)
        {
            try { s.Dispose(); } catch (Exception) { }
        }
        _sources.Clear();
    }
}
