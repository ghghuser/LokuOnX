namespace Recorder.Core;

/// <summary>
/// 先読み式のリミッター。1.5ms 先読みして、上限を超えそうな音を滑らかに下げる(上限を超えないことを保証する)。
/// 左右(全チャンネル)は連動して下げるので、音の定位が動かない。遅延は先読みぶん(1.5ms)。
/// </summary>
public sealed class Limiter
{
    private readonly int ch, n, sr;            // n = 先読みのフレーム数
    private readonly float[] delay;             // 遅らせた音(インターリーブ、n フレーム)
    private readonly float[] rBuf, mBuf;        // 戻り付きの必要ゲイン / その窓内の最小値(n+1 個のリング)
    private int dpos, wpos;
    private float r = 1f;
    private float ceiling = 0.89f, relCoef = 0.001f;

    public int LatencyFrames => n;

    public Limiter(int sampleRate, int channels)
    {
        sr = sampleRate;
        ch = Math.Max(1, channels);
        n = Math.Max(1, (int)Math.Round(0.0015 * sampleRate));
        delay = new float[n * ch];
        rBuf = new float[n + 1]; mBuf = new float[n + 1];
        Reset();
    }

    public void Set(double ceilingDb, double releaseMs)
    {
        ceiling = (float)Math.Pow(10.0, ceilingDb / 20.0);
        relCoef = (float)(1.0 - Math.Exp(-1.0 / (Math.Max(1.0, releaseMs) * 0.001 * sr)));
    }

    public void Reset()
    {
        Array.Clear(delay); Array.Fill(rBuf, 1f); Array.Fill(mBuf, 1f); dpos = 0; wpos = 0; r = 1f;
    }

    /// <summary>
    /// 音 x[t] が来るたびに、必要なゲイン gr[t] を求め、戻り付きの r[t] (r ≤ gr)にする。
    /// M[t] = 直近 n+1 個の r の最小値、G = 直近 n+1 個の M の平均。
    /// n フレーム前の音 x[t-n] に G をかける。G の平均に入る M はすべて x[t-n] を含む窓なので、
    /// それぞれ gr[t-n] 以下であり、したがって出力は上限を超えない。
    /// </summary>
    public void Process(float[] buf, int frames)
    {
        int n1 = n + 1;
        for (int f = 0; f < frames; f++)
        {
            int b = f * ch;
            float p = 0;
            for (int c = 0; c < ch; c++) { float a = buf[b + c]; if (a < 0) a = -a; if (a > p) p = a; }
            float gr = p > ceiling ? ceiling / p : 1f;
            float rec = r + relCoef * (1f - r);
            r = gr < rec ? gr : rec;
            rBuf[wpos] = r;
            float m = 1f;
            for (int k = 0; k < n1; k++) { float v = rBuf[k]; if (v < m) m = v; }
            mBuf[wpos] = m;
            double sum = 0;
            for (int k = 0; k < n1; k++) sum += mBuf[k];
            float g = (float)(sum / n1);
            int d = dpos * ch;
            for (int c = 0; c < ch; c++)
            {
                float old = delay[d + c];
                delay[d + c] = buf[b + c];
                buf[b + c] = old * g;
            }
            if (++dpos >= n) dpos = 0;
            if (++wpos >= n1) wpos = 0;
        }
    }
}

/// <summary>出力1台ぶんの最終ミックス用エフェクト。設定は別スレッドから Update で渡してよい。何もかけない設定のときは配列に触れない。</summary>
public sealed class OutputFxChain
{
    private readonly EffectChain chain;   // 入力と同じチェーン（ゲート → ローカット → EQ → パラメトリック → エコー → リバーブ → リミッター）
    private volatile OutputFxSetting snap = new();

    public OutputFxChain(int sampleRate, int channels)
    {
        chain = new EffectChain(sampleRate, channels);
    }

    public bool IsActive => snap.IsActive;

    public void Update(OutputFxSetting s)
    {
        var c = s.Clone(); c.Normalize();
        chain.Update(c.IsActive ? c.ToEffectSetting() : new EffectSetting());
        snap = c;
    }

    public void Process(float[] buf, int frames)
    {
        if (!snap.IsActive) return;
        chain.Process(buf, frames);
    }
}
