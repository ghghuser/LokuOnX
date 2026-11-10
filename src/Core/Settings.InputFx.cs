namespace Recorder.Core;

/// <summary>
/// Settings のうち、入力ごとのエフェクト（M11-4。PROJECT.md §3-9）。入力1〜4（入力デバイス欄の行）ごとに1つ。プリセットに含む。
/// ini: [InputFx] Fx1_GateOn / Fx1_EqBands …（入力1。2〜4は Fx2_ …）。初期値（かけない・録音に反映しない・出力に反映する）のままの入力は書かない。
/// </summary>
public sealed partial class Settings
{
    private const string FxSection = "InputFx";

    /// <summary>入力1〜4のエフェクト設定（常に4件。範囲外の値は読み込み時に範囲内へ直る）。</summary>
    public IReadOnlyList<EffectSetting> InputEffects
    {
        get
        {
            var sec = new IniSection(_ini, FxSection);
            var list = new List<EffectSetting>();
            for (int i = 1; i <= MaxInputs; i++) list.Add(EffectSetting.ReadFrom(sec, $"Fx{i}_"));
            return list;
        }
        set
        {
            var sec = new IniSection(_ini, FxSection);
            for (int i = 1; i <= MaxInputs; i++)
            {
                var prefix = $"Fx{i}_";
                foreach (var k in EffectSetting.IniKeys) _ini.Remove(FxSection, prefix + k);
                var fx = i <= value.Count ? value[i - 1] : null;
                if (fx == null) continue;
                var c = fx.Clone();
                c.Normalize();
                if (!c.IsDefault) c.WriteTo(sec, prefix, false);
            }
            Save();
        }
    }
}
