namespace Recorder.Core;

/// <summary>
/// 入力1本ぶんのエフェクトを実際にかける処理(インターリーブのfloatを、その場で書き換える)。
/// 設定は別スレッド(画面)から Update で渡してよい。何もかけない設定のときは、配列に一切触れない(ビット一致)。
/// </summary>
public sealed class EffectChain
{
    private readonly int sr, ch;
    private volatile EffectSetting snap = new();
    private EffectSetting? applied;

    // --- イコライザ(10バンド。各帯域はオクターブ幅のピーキングBiQuad)
    private sealed class Bq
    {
        public double B0 = 1, B1, B2, A1, A2;
        public bool On;
        public double[] Z1 = Array.Empty<double>(), Z2 = Array.Empty<double>();
    }
    private readonly Bq[] bands = Enumerable.Range(0, EqBands.Count).Select(_ => new Bq()).ToArray();
    private readonly Bq hpfBq = new();
    private readonly Bq[] paramBq = Enumerable.Range(0, ParamBand.Count).Select(_ => new Bq()).ToArray();
    private readonly Bq[] eqAll;    // ローカット → 10バンド → パラメトリック の順
    private bool eqWasOn;

    // --- ノイズゲート
    private bool gateOn, gateWasOn, gateOpen = true;
    private float gateOpenLin, gateCloseLin, gateFloor = 0.01f, gateG = 1f, gateRel = 0.001f, gateAtk = 0.1f;
    private int gateHold, gateHoldFrames;

    // --- エコー
    private float[][]? echo;
    private int echoLen, echoPos, echoDelay;
    private float echoFb, echoMix;
    private bool echoOn, echoWasOn;

    // --- リバーブ(Freeverb)
    private sealed class Comb { public float[] Buf = Array.Empty<float>(); public int Pos; public float Store; }
    private sealed class Allpass { public float[] Buf = Array.Empty<float>(); public int Pos; }
    private Comb[][]? combs;          // [ch][8]
    private Allpass[][]? allpasses;   // [ch][4]
    private float revFeedback, revDamp, revMix;
    private bool revOn, revWasOn;
    private static readonly int[] CombTuning = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
    private static readonly int[] AllpassTuning = { 556, 441, 341, 225 };
    private const int StereoSpread = 23;

    // --- リミッター(いちばん最後)
    private readonly Limiter lim;
    private bool limOn, limWasOn;

    public EffectChain(int sampleRate, int channels)
    {
        sr = sampleRate; ch = Math.Max(1, channels);
        eqAll = new[] { hpfBq }.Concat(bands).Concat(paramBq).ToArray();
        lim = new Limiter(sampleRate, ch);
    }

    /// <summary>設定を渡す(コピーして持つ。次の Process から反映)。</summary>
    public void Update(EffectSetting s)
    {
        var c = s.Clone(); c.Normalize();
        snap = c;
    }

    /// <summary>いま何かがかかる設定か(かからなければ Process を呼ばなくてよい)。</summary>
    public bool IsActive => snap.IsActive;

    public void Process(float[] buf, int frames)
    {
        var s = snap;
        if (!s.IsActive) { if (applied != null && applied.IsActive) { applied = s; ResetStates(); } return; }
        if (!ReferenceEquals(s, applied)) { Rebuild(s); applied = s; }

        if (gateOn) ProcessGate(buf, frames);
        if (eqOnNow) ProcessEq(buf, frames);
        if (echoOn) ProcessEcho(buf, frames);
        if (revOn) ProcessReverb(buf, frames);
        if (limOn) lim.Process(buf, frames);
    }

    private bool eqOnNow;

    /// <summary>
    /// ノイズゲート。全チャンネルの最大の大きさで開閉を決め(左右連動)、開く: 3ms、閉じる: 設定の戻り時間でゲインを滑らかに動かす。
    /// 開く閾値より4dB低い値を下回り、さらに60ms続いたら閉じる(バタつき防止)。
    /// </summary>
    private void ProcessGate(float[] buf, int frames)
    {
        for (int f = 0, b = 0; f < frames; f++, b += ch)
        {
            float lv = 0;
            for (int c = 0; c < ch; c++) { float a = buf[b + c]; if (a < 0) a = -a; if (a > lv) lv = a; }
            if (lv >= gateOpenLin) { gateOpen = true; gateHold = gateHoldFrames; }
            else if (gateOpen)
            {
                if (lv >= gateCloseLin) gateHold = gateHoldFrames;
                else if (--gateHold <= 0) gateOpen = false;
            }
            if (gateOpen) gateG += (1f - gateG) * gateAtk;
            else { gateG += (gateFloor - gateG) * gateRel; if (gateG < gateFloor) gateG = gateFloor; }
            if (gateG < 1f) for (int c = 0; c < ch; c++) buf[b + c] *= gateG;
        }
    }

