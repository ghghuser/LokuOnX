namespace Recorder.Core;

/// <summary>
/// 録音していないあいだ、選んだデバイスの音量だけを見る（「音が出たら自動で録音開始」の待機用。PROJECT.md §3-8）。
/// ファイルには何も書かない。ゲインは掛けたあとの音量で判定する。WPF や Windows API には依存しない（§6-3）。
/// </summary>
public sealed class LevelMonitor : IDisposable
{
    private readonly IAudioDeviceProvider _provider;
    private readonly IReadOnlyList<InputSetting> _inputs;
    private readonly List<IAudioSource> _sources = new();
    private readonly object _lock = new();
    private float _peak;

    public LevelMonitor(IAudioDeviceProvider provider, IReadOnlyList<InputSetting> inputs)
    {
        _provider = provider;
        _inputs = inputs;
    }

    /// <summary>デバイスを開く。開けた台数を返す。1台も開けなければ例外。</summary>
    public int Start()
    {
        var errors = new List<string>();
        foreach (var input in _inputs)
        {
            IAudioSource? src = null;
            try
            {
                var real = _provider.Resolve(input.Key) ?? throw new InvalidOperationException("既定のデバイスが見つかりません");
                src = _provider.Create(real);
                var format = src.Format;
                var gain = (float)Math.Pow(10.0, input.GainDb / 20.0);
                src.DataAvailable += (buf, count) =>
                {
                    var p = PeakMeter.Compute(buf, count, format) * gain;
                    lock (_lock) { if (p > _peak) _peak = p; }
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

    /// <summary>前回取得以降の最大音量（0〜）を返してリセットする。</summary>
    public float TakePeak()
    {
        lock (_lock)
        {
            var p = _peak;
            _peak = 0;
            return p;
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
