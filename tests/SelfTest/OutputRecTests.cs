using Recorder.Core;

/// <summary>出力の録音（M13）: 出力の最終ミックスを録音の音源へ流す部品のテスト。</summary>
static class OutputRecTests
{
    public static void Run(CheckFn check, Action<string> section)
    {
        section("出力の録音");

        // OutputRenderer の Sink には、最終ミックス（デバイスの周波数・チャンネル数）が渡る
        var r = new OutputRenderer(new[] { new InputTap() }, 48000, 2);
        int gotFrames = 0, gotCh = 0, gotRate = 0;
        r.Sink = (data, frames, ch, rate) => { gotFrames = frames; gotCh = ch; gotRate = rate; };
        var dest = new float[480 * 2];
        r.Render(dest, 0, 480);
        check(gotFrames == 480 && gotCh == 2 && gotRate == 48000, "出力のコールバックのたびに、最終ミックスが Sink へ渡る", $"{gotFrames}/{gotCh}/{gotRate}");
        r.Sink = null;
        gotFrames = 0;
        r.Render(dest, 0, 480);
        check(gotFrames == 0, "Sink を外すと渡らない");

        // OutputTapSource: Start で attach、受けたデータは DataAvailable へ流れ、Stop で外れる
        Action<float[], int, int, int>? attached = null;
        var tap = new OutputTapSource(48000, 2, s => attached = s);
        var received = new List<byte[]>();
        tap.DataAvailable += (b, n) => { var c = new byte[n]; Array.Copy(b, c, n); lock (received) received.Add(c); };
        check(tap.Format.SampleRate == 48000 && tap.Format.Channels == 2 && tap.Format.IsFloat && tap.Format.BitsPerSample == 32, "形式は float32・出力デバイスの周波数とチャンネル数");
        tap.Start();
        check(attached != null, "Start で出力エンジンへシンクを渡す");
        var data = new float[100 * 2];
        for (int i = 0; i < data.Length; i++) data[i] = i / 1000f;
        attached!(data, 100, 2, 48000);
        Thread.Sleep(120);
        int total;
        lock (received) total = received.Sum(b => b.Length);
        check(total == 100 * 2 * 4, "受けたデータが録音側へ流れる（バイト数が合う）", total.ToString());
        float first;
        lock (received) first = BitConverter.ToSingle(received[0], 4);
        check(Math.Abs(first - 0.001f) < 1e-6, "値が変わらず流れる");

        // 形式が違うデータ（出力デバイスが別の形式で開き直された）は録らず、知らせる
        string? notice = null;
        tap.Notice += m => notice = m;
        attached(new float[50 * 1], 50, 1, 44100);
        Thread.Sleep(60);
        int total2;
        lock (received) total2 = received.Sum(b => b.Length);
        check(total2 == total && tap.DroppedBytes == 50 * 1 * 4 && notice != null, "形式が違うデータは録らず、Notice で知らせる");

        tap.Stop();
        check(attached == null, "Stop でシンクが外れる");
        tap.Dispose();

        // 詰まったとき（Start 後に Pump が追いつかないほどためた）も、止まらず古いほうを捨てる
        Action<float[], int, int, int>? a2 = null;
        var tap2 = new OutputTapSource(48000, 2, s => a2 = s);
        tap2.Start();
        var big = new float[48000 * 2];   // 1秒ぶん
        for (int i = 0; i < 40; i++) a2!(big, 48000, 2, 48000);   // 約 15 MB
        check(tap2.DroppedBytes > 0, "詰まったら古いほうを捨てる（出力を止めない）");
        tap2.Dispose();
    }
}
