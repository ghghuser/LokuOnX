using Recorder.Core;

/// <summary>出力デバイス用の音づくり（OutputRenderer）のテスト。入力と出力の時計のずれを、仮想の時間で再現する（LokuOn の OutputTests を移したもの）。</summary>
static class OutputTests
{
    sealed class Sim
    {
        public long Underruns, Overflows;
        public double MinFill = 1e9, MaxFill, MaxJump;
        public int Glitches;
    }

    /// <summary>入力（srcRate, srcCh）を drift だけ速く／遅く送り、出力デバイス（devRate）が 480 フレームずつ引く。</summary>
    static Sim Simulate(int srcRate, int srcCh, int devRate, int devCh, double drift, double seconds, double warm = 3.0,
        Func<double, bool>? producerOn = null)
    {
        var buf = new SourceBuffer(srcRate, srcCh, 2.0);
        var tap = new InputTap { Buffer = buf };
        var rend = new OutputRenderer(new[] { tap }, devRate, devCh);
        double freq = 1000, amp = 0.5;
        long produced = 0;
        int chunk = srcRate / 100;
        var pbuf = new float[chunk * srcCh];
        var outBuf = new float[480 * devCh];
        var sim = new Sim();
        int block = 480;
        float prev = 0;
        bool havePrev = false;
        long blocks = (long)(seconds * devRate / block);
        long warmBlocks = (long)(warm * devRate / block);
        for (long b = 0; b < blocks; b++)
        {
            double t = (double)b * block / devRate;
            double target = t * srcRate * (1 + drift);
            while (produced + chunk <= target)
            {
                bool on = producerOn == null || producerOn(produced / (double)srcRate);
                if (on)
                {
                    for (int f = 0; f < chunk; f++)
                    {
                        float v = (float)(amp * Math.Sin(2 * Math.PI * freq * (produced + f) / srcRate));
                        for (int c = 0; c < srcCh; c++) pbuf[f * srcCh + c] = v;
                    }
                    buf.Push(pbuf, 0, chunk);
                }
                produced += chunk;
            }
            long u0 = rend.Underruns;
            rend.Render(outBuf, 0, block);
            if (b >= warmBlocks)
            {
                double fill = buf.AvailableFrames / (double)srcRate;
                sim.MinFill = Math.Min(sim.MinFill, fill);
                sim.MaxFill = Math.Max(sim.MaxFill, fill);
                if (rend.Underruns > u0) sim.Glitches++;
                for (int f = 0; f < block; f++)
                {
                    float v = outBuf[f * devCh];
                    if (havePrev) { double j = Math.Abs(v - prev); if (j > sim.MaxJump) sim.MaxJump = j; }
                    prev = v;
                    havePrev = true;
                }
            }
        }
        sim.Underruns = rend.Underruns;
        sim.Overflows = rend.Overflows;
        return sim;
    }

    static OutputRenderer Make(int srcRate, int srcCh, int devRate, int devCh, out SourceBuffer buf, out InputTap tap)
    {
        buf = new SourceBuffer(srcRate, srcCh, 2.0);
        tap = new InputTap { Buffer = buf };
        return new OutputRenderer(new[] { tap }, devRate, devCh);
    }

