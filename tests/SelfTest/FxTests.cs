using System.Text.Json;
using Recorder.Core;

/// <summary>入力ごとのエフェクト（M11-4）: 録音・出力への反映先、ピークは掛ける前、設定の保存・プロファイルの互換。</summary>
static class FxTests
{
    static float Tail(float[] a, int frames, int ch, int lastFrames)
    {
        float m = 0;
        for (int i = (frames - lastFrames) * ch; i < frames * ch; i++) m = Math.Max(m, Math.Abs(a[i]));
        return m;
    }

    /// <summary>直流 0.5 を流し込み、(録音側の最後の最大値, 出力用の複製先の最後の最大値, ピーク) を返す。ローカット400Hzは直流を消すので、エフェクトが掛かった側は 0 に近づく。</summary>
    static (float Rec, float Tap, float Peak) Run1(EffectSetting fx, bool attach = true)
    {
        var prov = new FakeProvider();
        using var hub = new InputHub(prov, () => true);
        hub.SetEffects(new[] { ("capture:A", fx) });
        var e = hub.Acquire("capture:A", 0);
        var tap = new InputTap();
        e.Stream.AddTap(tap);
        if (attach) e.Stream.Attach(48000, 2, hub.Now());
        const int N = 12 * 480;
        for (int i = 0; i < 12; i++) prov.Sources[0].Feed(0.5f, 480);
        var o = new float[N * 2];
        int got = tap.Buffer!.Read(o, 0, N);
        float tapTail = Tail(o, got, 2, 480);
        float recTail = -1;
        if (attach)
        {
            // 時計との微調整で、末尾がわずかに無音で埋まることがあるので、最後の480フレームは見ない
            var mix = new float[N * 2];
            e.Stream.MixInto(mix, N, new float[N * 2]);
            recTail = Tail(mix, N - 480, 2, 480);
        }
        float peak = e.Stream.TakePeak();
        hub.Release(e);
        return (recTail, tapTail, peak);
    }

    static EffectSetting Hpf(bool toRec, bool toOut, bool bypass = false) =>
        new EffectSetting { HpfHz = 400, ToRecording = toRec, ToOutput = toOut, Bypass = bypass };