    private void ResetStates()
    {
        foreach (var b in eqAll) { Array.Clear(b.Z1); Array.Clear(b.Z2); }
        echo = null; combs = null; allpasses = null; echoWasOn = revWasOn = eqWasOn = limWasOn = false; gateWasOn = false; gateOpen = true; gateG = 1f;
    }

    // ------------------------------------------------------------ 設定の反映

    private void Rebuild(EffectSetting s)
    {
        // ノイズゲート
        gateOn = s.GateOn;
        if (s.GateOn)
        {
            gateOpenLin = (float)Math.Pow(10, s.GateThresholdDb / 20.0);
            gateCloseLin = (float)Math.Pow(10, (s.GateThresholdDb - 4) / 20.0);
            gateFloor = (float)Math.Pow(10, -s.GateReductionDb / 20.0);
            gateRel = (float)(1.0 - Math.Exp(-1.0 / (s.GateReleaseMs * 0.001 * sr / 4.6)));   // 戻り時間でおよそ -40dB 下がる速さ
            gateAtk = (float)(1.0 - Math.Exp(-1.0 / (0.003 * sr / 4.6)));
            gateHoldFrames = (int)(0.06 * sr);
            if (!gateWasOn) { gateOpen = true; gateG = 1f; gateHold = gateHoldFrames; }
        }
        gateWasOn = s.GateOn;

        // イコライザ(ローカット / グラフィック / パラメトリックは、それぞれ独立にオン・オフできる)
        bool hpfOn = s.HpfHz >= 20;
        eqOnNow = s.EqOn || hpfOn || s.ParamOn;
        if (eqOnNow)
        {
            if (!eqWasOn) { foreach (var b in eqAll) { b.Z1 = new double[ch]; b.Z2 = new double[ch]; } }
            hpfBq.On = hpfOn;
            if (hpfOn)
            {
                var (h0, h1, h2, ha1, ha2) = EqMath.HighPass(sr, s.HpfHz);
                hpfBq.B0 = h0; hpfBq.B1 = h1; hpfBq.B2 = h2; hpfBq.A1 = ha1; hpfBq.A2 = ha2;
            }
            for (int i = 0; i < bands.Length; i++)
            {
                double db = s.Bands[i];
                bands[i].On = s.EqOn && Math.Abs(db) > 0.01;
                if (bands[i].On)
                {
                    var (b0, b1, b2, a1, a2) = EqMath.Peaking(sr, EqBands.Freqs[i], EqBands.Q, db);
                    bands[i].B0 = b0; bands[i].B1 = b1; bands[i].B2 = b2; bands[i].A1 = a1; bands[i].A2 = a2;
                }
            }
            for (int i = 0; i < paramBq.Length; i++)
            {
                var pb = s.Params[i];
                paramBq[i].On = s.ParamOn && Math.Abs(pb.Db) > 0.01;
                if (paramBq[i].On)
                {
                    var (b0, b1, b2, a1, a2) = EqMath.Peaking(sr, pb.Freq, pb.Q, pb.Db);
                    paramBq[i].B0 = b0; paramBq[i].B1 = b1; paramBq[i].B2 = b2; paramBq[i].A1 = a1; paramBq[i].A2 = a2;
                }
            }
        }
        eqWasOn = eqOnNow;

        // エコー
        echoOn = s.EchoOn;
        if (s.EchoOn)
        {
            echoDelay = Math.Max(1, (int)(s.EchoMs * 0.001 * sr));
            if (echo == null || !echoWasOn)
            {
                echoLen = sr + 2;      // 最大1秒
                echo = new float[ch][];
                for (int c = 0; c < ch; c++) echo[c] = new float[echoLen];
                echoPos = 0;
            }
            echoFb = (float)s.EchoFeedback; echoMix = (float)s.EchoMix;
        }
        echoWasOn = s.EchoOn;

        // リバーブ
        revOn = s.ReverbOn;
        if (s.ReverbOn)
        {
            if (combs == null || !revWasOn)
            {
                double k = sr / 44100.0;
                combs = new Comb[ch][]; allpasses = new Allpass[ch][];
                for (int c = 0; c < ch; c++)
                {
                    int spread = (c % 2) * StereoSpread;
                    combs[c] = CombTuning.Select(t => new Comb { Buf = new float[Math.Max(1, (int)((t + spread) * k))] }).ToArray();
                    allpasses[c] = AllpassTuning.Select(t => new Allpass { Buf = new float[Math.Max(1, (int)((t + spread) * k))] }).ToArray();
                }
            }
            revFeedback = (float)(0.7 + 0.28 * s.ReverbRoom);
            revDamp = (float)(0.4 * s.ReverbDamp);
            revMix = (float)s.ReverbMix;
        }
        revWasOn = s.ReverbOn;

        // リミッター
        limOn = s.LimiterOn;
        if (limOn)
        {
            lim.Set(s.CeilingDb, s.ReleaseMs);
            if (!limWasOn) lim.Reset();
        }
        limWasOn = limOn;
    }

