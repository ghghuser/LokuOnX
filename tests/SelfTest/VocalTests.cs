using Recorder.Core;

/// <summary>ボーカル低減（M16）: 中央の音は弱まり、左右に広がった音・対象帯域の外は残る。モノラルでは何もしない。設定の保存・変換。</summary>
static class VocalTests
{
    const int SR = 48000;

    /// <summary>1秒の正弦波を流し、後半の音量変化(dB)を返す。sideOnly=true は左右逆相（中央成分が0）の音。</summary>
    static double GainDb(EffectSetting s, double hz, bool sideOnly, int ch = 2)
    {
        var chain = new EffectChain(SR, ch); chain.Update(s);
        const int block = 480;
        var buf = new float[block * ch];
        double sumIn = 0, sumOut = 0;
        for (int off = 0; off < SR; off += block)
        {
            for (int f = 0; f < block; f++)
            {
                float v = (float)(0.2 * Math.Sin(2 * Math.PI * hz * (off + f) / SR));
                for (int c = 0; c < ch; c++) buf[f * ch + c] = sideOnly && c == 1 ? -v : v;
            }
            var inCopy = (float[])buf.Clone();
            chain.Process(buf, block);
            if (off >= SR / 2) for (int j = 0; j < buf.Length; j++) { sumIn += inCopy[j] * inCopy[j]; sumOut += buf[j] * buf[j]; }
        }
        return 10 * Math.Log10(sumOut / sumIn);
    }

    public static void Run(CheckFn check, Action<string> section)
    {
        section("ボーカル低減（M16）");
        var v = new EffectSetting { VocalOn = true, VocalAmount = 1.0, VocalLowHz = 200, VocalHighHz = 6000 };

        check(GainDb(v, 1000, false) < -35, "中央の音（左右同じ）は、対象帯域では大きく弱まる");
        check(Math.Abs(GainDb(v, 1000, true)) < 0.1, "左右逆相の音（中央成分なし）はそのまま残る");
        check(GainDb(v, 50, false) > -1, "対象帯域より低い中央の音（ベース・キック）は残る");
        check(GainDb(v, 10000, false) > -1, "対象帯域より高い中央の音は残る");

        check(GainDb(v, 3000, false) < -20 && GainDb(v, 500, false) < -25, "対象帯域の上側（3kHz）も下側（500Hz）も、位相がそろって深く弱まる（端に近いほど浅くなる）");
        var zero = new EffectSetting { VocalOn = true, VocalAmount = 0 };
        check(Math.Abs(GainDb(zero, 1000, false)) < 0.1 && Math.Abs(GainDb(zero, 1000, true)) < 0.1, "弱める量0%では、音量は変わらない（位相が少し回るだけ）");

        var half = new EffectSetting { VocalOn = true, VocalAmount = 0.5 };
        double g = GainDb(half, 1000, false);
        check(g > -7 && g < -5, "弱める量50%で、中央の音はおよそ半分（-6 dB 前後）", g.ToString("0.0"));

        // モノラル（1チャンネル）は何もしない（ビット一致）
        var mono = new EffectChain(SR, 1); mono.Update(v);
        var a = new float[480]; for (int i = 0; i < a.Length; i++) a[i] = (float)Math.Sin(i * 0.05);
        var b = (float[])a.Clone();
        mono.Process(b, 480);
        check(a.SequenceEqual(b), "モノラルの入力では何も変わらない");

        // 設定
        var d = new EffectSetting();
        check(!d.VocalOn && !d.HasAny, "初期値はオフ（何も掛からない）");
        check(new EffectSetting { VocalOn = true }.IsActive, "ボーカル低減だけオンでも「掛かる設定」になる");
        var bad = new EffectSetting { VocalAmount = 9, VocalLowHz = 1, VocalHighHz = 99999 }; bad.Normalize();
        check(bad.VocalAmount == 1 && bad.VocalLowHz == 50 && bad.VocalHighHz == 12000, "範囲外の値は範囲内へ直す");
        var nan = new EffectSetting { VocalAmount = double.NaN }; nan.Normalize();
        check(nan.VocalAmount == 0.85, "壊れた値（NaN）は既定へ戻す");

        var dst = new EffectSetting { ToRecording = true, ToOutput = false };
        dst.CopySoundFrom(v);
        check(dst.VocalOn && dst.VocalAmount == 1.0 && dst.ToRecording && !dst.ToOutput, "コピーでボーカル低減の設定も写る（反映先は変えない）");
        check(!dst.SameSound(new EffectSetting()) && dst.SameSound(v), "SameSound がボーカル低減を比べる");

        // 出力のエフェクトとの変換
        var o = new OutputFxSetting(); o.CopyFrom(v);
        check(o.VocalOn && o.IsActive && o.ToEffectSetting().SameSound(v), "出力のエフェクトへ取り込み・書き戻しで保たれる");
        check(!o.IsDefault, "ボーカル低減がオンの出力は初期値ではない");

        // ini の往復
        string dir = Path.Combine(Path.GetTempPath(), "lokuonx-vocal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var ini = new IniFile(Path.Combine(dir, "a.ini"));
            v.WriteTo(new IniSection(ini, "InputFx"), "Fx1_", false);
            o.WriteTo(new IniSection(ini, "Outputs"), "Fx1_", false);
            ini.Save();
            var ini2 = new IniFile(Path.Combine(dir, "a.ini"));
            check(EffectSetting.ReadFrom(new IniSection(ini2, "InputFx"), "Fx1_").SameSound(v), "ボーカル低減の設定が ini で往復できる（入力）");
            check(OutputFxSetting.ReadFrom(new IniSection(ini2, "Outputs"), "Fx1_").SameSound(o), "ボーカル低減の設定が ini で往復できる（出力）");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
