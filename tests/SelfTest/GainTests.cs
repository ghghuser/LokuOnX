using Recorder.Core;

/// <summary>入力のゲインを左右別にできること（M15。GainMath.Channels と、DeviceStream が左右それぞれに掛けること）のテスト。</summary>
static class GainTests
{
    public static void Run(CheckFn check, Action<string> section)
    {
        section("L/R別ゲイン（M15）");
        {
            var a = GainMath.Channels(0, 3, 9, 0.5);
            check(a.L == 3 && a.R == 3, "一括: 左右とも同じゲイン");
            var b = GainMath.Channels(1, -6, 4, 0.5);
            check(b.L == -6 && b.R == 4, "左右それぞれ: 左・右の値がそのまま");
            var c = GainMath.Channels(2, 0, 0, 0);
            check(c.L == 0 && c.R == 0, "バランス: 中央なら左右とも音量のまま");
            var d = GainMath.Channels(2, 0, 0, 0.5);
            check(d.R == 0 && Math.Abs(d.L - -15) < 1e-9, "バランス: 右へ半分ずらすと、左が半分（0→−30 の中間）まで下がる", $"{d}");
            var e = GainMath.Channels(2, 6, 0, -1);
            check(e.L == 6 && GainMath.IsMuted(e.R), "バランス: 左いっぱいなら右がミュート");
            var f = GainMath.Channels(2, Settings.MinGainDb, 0, 0.5);
            check(GainMath.IsMuted(f.L) && GainMath.IsMuted(f.R), "バランス: 音量がミュートなら両方ミュート");
            var s = new InputSetting("capture:A", 0, true, 1, -3, 0).Gains;
            check(s.L == 0 && s.R == -3, "InputSetting.Gains が動かし方に従う");
        }
        {
            var prov = new FakeProvider();
            using var hub = new InputHub(prov, () => true);
            var e = hub.Acquire("capture:A", new ChGain(0, Settings.MinGainDb));   // 右だけミュート
            var tap = new InputTap();
            e.Stream.AddTap(tap);
            prov.Sources[0].Feed(0.25f, 480);
            var buf = tap.Buffer;
            var o = new float[480 * 2];
            buf!.Read(o, 0, 480);
            check(Math.Abs(o[10] - 0.25f) < 0.01f && o[11] == 0f, "左は通り、右はミュートされる", $"L={o[10]} R={o[11]}");
            hub.SetGain("capture:A", 0);   // double からそのまま（左右同じ）
            prov.Sources[0].Feed(0.25f, 480);
            prov.Sources[0].Feed(0.25f, 480);
            var o2 = new float[960 * 2];
            buf.Read(o2, 0, 960);
            check(Math.Abs(o2[960 * 2 - 1] - 0.25f) < 0.01f && Math.Abs(o2[960 * 2 - 2] - 0.25f) < 0.01f, "あとから左右同じゲインに戻すと、右も通る");
        }
    }
}
