using System.Diagnostics;
using Recorder.Core;

/// <summary>出力エンジン（OutputEngine）と、出力先の検査（OutputGuard）のテスト。デバイスは偽物（Fakes.cs）。</summary>
static class EngineTests
{
    internal static bool WaitFor(Func<bool> cond, int ms = 4000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            if (cond()) return true;
            Thread.Sleep(20);
        }
        return cond();
    }

    public static void Run(CheckFn check, Action<string> section)
    {
        section("出力先の検査（ループ防止）");
        {
            check(OutputGuard.IsLoop("SPK", null, new[] { "render:SPK" }), "入力のPC内部音（🔊）と同じデバイスは出力先にできない");
            check(OutputGuard.IsLoop("spk", null, new[] { "render:SPK" }), "デバイスIDの大文字小文字は区別しない");
            check(!OutputGuard.IsLoop("SPK", null, new[] { "capture:MIC" }), "マイク入力なら同じIDでもループではない");
            check(!OutputGuard.IsLoop("OTHER", null, new[] { "render:SPK" }), "別のデバイスなら出力できる");
            check(OutputGuard.IsLoop("default", "render:SPK", new[] { "render:SPK" }), "「既定の出力」は、実際のデバイスに置き換えて判定する");
            check(!OutputGuard.IsLoop("default", null, new[] { "render:SPK" }), "既定が分からないときは判定しない");
        }

        section("出力エンジン");
        {
            var ins = new FakeProvider();
            using var hub = new InputHub(ins, () => true);
            var outs = new FakeOutProvider();
            using var eng = new OutputEngine(hub, outs, ins);
            eng.SetInputs(new[] { new InputSetting("capture:A", 0) });
            eng.DeviceId = "SPK";
            Thread.Sleep(700);
            check(!eng.Running && hub.OpenCount == 0 && outs.Devices.Count == 0, "「出力する」がオフのあいだは、何も開かない");

            eng.On = true;
            check(WaitFor(() => eng.Running), "オンにすると出力が始まる", $"state={eng.State} err={eng.Error}");
            check(eng.State == "出力中" && hub.OpenCount == 1 && outs.Devices.Count == 1 && eng.Error.Length == 0, "入力を1回だけ開き、出力デバイスを1つ開く");

            var rend = outs.Devices[0].Renderer!;
            var o = new float[480 * 2];
            var src = ins.Sources[0];
            for (int k = 0; k < 20; k++) src.Feed(0.3f, 480);
            float peak = 0;
            for (int k = 0; k < 6; k++) { rend.Render(o, 0, 480); peak = Math.Max(peak, o.Max()); src.Feed(0.3f, 480); }
            check(peak > 0.25f, "入力の音が出力デバイスへ届く", $"peak={peak}");

            eng.VolumeDb = -6.0206;
            float peak2 = 0;
            for (int k = 0; k < 4; k++) { rend.Render(o, 0, 480); src.Feed(0.3f, 480); }
            for (int k = 0; k < 4; k++) { rend.Render(o, 0, 480); peak2 = Math.Max(peak2, o.Max()); src.Feed(0.3f, 480); }
            check(Math.Abs(peak2 - 0.15f) < 0.02f, "出力の全体音量を動作中に変えられる（-6dBで半分）", $"peak={peak2}");
            var (pl, pr, _) = eng.ReadPeaks();
            check(pl > 0.1f && pr > 0.1f, "出力のピーク（メーター用）が取れる", $"{pl}/{pr}");

            // 入力を変えても出力デバイスは開き直さない（複製先の付け外しだけ）
            eng.SetInputs(new[] { new InputSetting("capture:B", 0) });
            check(WaitFor(() => hub.OpenCount == 1 && ins.Sources.Count == 2 && eng.Running), "入力が変わると、入力だけ入れ替える", $"open={hub.OpenCount} state={eng.State}");
            check(outs.Devices.Count == 1 && !outs.Devices[0].Disposed, "出力デバイスは開き直さない（出力が途切れない）");
            for (int k = 0; k < 20; k++) ins.Sources[1].Feed(0.3f, 480);
            float peakB = 0;
            for (int k = 0; k < 6; k++) { rend.Render(o, 0, 480); peakB = Math.Max(peakB, o.Max()); ins.Sources[1].Feed(0.3f, 480); }
            check(peakB > 0.25f, "入れ替えた入力の音が同じ出力へ届く", $"peak={peakB}");

            // 2台 → 1台に減らしても、残った入力は途切れず、出力デバイスも開き直さない
            eng.SetInputs(new[] { new InputSetting("capture:B", 0), new InputSetting("capture:C", 0) });
            check(WaitFor(() => hub.OpenCount == 2 && ins.Sources.Count == 3), "入力を足すと、足した入力だけ開く", $"open={hub.OpenCount}");
            var sourceB = ins.Sources[1];
            eng.SetInputs(new[] { new InputSetting("capture:B", 0) });
            check(WaitFor(() => hub.OpenCount == 1), "入力を減らすと、外した入力だけ手放す", $"open={hub.OpenCount}");
            check(outs.Devices.Count == 1 && !outs.Devices[0].Disposed && sourceB.StopCount == 0 && ins.Sources.Count == 3, "残った入力は開いたまま、出力デバイスも開き直さない");

            // ループ防止
            eng.SetInputs(new[] { new InputSetting("render:SPK", 0) });
            check(WaitFor(() => !eng.Running && eng.Error.Contains("音が回")), "出力先と同じデバイスの🔊入力は、赤文字のエラーで止める", $"state={eng.State} err={eng.Error}");
            check(hub.OpenCount == 0, "ループのときは入力も開かない");
            eng.DeviceId = "default";
            Thread.Sleep(700);
            check(!eng.Running && eng.Error.Contains("音が回"), "「既定の出力」を選んでいても、実際のデバイスで判定する");
            eng.SetInputs(new[] { new InputSetting("render:OTHER", 0) });
            check(WaitFor(() => eng.Running), "ループでない入力にすれば出力が始まる", $"state={eng.State} err={eng.Error}");

            // 入力が無い・出力先が無い
            eng.SetInputs(Array.Empty<InputSetting>());
            check(WaitFor(() => !eng.Running && eng.Error.Contains("入力デバイス")), "入力が選ばれていないとき、理由を出す", eng.Error);
            eng.SetInputs(new[] { new InputSetting("capture:A", 0) });
            eng.DeviceId = "";
            check(WaitFor(() => !eng.Running && eng.Error.Contains("出力先")), "出力先が未選択のとき、理由を出す", eng.Error);
            eng.DeviceId = "SPK";
            check(WaitFor(() => eng.Running && eng.Error.Length == 0), "直すと再び出力が始まり、エラーが消える", $"state={eng.State} err={eng.Error}");

            // オフ
            eng.On = false;
            check(WaitFor(() => !eng.Running && hub.OpenCount == 0), "オフにすると、出力デバイスを閉じて入力も手放す", $"open={hub.OpenCount}");
            check(outs.Devices[^1].Disposed && eng.State == "停止中", "停止中と表示される");

            // 出力デバイスが外れた
            eng.On = true;
            check(WaitFor(() => eng.Running), "もう一度オンにできる");
            outs.Devices[^1].Failed = true;
            outs.Devices[^1].LastError = "デバイスが削除されました";
            check(WaitFor(() => eng.State == "再接続待ち" && !eng.Running), "出力デバイスが外れたら、再接続待ちになる（止まらずに待つ）", $"state={eng.State}");
            check(eng.Error.Contains("つなぎ直し") && hub.OpenCount == 0, "赤文字で知らせ、入力は手放す（録音には影響しない）", eng.Error);
            eng.On = false;
            check(WaitFor(() => eng.State == "停止中" && eng.Error.Length == 0), "オフにすると、待機もエラーも解除される", $"state={eng.State} err={eng.Error}");

            // 出力デバイスを開けない
            outs.NextStartError = "出力デバイスを開けません: テスト";
            eng.On = true;
            check(WaitFor(() => eng.State == "再接続待ち"), "開けないときも、再接続待ちになる", $"state={eng.State}");
            check(eng.Error.Contains("開けません") && hub.OpenCount == 0 && outs.Devices[^1].Disposed, "開けなかったデバイスと入力は片付ける", eng.Error);
            eng.On = false;
        }
    }
}
