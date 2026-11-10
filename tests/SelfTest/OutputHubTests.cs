using Recorder.Core;

/// <summary>複数出力（OutputHub）・入力ごとの送り・出力の設定（OutputConfig）のテスト（M11-3）。デバイスは偽物（Fakes.cs）。</summary>
static class OutputHubTests
{
    static float PumpLast(FakeSource src, OutputRenderer[] rends, int n, out float[] lasts)
    {
        var o = new float[480 * 2];
        lasts = new float[rends.Length];
        for (int k = 0; k < n; k++)
        {
            src.Feed(0.3f, 480);
            for (int i = 0; i < rends.Length; i++)
            {
                rends[i].Render(o, 0, 480);
                lasts[i] = o[400];
            }
        }
        return lasts.Length > 0 ? lasts[0] : 0;
    }

    public static void Run(CheckFn check, Action<string> section)
    {
        section("出力の設定（OutputConfig）");
        {
            check(OutputConfig.CleanName("  abcdefghijklmnop ", "x") == "abcdefghijkl", "名前は前後の空白を除き12文字まで");
            check(OutputConfig.CleanName("   ", "ミックス1") == "ミックス1", "空の名前は既定に戻る");
            check(OutputConfig.InputLabel(new[] { "", "マイク" }, 0) == "入力1" && OutputConfig.InputLabel(new[] { "", "マイク" }, 1) == "マイク", "入力の名前は未設定なら「入力N」");
            var c = new OutputConfig("", "SPK", 99, new[] { new OutSend(true, -99) }).Normalize(2);
            check(c.Name == "ミックス3" && c.VolumeDb == Settings.MaxGainDb && c.Sends.Count == OutputConfig.InputSlots && c.SendAt(0).Db == Settings.MinGainDb && c.SendAt(3).On, "範囲外の値と足りない送りを直す");
            var w = OutputConfig.Default(0).WithSend(2, new OutSend(false, 3));
            check(!w.SendAt(2).On && w.SendAt(2).Db == 3 && w.SendAt(0).On, "送りの1つだけ変えた設定を作れる");
        }

        section("複数の出力（OutputHub）");
        {
            var ins = new FakeProvider();
            using var hub = new InputHub(ins, () => true);
            var outs = new FakeOutProvider();
            using var oh = new OutputHub(hub, outs, ins);
            oh.SetInputs(new[] { new InputSetting("capture:A", 0) });
            var a = oh.Add()!;
            var b = oh.Add()!;
            oh.Add(); oh.Add();
            check(oh.Count == 4 && oh.Add() == null, "出力は最大4台");
            oh.EnsureCount(2);
            check(oh.Count == 2, "台数を合わせられる");

            a.DeviceId = "SPK"; b.DeviceId = "SPK";
            a.On = true;
            check(EngineTests.WaitFor(() => a.Running), "1台目が始まる", a.Error);
            b.On = true;
            check(EngineTests.WaitFor(() => b.Running), "2台目が始まる", b.Error);
            check(hub.OpenCount == 1 && ins.Sources.Count == 1 && outs.Devices.Count == 2, "同じ入力は1回だけ開き、出力デバイスは2つ開く");

            var src = ins.Sources[0];
            var rends = new[] { outs.Devices[0].Renderer!, outs.Devices[1].Renderer! };
            for (int k = 0; k < 20; k++) src.Feed(0.3f, 480);
            PumpLast(src, rends, 6, out var v);
            check(Math.Abs(v[0] - 0.3f) < 0.02f && Math.Abs(v[1] - 0.3f) < 0.02f, "どちらの出力にも同じ入力が届く", $"{v[0]} {v[1]}");

            b.SetSends(new[] { new OutSend(false, 0) });
            PumpLast(src, rends, 8, out v);
            check(Math.Abs(v[0] - 0.3f) < 0.02f && v[1] == 0f, "送りをオフにした出力だけ無音になる（もう一方は鳴る）", $"{v[0]} {v[1]}");

            b.SetSends(new[] { new OutSend(true, -6.0206) });
            PumpLast(src, rends, 30, out v);
            check(Math.Abs(v[1] - 0.15f) < 0.02f && Math.Abs(v[0] - 0.3f) < 0.02f, "入力×出力ごとの音量（-6dBで半分）。開き直さずに効く", $"{v[0]} {v[1]}");
            check(outs.Devices.Count == 2 && !outs.Devices[1].Disposed, "送りの変更で出力デバイスは開き直さない");

            oh.StopAll();
            check(EngineTests.WaitFor(() => !a.Running && !b.Running) && !oh.AnyOn && oh.CanResume, "すべて停止できる（再開用に覚える）");
            check(EngineTests.WaitFor(() => hub.OpenCount == 0), "停止すると入力も手放す");
            oh.ResumeAll();
            check(EngineTests.WaitFor(() => a.Running && b.Running) && !oh.CanResume, "すべて再開すると、停止前にオンだった出力が戻る");

            oh.RemoveAt(1);
            check(oh.Count == 1 && EngineTests.WaitFor(() => outs.Devices.Count(d => !d.Disposed) == 1), "出力を削除すると、その出力デバイスを閉じる");
            check(a.Running, "残った出力は動き続ける");

            oh.StopAll(remember: false);
            check(EngineTests.WaitFor(() => !a.Running) && !oh.CanResume, "覚えずに停止すると、すべて再開はできない");
        }
    }
}
