namespace Gokko.Core;

/// <summary>パッドの押す・離すを、鳴らし方（1回だけ／ループ／押している間だけ）に従って <see cref="PadMixer"/> へ伝える。UI のスレッドから呼ぶ。</summary>
public sealed class PadController
{
    private readonly PadBank _bank;
    private readonly PadMixer _mixer;
    private readonly Func<string, PadClip?> _load;
    private readonly HashSet<int> _held = new();

    /// <param name="load">ファイルを読み込む（失敗は null。呼び出し側がキャッシュしてよい）。</param>
    public PadController(PadBank bank, PadMixer mixer, Func<string, PadClip?> load)
    {
        _bank = bank;
        _mixer = mixer;
        _load = load;
    }

    /// <summary>押した。鳴らせなかった（空・読み込み失敗）ときは false。</summary>
    public bool Press(int id)
    {
        var p = _bank[id];
        if (p.IsEmpty) return false;
        switch (p.Mode)
        {
            case PadMode.Loop when _mixer.IsPlaying(id):
                _mixer.Stop(id);
                return true;
            case PadMode.Hold:
                if (!_held.Add(id)) return true;   // 押しっぱなしの繰り返しは無視
                break;
        }
        var clip = _load(p.FilePath);
        if (clip == null) { _held.Remove(id); return false; }
        _mixer.Start(id, clip, p.Mode == PadMode.Loop, p.VolumeDb);
        return true;
    }

    /// <summary>離した。「押している間だけ」のパッドだけ止める。</summary>
    public void Release(int id)
    {
        if (!_held.Remove(id)) return;
        _mixer.Stop(id);
    }

    public void StopAll()
    {
        _held.Clear();
        _mixer.StopAll();
    }
}