    // ------------------------------------------------------------ 処理

    private void ProcessEq(float[] buf, int frames)
    {
        foreach (var b in eqAll)
        {
            if (!b.On) continue;
            double b0 = b.B0, b1 = b.B1, b2 = b.B2, a1 = b.A1, a2 = b.A2;
            for (int c = 0; c < ch; c++)
            {
                double z1 = b.Z1[c], z2 = b.Z2[c];
                for (int f = 0, i = c; f < frames; f++, i += ch)
                {
                    double x = buf[i];
                    double y = b0 * x + z1;
                    z1 = b1 * x - a1 * y + z2;
                    z2 = b2 * x - a2 * y;
                    buf[i] = (float)y;
                }
                if (Math.Abs(z1) < 1e-20) z1 = 0;
                if (Math.Abs(z2) < 1e-20) z2 = 0;
                b.Z1[c] = z1; b.Z2[c] = z2;
            }
        }
    }

    private void ProcessEcho(float[] buf, int frames)
    {
        var e = echo!;
        int pos0 = echoPos;
        for (int c = 0; c < ch; c++)
        {
            var line = e[c];
            int pos = pos0;
            for (int f = 0, i = c; f < frames; f++, i += ch)
            {
                int rp = pos - echoDelay; if (rp < 0) rp += echoLen;
                float d = line[rp];
                float x = buf[i];
                line[pos] = x + d * echoFb;
                buf[i] = x + d * echoMix;
                if (++pos >= echoLen) pos = 0;
            }
        }
        echoPos = (pos0 + frames) % echoLen;
    }

    private void ProcessReverb(float[] buf, int frames)
    {
        float fb = revFeedback, d1 = revDamp, d2 = 1f - revDamp, mix = revMix;
        for (int c = 0; c < ch; c++)
        {
            var cb = combs![c]; var ap = allpasses![c];
            for (int f = 0, i = c; f < frames; f++, i += ch)
            {
                float x = buf[i], inp = x * 0.015f, sum = 0;
                for (int k = 0; k < cb.Length; k++)
                {
                    var cm = cb[k];
                    float o = cm.Buf[cm.Pos];
                    cm.Store = o * d2 + cm.Store * d1;
                    if (cm.Store < 1e-20f && cm.Store > -1e-20f) cm.Store = 0;
                    cm.Buf[cm.Pos] = inp + cm.Store * fb;
                    if (++cm.Pos >= cm.Buf.Length) cm.Pos = 0;
                    sum += o;
                }
                for (int k = 0; k < ap.Length; k++)
                {
                    var a = ap[k];
                    float bo = a.Buf[a.Pos];
                    float o = -sum + bo;
                    a.Buf[a.Pos] = sum + bo * 0.5f;
                    if (++a.Pos >= a.Buf.Length) a.Pos = 0;
                    sum = o;
                }
                buf[i] = x + sum * 3f * mix;
            }
        }
    }
}
