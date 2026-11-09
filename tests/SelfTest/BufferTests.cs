using Recorder.Core;

/// <summary>SourceBuffer（タップ付きバッファ）と TrimResampler（時計ずれ補正つきの変換）のテスト。</summary>
static class BufferTests
{
    public static void Run(CheckFn check, Action<string> section)
    {
        section("SourceBuffer");
        {
            var src = new SourceBuffer(48000, 2, 1.0);
            var tap = new SourceBuffer(48000, 2, 1.0);
            src.AddTap(tap);
            var d = new float[480 * 2]; for (int i = 0; i < d.Length; i++) d[i] = i * 0.001f;
            src.Push(d, 0, 480);
            check(src.AvailableFrames == 480 && tap.AvailableFrames == 480, "Push した音はタップにも入る");
            var o = new float[480 * 2];
            check(src.Read(o, 0, 480) == 480 && o.SequenceEqual(d), "本体から読んでも中身は同じ");
            check(tap.AvailableFrames == 480, "本体を読んでもタップは減らない（独立している）");
            check(tap.Read(o, 0, 480) == 480 && o.SequenceEqual(d), "タップの中身も同じ");
            src.RemoveTap(tap); src.Push(d, 0, 480);
            check(tap.AvailableFrames == 0 && src.AvailableFrames == 480, "タップを外すと複製されない");
            src.Clear();
            check(src.AvailableFrames == 0, "Clear で空になる");
        }
        {
            var b = new SourceBuffer(1000, 1, 1.0);          // 1000フレームのリング
            var d = new float[600]; for (int i = 0; i < d.Length; i++) d[i] = i;
            b.Push(d, 0, 600); b.Push(d, 0, 600);            // 1200入れて200あふれる
            check(b.AvailableFrames == 1000 && b.DroppedFrames == 200, "あふれた分は古い方から捨てる", $"{b.AvailableFrames}/{b.DroppedFrames}");
            b.DiscardTo(300);
            var o = new float[300];
            check(b.AvailableFrames == 300 && b.Read(o, 0, 300) == 300 && o[299] == 599, "DiscardTo は新しい方を残す");
        }
        {
            var src = new SourceBuffer(48000, 2, 1.0); var tap = new SourceBuffer(48000, 1, 1.0);
            src.AddTap(tap); src.Push(new float[960], 0, 480);
            check(tap.AvailableFrames == 0 && src.AvailableFrames == 480, "チャンネル数が違うタップには複製しない");
        }

        section("TrimResampler");
        {
            var rs = new TrimResampler(48000, 48000, 2);
            var inp = new float[960]; for (int i = 0; i < inp.Length; i++) inp[i] = (float)Math.Sin(i * 0.1);
            var outp = new List<float>(); rs.Process(inp, 480, outp);
            check(rs.IsPassThrough && outp.SequenceEqual(inp), "同じ周波数・補正なしは素通し（ビット一致）");
        }
        {
            var rs = new TrimResampler(48000, 44100, 1);
            var outp = new List<float>(); var blk = new float[480]; long t = 0;
            for (int k = 0; k < 100; k++)
            {
                for (int i = 0; i < 480; i++) blk[i] = (float)(0.5 * Math.Sin(2 * Math.PI * 1000 * (t + i) / 48000.0));
                t += 480; rs.Process(blk, 480, outp);
            }
            check(Math.Abs(outp.Count - 44100) < 100, "48k→44.1k で1秒ぶんがほぼ44100フレームになる", $"{outp.Count}");
            double peak = 0; for (int i = 2000; i < outp.Count; i++) peak = Math.Max(peak, Math.Abs(outp[i]));
            check(Math.Abs(peak - 0.5) < 0.02, "変換しても振幅が保たれる", $"{peak:F3}");
        }
        {
            int Count(double trim)
            {
                var rs = new TrimResampler(48000, 48000, 1, allowTrim: true) { Trim = trim };
                var outp = new List<float>(); var blk = new float[480];
                for (int k = 0; k < 100; k++) rs.Process(blk, 480, outp);
                return outp.Count;
            }
            int n0 = Count(0), nPlus = Count(0.005), nMinus = Count(-0.005);
            check(!new TrimResampler(48000, 48000, 1, true).IsPassThrough, "allowTrim なら同じ周波数でも素通しにしない");
            check(nPlus < n0 && nMinus > n0 && Math.Abs((n0 - nPlus) - n0 * 0.005) < 30, "Trim で出力の長さが±0.5%動く", $"{nMinus}/{n0}/{nPlus}");
        }
    }
}
