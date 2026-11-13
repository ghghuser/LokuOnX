using Recorder.Core;

namespace Recorder.UI;

/// <summary>
/// 出力ごとのエフェクト（11-5）を、入力のエフェクトと同じウィンドウ（EffectWindow）で編集するための橋渡し。
/// タブ＝出力カード（Slot は何枚目か）。設定は出力カードの OutputConfig.Fx（OutputFxSetting）へ、IOutputHost.Update 経由で即反映する。
/// </summary>
internal sealed class OutputFxHost : IEffectHost
{
    private readonly IOutputHost _host;
    private readonly Settings _s;

    public OutputFxHost(IOutputHost host, Settings settings)
    {
        _host = host;
        _s = settings;
    }

    public IReadOnlyList<(int Slot, string Name, bool Active)> Slots() =>
        _host.Configs.Select((c, i) => (i, c.Name, true)).ToList();

    public EffectSetting GetEffect(int slot) =>
        slot >= 0 && slot < _host.Configs.Count ? _host.Configs[slot].FxCopy().ToEffectSetting() : new EffectSetting();

    public void SetEffect(int slot, EffectSetting fx)
    {
        if (slot < 0 || slot >= _host.Configs.Count) return;
        var o = _host.Configs[slot].FxCopy();   // 複製に対して編集する（record の with は参照を共有するため）
        o.CopyFrom(fx);
        _host.Update(slot, _host.Configs[slot] with { Fx = o });
    }

    public bool IsRecording => false;

    public string WindowTitle => Loc.Get("outputfxhost.s01");

    public bool HasRouting => false;

    public string WindowRect
    {
        get => _s.OutputFxWindowRect;
        set => _s.OutputFxWindowRect = value;
    }
}
