using Recorder.Core;

/// <summary>コーラス: 混ぜる量0は原音のまま、オンなら音が変わるが大きさは保たれる。設定の保存・変換。</summary>
static class ChorusTests
{
    const int SR = 48000;

    static float[] Run(EffectSetting s, int ch, double hz, int seconds = 1)
    {
        var chain = new EffectChain(SR, ch); chain.Update(s);
        const int block = 480;
        var all = new List<float>();
        var buf = new float[block * ch];
        for (int off = 0; off < SR * seconds; off += block)
        {
            for (int f = 0; f < block; f++)
            {
                float v = (float)(0.5 * Math.Sin(2 * Math.PI * hz * (off + f) / SR));
                for (int c = 0; c < ch; c++) buf[f * ch + c] = v;
            }
            chain.Process(buf, block);
            all.AddRange(buf);
        }
        return all.ToArray();
    }

    public static void RunAll(CheckFn check, Action<string> section)
    {
        section("コーラス（M16b）");
        var on = new EffectSetting { ChorusOn = true, ChorusRate = 1, ChorusDepthMs = 5, ChorusMix = 1 };
        var zero = new EffectSetting { ChorusOn = true, ChorusMix = 0 };
        var dry = Run(new EffectSetting(), 2, 440);
        var z = Run(zero, 2, 440);
        bool same = dry.Length == z.Length;
        for (int i = 0; same && i < dry.Length; i++) if (Math.Abs(dry[i] - z[i]) > 1e-6f) same = false;
        check(same, "混ぜる量0のコーラスは原音と同じ");

        var o = Run(on, 2, 440);
        bool finite = true, bounded = true, differs = false;
        for (int i = 0; i < o.Length; i++)
        {
            if (!float.IsFinite(o[i])) finite = false;
            if (Math.Abs(o[i]) > 0.5f + 1e-3f) bounded = false;
            if (i > o.Length / 2 && Math.Abs(o[i] - dry[i]) > 0.01f) differs = true;
        }
        check(finite && bounded, "コーラスの出力は有限で、入力の最大の大きさを超えない");
        check(differs, "コーラスをオンにすると音が変わる");

        var m = Run(on, 1, 440);
        check(m.All(float.IsFinite) && m.Skip(SR / 2).Any(x => Math.Abs(x) > 0.1f), "モノラルでも動く");

        check(new EffectSetting { ChorusOn = true }.IsActive && !new EffectSetting().IsActive, "コーラスだけでもエフェクトが掛かる設定になる");
        var bad = new EffectSetting { ChorusRate = 99, ChorusDepthMs = -5, ChorusMix = double.NaN };
        bad.Normalize();
        check(bad.ChorusRate == 5 && bad.ChorusDepthMs == 0 && bad.ChorusMix == 0.5, "範囲外の値は使える値に直る");

        var oset = new OutputFxSetting(); oset.CopyFrom(on);
        check(oset.ChorusOn && oset.IsActive && oset.ToEffectSetting().SameSound(on), "出力のエフェクトへ取り込み・書き戻しで保たれる");

        string dir = Path.Combine(Path.GetTempPath(), "lokuonx-chorus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var ini = new IniFile(Path.Combine(dir, "a.ini"));
            on.WriteTo(new IniSection(ini, "InputFx"), "Fx1_", false);
            oset.WriteTo(new IniSection(ini, "Outputs"), "Fx1_", false);
            ini.Save();
            var ini2 = new IniFile(Path.Combine(dir, "a.ini"));
            check(EffectSetting.ReadFrom(new IniSection(ini2, "InputFx"), "Fx1_").SameSound(on), "コーラスの設定が ini で往復できる（入力）");
            check(OutputFxSetting.ReadFrom(new IniSection(ini2, "Outputs"), "Fx1_").SameSound(oset), "コーラスの設定が ini で往復できる（出力）");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
