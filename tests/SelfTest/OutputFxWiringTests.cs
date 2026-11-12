using System.Text.Json;
using Recorder.Core;

/// <summary>出力ごとのエフェクト（M11-5）の配線: 出力カードの設定・出力エンジン・描画（全体音量のあと・切り詰めの前）・ini項目・JSON。デバイスは偽物（Fakes.cs）。</summary>
static class OutputFxWiringTests
{
    /// <summary>入力へ一定の音（value）を流し続け、出力の最後の数ブロックの最大値を返す（リミッターが落ち着くまで流す）。</summary>
    static float Steady(OutputRenderer rend, FakeSource src, float value)
    {
        var o = new float[480 * 2];
        for (int k = 0; k < 20; k++) src.Feed(value, 480);
        float peak = 0;
        for (int k = 0; k < 40; k++)
        {
            rend.Render(o, 0, 480);
            if (k >= 30) peak = Math.Max(peak, o.Max(v => Math.Abs(v)));
            src.Feed(value, 480);
        }
        return peak;
    }

    static (FakeProvider ins, FakeOutProvider outs, InputHub hub, OutputEngine eng) Make()
    {
        var ins = new FakeProvider();
        var hub = new InputHub(ins, () => true);
        var outs = new FakeOutProvider();
        var eng = new OutputEngine(hub, outs, ins);
        eng.SetInputs(new[] { new InputSetting("capture:A", 0) });
        eng.DeviceId = "SPK";
        return (ins, outs, hub, eng);
    }

