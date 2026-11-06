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
    private readonly object _lock = new();

    public LevelMonitor(IAudioDeviceProvider provider, IReadOnlyList<InputSetting> inputs)
    {
        _provider = provider;
        _inputs = inputs;
        _gain = inputs.Select(i => (float)Math.Pow(10.0, i.GainDb / 20.0)).ToArray();
        _peaks = new float[inputs.Count];
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
                src.DataAvailable += (buf, count) =>
                {
                    var p = PeakMeter.Compute(buf, count, format) * _gain[index];
                    lock (_lock) { if (p > _peaks[index]) _peaks[index] = p; }
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
        if (index >= 0 && index < _gain.Length) _gain[index] = (float)Math.Pow(10.0, gainDb / 20.0);
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