    public static void Run(CheckFn check, Action<string> section)
    {
        section("入力ごとのエフェクト: 録音・出力への反映先");
        {
            var d = Run1(new EffectSetting());
            check(Math.Abs(d.Rec - 0.5f) < 0.01f && Math.Abs(d.Tap - 0.5f) < 0.01f, "エフェクトなしは、録音も出力も素の音", $"rec={d.Rec} tap={d.Tap}");

            var a = Run1(Hpf(false, true));
            check(Math.Abs(a.Rec - 0.5f) < 0.01f, "既定（録音に反映しない）: 録音は素の音のまま", $"rec={a.Rec}");
            check(a.Tap < 0.02f, "既定（出力に反映する）: 出力用の複製先にはエフェクト後の音が届く", $"tap={a.Tap}");
            check(Math.Abs(a.Peak - 0.5f) < 0.01f, "ピーク（メーター・無音検出用）は掛ける前の音で測る", $"peak={a.Peak}");

            var b = Run1(Hpf(true, false));
            check(b.Rec < 0.02f && Math.Abs(b.Tap - 0.5f) < 0.01f, "録音に反映だけオン: 録音はエフェクト後、出力は素の音", $"rec={b.Rec} tap={b.Tap}");
            check(Math.Abs(b.Peak - 0.5f) < 0.01f, "録音に反映しても、ピークは掛ける前の音", $"peak={b.Peak}");

            var c = Run1(Hpf(true, true));
            check(c.Rec < 0.02f && c.Tap < 0.02f, "両方オン: 録音も出力もエフェクト後", $"rec={c.Rec} tap={c.Tap}");

            var f = Run1(Hpf(false, false));
            check(Math.Abs(f.Rec - 0.5f) < 0.01f && Math.Abs(f.Tap - 0.5f) < 0.01f, "両方オフ: どちらも素の音", $"rec={f.Rec} tap={f.Tap}");

            var g = Run1(Hpf(true, true, bypass: true));
            check(Math.Abs(g.Rec - 0.5f) < 0.01f && Math.Abs(g.Tap - 0.5f) < 0.01f, "バイパスは素の音（反映先がオンでも）", $"rec={g.Rec} tap={g.Tap}");

            var h = Run1(Hpf(true, true), attach: false);
            check(h.Tap < 0.02f, "録音につながっていなくても、出力にはエフェクトが掛かる", $"tap={h.Tap}");
        }

        section("入力ごとのエフェクト: 設定の差し替え（InputHub.SetEffects）");
        {
            var prov = new FakeProvider();
            using var hub = new InputHub(prov, () => true);
            var e = hub.Acquire("capture:A", 0);
            var tap = new InputTap();
            e.Stream.AddTap(tap);
            var o = new float[4800 * 2];
            prov.Sources[0].Feed(0.5f, 4800);
            tap.Buffer!.Read(o, 0, 4800);
            check(Tail(o, 4800, 2, 480) > 0.49f, "最初はエフェクトなし");
            hub.SetEffects(new[] { ("capture:A", Hpf(false, true)) });
            prov.Sources[0].Feed(0.5f, 4800);
            tap.Buffer.Read(o, 0, 4800);
            check(Tail(o, 4800, 2, 480) < 0.02f, "開いている入力へ、あとから設定しても効く");
            hub.SetEffects(Array.Empty<(string, EffectSetting)>());
            prov.Sources[0].Feed(0.5f, 4800);
            tap.Buffer.Read(o, 0, 4800);
            check(Tail(o, 4800, 2, 480) > 0.49f, "設定から外れたデバイスは「かけない」に戻る");
            hub.Release(e);
            hub.SetEffects(new[] { ("capture:A", Hpf(false, true)) });
            var e2 = hub.Acquire("capture:A", 0);
            var tap2 = new InputTap();
            e2.Stream.AddTap(tap2);
            prov.Sources[^1].Feed(0.5f, 4800);
            var o2 = new float[4800 * 2];
            tap2.Buffer!.Read(o2, 0, 4800);
            check(Tail(o2, 4800, 2, 480) < 0.02f, "設定してから開いた入力にも、開いたときに渡される");
            hub.Release(e2);
        }

        section("入力ごとのエフェクト: コピー・保存・プロファイル");
        {
            var src = new EffectSetting { GateOn = true, GateThresholdDb = -50, HpfHz = 120, EqOn = true, EchoOn = true, EchoMs = 250, ToRecording = false, ToOutput = true };
            src.Bands[3] = 6;
            src.Params[2].Db = -4;
            src.ParamOn = true;
            var dst = new EffectSetting { ToRecording = true, ToOutput = false, ReverbOn = true };
            dst.CopySoundFrom(src);
            check(dst.SameSound(src) && dst.ToRecording && !dst.ToOutput, "貼り付けは効果だけ写し、録音・出力への反映先は変えない");
            check(!dst.ReverbOn, "貼り付けで、元に無い効果（リバーブ）はオフになる");
            src.Bands[3] = 0;
            check(dst.Bands[3] == 6, "貼り付けた設定は、元とは別のコピー");

            // iniの項目名の一覧（IniKeys）が WriteTo と食い違っていないこと（初期値に戻したとき、ini から消し忘れない）
            string dir = Path.Combine(Path.GetTempPath(), "lokuonx-fx-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var path = Path.Combine(dir, "a.ini");
                var ini = new IniFile(path);
                new EffectSetting().WriteTo(new IniSection(ini, "InputFx"), "Fx1_", false);
                ini.Save();
                var written = File.ReadAllLines(path).Where(l => l.Contains('=')).Select(l => l.Split('=')[0].Trim()).OrderBy(x => x).ToList();
                var expected = EffectSetting.IniKeys.Select(k => "Fx1_" + k).OrderBy(x => x).ToList();
                check(written.SequenceEqual(expected), "IniKeys は WriteTo が書く項目と一致する", string.Join(",", written.Except(expected).Concat(expected.Except(written))));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }

            // プロファイル（JSON）: 4入力ぶんの往復。反映先も含む
            var list = new List<EffectSetting> { dst, new EffectSetting(), new EffectSetting { ReverbOn = true, ToRecording = true }, new EffectSetting() };
            var json = JsonSerializer.Serialize(list);
            var back = JsonSerializer.Deserialize<List<EffectSetting>>(json)!;
            foreach (var x in back) x.Normalize();
            check(back.Count == 4 && back[0].SameSound(dst) && back[0].ToRecording && !back[0].ToOutput && back[2].ReverbOn && back[2].ToRecording, "プロファイルのJSONで往復できる（反映先も含む）");
            check(!json.Contains("IsActive") && !json.Contains("HasAny") && !json.Contains("IsDefault"), "計算で出る項目（IsActive など）はJSONに書かない");

            // 壊れた・足りない値でも読める（範囲内に直る）
            var broken = JsonSerializer.Deserialize<List<EffectSetting>>("[{\"GateThresholdDb\":99,\"Bands\":[100],\"EchoMs\":-5},{}]")!;
            foreach (var x in broken) x.Normalize();
            check(broken[0].GateThresholdDb == -10 && broken[0].Bands.Length == EqBands.Count && broken[0].Bands[0] == EqBands.MaxDb && broken[0].EchoMs == 20, "範囲外・足りない値は範囲内に直る");
            check(broken[1].IsDefault, "空の項目は初期値（かけない）");
        }
    }
}