    public static void Run(CheckFn check, Action<string> section)
    {
        section("出力（クロックのずれの補正・変換）");

        // 1) ずれの補正（同じレート・違うレート、速い／遅い）
        foreach (var (inR, devR, d, name) in new[]
        {
            (48000, 48000, 0.0, "ずれなし 48k→48k"),
            (48000, 48000, 0.0005, "入力が+500ppm 48k→48k"),
            (48000, 48000, -0.0005, "入力が-500ppm 48k→48k"),
            (48000, 44100, 0.003, "入力が+0.3% 48k→44.1k"),
            (44100, 48000, -0.003, "入力が-0.3% 44.1k→48k"),
        })
        {
            var sm = Simulate(inR, 2, devR, 2, d, 60);
            string det = $"underrun={sm.Underruns} overflow={sm.Overflows} fill={sm.MinFill * 1000:F0}-{sm.MaxFill * 1000:F0}ms jump={sm.MaxJump:F3}";
            check(sm.Glitches == 0 && sm.Overflows == 0, name + ": 60秒、途切れなし", det);
            check(sm.MinFill > 0.003 && sm.MaxFill < 0.2, name + ": たまりが範囲内", det);
            check(sm.MaxJump < 0.2, name + ": 波形が連続（プチノイズなし）", det);
        }

        // 2) 長時間（5分）
        {
            var sm = Simulate(48000, 1, 44100, 2, 0.0005, 300);
            string det = $"underrun={sm.Underruns} overflow={sm.Overflows} fill={sm.MinFill * 1000:F0}-{sm.MaxFill * 1000:F0}ms jump={sm.MaxJump:F3}";
            check(sm.Glitches == 0 && sm.Overflows == 0 && sm.MaxJump < 0.2, "5分（モノラル→ステレオ、+500ppm）", det);
        }

        // 3) 入力が止まって再開（ループバックの無音）
        {
            var sm = Simulate(48000, 2, 48000, 2, 0.0, 30, warm: 3, producerOn: t => t < 10 || t > 15);
            check(sm.Underruns >= 1, "入力が止まると無音になる（止まらない）", $"underrun={sm.Underruns}");
            var rend = Make(48000, 2, 48000, 2, out var buf, out _);
            var o = new float[480 * 2];
            rend.Render(o, 0, 480);
            check(o.All(x => x == 0f), "データがないときは無音");
            var d = new float[480 * 2];
            Array.Fill(d, 0.3f);
            for (int k = 0; k < 10; k++) buf.Push(d, 0, 480);
            float peak = 0;
            for (int k = 0; k < 6; k++) { rend.Render(o, 0, 480); peak = Math.Max(peak, o.Max()); }
            check(peak > 0.25f, "たまれば鳴り始める", $"peak={peak}");
        }

        // 4) 音量・送りのオンオフ・チャンネル変換
        {
            var rend = Make(48000, 1, 48000, 2, out var buf, out _);
            var m = new float[4800];
            Array.Fill(m, 0.25f);
            var o = new float[480 * 2];
            for (int k = 0; k < 20; k++) buf.Push(m, 0, 480);
            rend.Render(o, 0, 480);
            for (int k = 0; k < 4; k++) { buf.Push(m, 0, 480); rend.Render(o, 0, 480); }
            check(Math.Abs(o[400] - 0.25f) < 0.01f && Math.Abs(o[401] - 0.25f) < 0.01f, "モノラル→左右に複製");
            rend.Bus.MasterGain = 0.5f;
            for (int k = 0; k < 3; k++) { buf.Push(m, 0, 480); rend.Render(o, 0, 480); }
            check(Math.Abs(o[400] - 0.125f) < 0.02f, "この出力の全体音量 0.5", $"{o[400]}");
            rend.Bus.MasterGain = 1f;
            var big = new float[480];
            Array.Fill(big, 2.0f);
            for (int k = 0; k < 3; k++) { buf.Push(big, 0, 480); rend.Render(o, 0, 480); }
            check(o.Max() <= 1f && rend.ReadPeaks().Clipped, "大きすぎる音は±1に切り詰めてクリップを記録");
            rend.Bus.SetSend(0, false);
            buf.Push(m, 0, 480);
            rend.Render(o, 0, 480);
            check(o.All(x => x == 0f) && buf.AvailableFrames == 0, "送りオフ: 無音でためない");
        }
        {
            var sd = new float[480 * 2];
            for (int i = 0; i < 480; i++) { sd[i * 2] = 0.2f; sd[i * 2 + 1] = 0.4f; }
            var r1 = Make(48000, 2, 48000, 1, out var b1, out _);
            var o1 = new float[480];
            for (int k = 0; k < 20; k++) b1.Push(sd, 0, 480);
            r1.Render(o1, 0, 480);
            r1.Render(o1, 0, 480);
            check(Math.Abs(o1[300] - 0.3f) < 0.01f, "ステレオ→モノラルは平均", $"{o1[300]}");
            var r6 = Make(48000, 2, 48000, 6, out var b6, out _);
            var o6 = new float[480 * 6];
            for (int k = 0; k < 20; k++) b6.Push(sd, 0, 480);
            r6.Render(o6, 0, 480);
            r6.Render(o6, 0, 480);
            check(Math.Abs(o6[300 * 6] - 0.2f) < 0.01f && Math.Abs(o6[300 * 6 + 1] - 0.4f) < 0.01f && o6[300 * 6 + 2] == 0f, "ステレオ→6ch: 余りは無音");
        }

        // 5) 入力の形式が変わって複製先のバッファが作り直されても、止まらずに追従する
        {
            var rend = Make(48000, 2, 48000, 2, out var buf, out var tap);
            var d = new float[480 * 2];
            Array.Fill(d, 0.3f);
            var o = new float[480 * 2];
            for (int k = 0; k < 10; k++) buf.Push(d, 0, 480);
            for (int k = 0; k < 4; k++) rend.Render(o, 0, 480);
            var nb = new SourceBuffer(44100, 1, 2.0);   // デバイスが別の形式でつなぎ直された
            tap.Buffer = nb;
            var m = new float[441];
            Array.Fill(m, 0.4f);
            float peak = 0;
            for (int k = 0; k < 20; k++) { nb.Push(m, 0, 441); rend.Render(o, 0, 480); peak = Math.Max(peak, o.Max()); }
            check(peak > 0.3f && rend.LastError.Length == 0, "複製先が作り直されても、新しい形式で鳴り続ける", $"peak={peak} err={rend.LastError}");
            tap.Buffer = null;
            rend.Render(o, 0, 480);
            check(o.All(x => x == 0f), "複製先が無い（入力が外れている）間は無音");
            check(rend.Describe().Contains("入力1"), "診断用の説明が出せる");
        }
    }
}
