namespace Gokko.Core;

/// <summary>パッドを押した結果。</summary>
public enum PadPress { Started, Stopped, Empty, LoadFailed }

/// <summary>
/// パッドの押す・離すを、鳴らし方（<see cref="PadMode"/>）に従って <see cref="PadMixer"/> へ伝える。
/// 読み込み済みの音は覚えておく（ファイルの場所ごと）。読み込みは引数の関数に任せる（失敗は null）。
/// </summary>
public sealed class PadController
{
    private readonly PadBank _bank;
    private readonly PadMixer _mixer;
    private readonly Func<string, PadClip?> _load;
    private readonly Dictionary<string, PadClip?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public PadController(PadBank bank, PadMixer mixer, Func<string, PadClip?> load)
    {
        _bank = bank;
        _mixer = mixer;
        _load = load;
    }

    /// <summary>音を読み込んで覚える（すでに覚えていれば何もしない）。読めなければ null。</summary>
    public PadClip? Preload(string path)
    {
        if (path.Length == 0) return null;
        lock (_lock)
        {
            if (_cache.TryGetValue(path, out var c)) return c;
        }
        var clip = _load(path);   // 重い処理なので、鍵の外で行う
        lock (_lock) _cache[path] = clip;
        return clip;
    }

    /// <summary>覚えた音を捨てる（ファイルを差し替えたあとなど）。path が null なら全部。</summary>
    public void Forget(string? path)
    {
        lock (_lock)
        {
            if (path == null) _cache.Clear();
            else _cache.Remove(path);
        }
    }

    public PadPress Press(int id)
    {
        var p = _bank[id];
        if (p.IsEmpty) return PadPress.Empty;
        if (p.Mode == PadMode.Loop && _mixer.IsPlaying(id))
        {
            _mixer.Stop(id);
            return PadPress.Stopped;
        }
        var clip = Preload(p.FilePath);
        if (clip == null) return PadPress.LoadFailed;
        _mixer.Start(id, clip, p.Mode == PadMode.Loop, p.VolumeDb);
        return PadPress.Started;
    }

    /// <summary>離した。「押している間だけ」のパッドだけ止める。</summary>
    public void Release(int id)
    {
        if (_bank[id].Mode == PadMode.Hold) _mixer.Stop(id);
    }
}