    public static void Run(CheckFn check, Action<string> section)
    {
        section("出力ごとのエフェクト: 設定（OutputConfig）");
        {
            var plain = OutputConfig.Default(0).Normalize(0);
            check(plain.Fx == null && plain.FxCopy().IsDefault, "何も設定していない出力は、エフェクトを持たない（null）");

            var cfg = new OutputConfig("A", "SPK", 0, OutputConfig.Default(0).Sends, new OutputFxSetting { LimiterOn = true, CeilingDb = -99, ReleaseMs = 99999 });
            var n = cfg.Normalize(0);
            check(n.Fx != null && n.Fx.LimiterOn && n.Fx.CeilingDb == OutputFxSetting.CeilingMinDb && n.Fx.ReleaseMs == OutputFxSetting.ReleaseMaxMs,
                "範囲外の値は範囲内へ直る", $"{n.Fx?.CeilingDb}/{n.Fx?.ReleaseMs}");
            check((n with { Name = "B" }).Fx!.LimiterOn && n.WithSend(1, new OutSend(false, 0)).Fx!.LimiterOn, "名前・送りを変えても、エフェクトは残る");
            var copy = n.FxCopy();
            copy.LimiterOn = false;
            check(n.Fx!.LimiterOn, "FxCopy は元とは別のコピー（編集しても元は変わらない）");
            check(new OutputConfig("A", "", 0, plain.Sends, new OutputFxSetting()).Normalize(0).Fx == null, "初期値と同じ設定は null に戻る");
        }

        section("出力ごとのエフェクト: ini項目・JSON");
        {
            string dir = Path.Combine(Path.GetTempPath(), "lokuonx-ofx-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var path = Path.Combine(dir, "a.ini");
                var ini = new IniFile(path);
                new OutputFxSetting().WriteTo(new IniSection(ini, "Outputs"), "Fx1_", false);
                ini.Save();
                var written = File.ReadAllLines(path).Where(l => l.Contains('=')).Select(l => l.Split('=')[0].Trim()).OrderBy(x => x).ToList();
                var expected = OutputFxSetting.IniKeys.Select(k => "Fx1_" + k).OrderBy(x => x).ToList();
                check(written.SequenceEqual(expected), "IniKeys は WriteTo が書く項目と一致する", string.Join(",", written.Except(expected).Concat(expected.Except(written))));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }

            var fx = new OutputFxSetting { EqOn = true, HpfHz = 90, LimiterOn = true, CeilingDb = -3, ReleaseMs = 250 };
            fx.Bands[2] = 4.5; fx.Params[0].Db = -3; fx.ParamOn = true;
            var json = JsonSerializer.Serialize(new List<OutputFxSetting?> { fx, null });
            var back = JsonSerializer.Deserialize<List<OutputFxSetting?>>(json)!;
            back[0]!.Normalize();
            check(back[0]!.SameSound(fx) && back[1] == null, "プリセットのJSONで往復できる（無い出力は null のまま＝かけない）");
            check(!json.Contains("IsActive") && !json.Contains("HasAny") && !json.Contains("IsDefault"), "計算で出る項目（IsActive など）はJSONに書かない");
        }

        section("出力ごとのエフェクト: 出力への反映");
        {
            var (ins, outs, hub, eng) = Make();
            using (hub) using (eng)
            {
                // 動かす前に設定しておく（開くときに渡される）。全体音量 +6dB のあとに、上限 -12dB のリミッターが掛かる
                eng.Fx = new OutputFxSetting { LimiterOn = true, CeilingDb = -12 };
                eng.VolumeDb = 6.0206;
                eng.On = true;
                check(EngineTests.WaitFor(() => eng.Running), "オンにすると出力が始まる", $"state={eng.State} err={eng.Error}");
                var rend = outs.Devices[0].Renderer!;
                var src = ins.Sources[0];
                float limited = Steady(rend, src, 0.3f);   // 0.3 × 2 = 0.6 → 上限 0.251
                check(Math.Abs(limited - 0.2512f) < 0.01f, "全体音量のあとにリミッターが掛かり、上限（-12dB）に収まる", $"peak={limited}");

                eng.Fx = new OutputFxSetting();
                float plain = Steady(rend, src, 0.3f);
                check(Math.Abs(plain - 0.6f) < 0.02f, "動作中にエフェクトを外すと、すぐ素の音に戻る（開き直さない）", $"peak={plain}");
                check(outs.Devices.Count == 1 && !outs.Devices[0].Disposed, "エフェクトを変えても出力デバイスは開き直さない");

                eng.Fx = new OutputFxSetting { LimiterOn = true, CeilingDb = -12, Bypass = true };
                float bypassed = Steady(rend, src, 0.3f);
                check(Math.Abs(bypassed - 0.6f) < 0.02f, "バイパス中は設定が残っていても素の音", $"peak={bypassed}");

                eng.Fx = new OutputFxSetting { HpfHz = 400 };
                float hpf = Steady(rend, src, 0.3f);
                check(hpf < 0.03f, "ローカットも出力に掛かる（直流は消える）", $"peak={hpf}");
                check(Math.Abs(eng.Fx.HpfHz - 400) < 0.001, "エンジンが持つ設定を読み返せる");
            }
        }

        section("出力ごとのエフェクト: 出力ごとに独立");
        {
            var ins = new FakeProvider();
            using var hub = new InputHub(ins, () => true);
            var outs = new FakeOutProvider();
            using var hubOut = new OutputHub(hub, outs, ins);
            var a = hubOut.Add()!; var b = hubOut.Add()!;
            foreach (var e in new[] { a, b }) e.SetInputs(new[] { new InputSetting("capture:A", 0) });
            a.DeviceId = "SPK"; b.DeviceId = "default";   // どちらのデバイスが a か分かるよう、出力先を変えておく
            a.Fx = new OutputFxSetting { HpfHz = 400 };   // a にだけローカット
            a.On = true; b.On = true;
            check(EngineTests.WaitFor(() => a.Running && b.Running && outs.Devices.Count == 2), "2つの出力が動く", $"{a.State}/{b.State}");
            var src = ins.Sources[0];
            var ra = outs.Devices.First(d => d.Id == "SPK").Renderer!; var rb = outs.Devices.First(d => d.Id == "default").Renderer!;
            var o = new float[480 * 2];
            for (int k = 0; k < 20; k++) src.Feed(0.3f, 480);
            float pa = 0, pb = 0;
            for (int k = 0; k < 40; k++)
            {
                ra.Render(o, 0, 480); if (k >= 30) pa = Math.Max(pa, o.Max(v => Math.Abs(v)));
                rb.Render(o, 0, 480); if (k >= 30) pb = Math.Max(pb, o.Max(v => Math.Abs(v)));
                src.Feed(0.3f, 480);
            }
            check(pa < 0.03f && Math.Abs(pb - 0.3f) < 0.02f, "エフェクトは、設定した出力にだけ掛かる（別の出力は素の音）", $"a={pa} b={pb}");
        }
    }
}
