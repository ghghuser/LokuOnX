using Recorder.Core;

/// <summary>エフェクト設定の ini への保存・復元（IniSection 経由）のテスト。</summary>
static class IniTests
{
    public static void Run(CheckFn check, Action<string> section)
    {
        section("エフェクト設定のini保存・復元");
        string dir = Path.Combine(Path.GetTempPath(), "lokuonx-ini-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "a.ini");
            var fx = new EffectSetting { EqOn = true, HpfHz = 85, Bands = new double[] { 3.5, 0, -4, 1.5, 0, 0, 2, 0, -6, 12 }, EchoOn = true, EchoMs = 420, EchoFeedback = 0.5, EchoMix = 0.4, ReverbOn = true, ReverbRoom = 0.8, ToRecording = true, ToOutput = false, Bypass = true, GateOn = true, GateThresholdDb = -52 };
            fx.Params[1].Freq = 1500; fx.Params[1].Db = -5; fx.Params[1].Q = 3; fx.ParamOn = true;
            var ofx = new OutputFxSetting { EqOn = true, HpfHz = 80, LimiterOn = true, CeilingDb = -2.5, ReleaseMs = 300, Bypass = true };
            ofx.Bands[3] = -4;
            var ini = new IniFile(path);
            fx.WriteTo(new IniSection(ini, "Input1"), "Fx", false);
            ofx.WriteTo(new IniSection(ini, "Output1"), "Fx", true);
            ini.Save();
            var r = new IniFile(path);
            var rf = EffectSetting.ReadFrom(new IniSection(r, "Input1"), "Fx");
            check(rf.SameSound(fx) && rf.ToRecording && !rf.ToOutput && rf.Params[1].Freq == 1500, "入力のエフェクト設定が保存・復元される");
            var ro = OutputFxSetting.ReadFrom(new IniSection(r, "Output1"), "Fx");
            check(ro.SameSound(ofx) && ro.CeilingDb == -2.5, "出力のエフェクト設定が保存・復元される");
            var empty = EffectSetting.ReadFrom(new IniSection(r, "Nothing"), "Fx");
            check(empty.IsDefault, "項目が無いときは初期値（かけない）で読める");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
