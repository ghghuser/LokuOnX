using Recorder.Core;

/// <summary>入力の持ち主（InputHub）・出力用の複製先（タップ）・録音の開始／終了で入力を開き直さないこと（M11-2 の A2 方式）のテスト。</summary>
static class HubTests
{
    static MixedSource Mix(InputHub hub, int deviceChannels, out Func<long> frames, out Func<float> peak)
    {
        var infos = new[] { new AudioDeviceInfo("capture:A", "A", false, 48000, deviceChannels) };
        var ms = new MixedSource(hub, new[] { new MixInput("capture:A", "A", 0) }, infos);
        var gate = new object();
        long total = 0;
        float pk = 0;
        ms.DataAvailable += (b, n) =>
        {
            var f = new float[n / 4];
            Buffer.BlockCopy(b, 0, f, 0, n);
            lock (gate)
            {
                total += f.Length / deviceChannels;
                foreach (var v in f) pk = Math.Max(pk, Math.Abs(v));
            }
        };
        frames = () => { lock (gate) return total; };
        peak = () => { lock (gate) return pk; };
        return ms;
    }

    public static void Run(CheckFn check, Action<string> section)
    {
        section("入力の持ち主（InputHub）");
        {
            var prov = new FakeProvider();
            using var hub = new InputHub(prov, () => true);
            var a = hub.Acquire("capture:A", 0);
            var b = hub.Acquire("capture:A", 0);
            check(ReferenceEquals(a, b) && hub.OpenCount == 1 && prov.Sources.Count == 1, "同じ入力は共有する（開くのは1回）");
            check(a.Input.IsConnected, "つながっている");
            hub.Release(a);
            check(hub.OpenCount == 1 && prov.Sources[0].StopCount == 0, "1人が手放しても開いたまま");
            hub.Release(b);
            check(hub.OpenCount == 0 && prov.Sources[0].StopCount >= 1, "全員が手放したら閉じる");
            var c = hub.Acquire("capture:A", 0);
            check(prov.Sources.Count == 2, "閉じたあとの取得は開き直す");
            hub.Release(c);
        }

        section("出力用の複製先（タップ）");
        {
            var prov = new FakeProvider();
            using var hub = new InputHub(prov, () => true);
            var e = hub.Acquire("capture:A", 6.0206);
            var tap = new InputTap();
            e.Stream.AddTap(tap);
            prov.Sources[0].Feed(0.25f, 480);
            var buf = tap.Buffer;
            check(buf != null && buf.AvailableFrames == 480 && buf.Channels == 2 && buf.SampleRate == 48000, "届いた音がタップに入る");
            var o = new float[480 * 2];
            buf!.Read(o, 0, 480);
            check(Math.Abs(o[10] - 0.5f) < 0.01f, "タップには入力ゲイン（+6dB）を掛けたあとの音が入る", $"{o[10]}");
            hub.SetGain("capture:A", Settings.MinGainDb);
            prov.Sources[0].Feed(0.25f, 480);
            prov.Sources[0].Feed(0.25f, 480);
            var o2 = new float[960 * 2];
            buf.Read(o2, 0, 960);
            bool tailZero = true;
            for (int i = 480 * 2; i < 960 * 2; i++) if (o2[i] != 0f) tailZero = false;
            check(tailZero, "ミュート（最小ゲイン）はタップにも効く");
            e.Stream.RemoveTap(tap);
            check(tap.Buffer == null, "タップを外すとバッファも手放す");
            hub.Release(e);
        }

        section("録音の開始・終了で入力を開き直さない");
        {
            var prov = new FakeProvider();
            using var hub = new InputHub(prov, () => true);
            var outEntry = hub.Acquire("capture:A", 0);          // 出力が先に開いている
            var tap = new InputTap();
            outEntry.Stream.AddTap(tap);
            var src = prov.Sources[0];
            src.Feed(0.9f, 4800);                                  // 録音の前にたまった、大きな古い音
            var before = tap.Buffer;

            var ms = Mix(hub, 2, out var frames, out var peak);
            var t0 = hub.Now();
            ms.Start();
            for (int i = 0; i < 40; i++) { src.Feed(0.3f, 480); Thread.Sleep(10); }
            var stopT = hub.Now();
            ms.Stop();
            check(prov.Sources.Count == 1 && src.StopCount == 0, "録音の開始・終了で入力を開き直さない（出力用に開いたまま）");
            check(ReferenceEquals(tap.Buffer, before), "録音の開始・終了で、出力用の複製先を作り直さない");
            check(tap.Buffer!.PushedFrames == 4800 + 40 * 480, "複製先へは録音中も音が流れ続ける", $"{tap.Buffer.PushedFrames}");
            check(peak() < 0.35f && peak() > 0.25f, "録音の先頭に、録音前にたまっていた古い音が混ざらない", $"peak={peak()}");
            var secs = stopT - t0;
            check(Math.Abs(frames() / 48000.0 - secs) < 0.03, "録音の長さが、開始から停止までの経過時間と一致する", $"録音={frames() / 48000.0:F3}s 経過={secs:F3}s");
            src.Feed(0.2f, 480);
            check(tap.Buffer!.PushedFrames == 4800 + 41 * 480, "録音が終わったあとも複製先へ流れる");
            hub.Release(outEntry);
            check(hub.OpenCount == 0 && src.StopCount >= 1, "最後に手放したら閉じる");
        }
        {
            // 出力が開いていないとき: 録音が終わったら入力は閉じる（従来どおり）
            var prov = new FakeProvider();
            using var hub = new InputHub(prov, () => true);
            var ms = Mix(hub, 2, out _, out _);
            ms.Start();
            Thread.Sleep(50);
            ms.Stop();
            check(hub.OpenCount == 0 && prov.Sources[0].StopCount >= 1, "出力が無ければ、録音が終わると入力は閉じる");
        }

        section("モノラルのデバイスを2chで録音しても、出力用の複製先は変わらない");
        {
            var prov = new FakeProvider { Ch = 1 };
            using var hub = new InputHub(prov, () => true);
            var e = hub.Acquire("capture:A", 0);
            var tap = new InputTap();
            e.Stream.AddTap(tap);
            var src = prov.Sources[0];
            src.Feed(0.2f, 480);
            var before = tap.Buffer;
            check(before != null && before.Channels == 1, "モノラルのデバイスの複製先はモノラル");
            var ms = Mix(hub, 2, out _, out _);                    // 録音は2ch（デバイスはモノラル）
            ms.Start();
            src.Feed(0.2f, 480);
            check(ReferenceEquals(tap.Buffer, before) && tap.Buffer!.Channels == 1, "録音を始めても、複製先はモノラルのまま（作り直さない）");
            var o = new float[960];
            int got = tap.Buffer.Read(o, 0, 960);
            check(got == 960 && Math.Abs(o[700] - 0.2f) < 0.001f, "録音用に2chへ変換しても、複製先の中身は同じ音", $"got={got} v={o[700]}");
            ms.Stop();
            src.Feed(0.2f, 480);
            check(ReferenceEquals(tap.Buffer, before), "録音を終えても、複製先は変わらない");
            hub.Release(e);
        }

        section("入力チェック（LevelMonitor）");
        {
            var prov = new FakeProvider();
            using var hub = new InputHub(prov, () => true);
            var e = hub.Acquire("capture:A", 0);                   // 出力などが先に開いている
            prov.Sources[0].Feed(0.9f, 480);
            var mon = new LevelMonitor(hub, new[] { new InputSetting("capture:A", 0) });
            mon.Start();
            check(mon.TakePeaks()[0] == 0f, "先に開いていた入力にたまっていた古いピークは、メーターに出さない");
            prov.Sources[0].Feed(0.5f, 480);
            var p = mon.TakePeaks()[0];
            check(Math.Abs(p - 0.5f) < 0.01f, "モニターは新しい音のピークを出す", $"{p}");
            mon.Stop();
            check(hub.OpenCount == 1, "モニターを止めても、ほかが使っている入力は閉じない");
            hub.Release(e);
            check(hub.OpenCount == 0, "全員が手放すと閉じる");
        }
    }
}
