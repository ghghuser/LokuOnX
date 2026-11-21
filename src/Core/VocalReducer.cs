namespace Recorder.Core;

/// <summary>
/// ボーカル低減（カラオケ化）。ステレオの中央に定位した成分（主にボーカル）を弱める。
/// 中央成分 M=(L+R)/2 を、下限・上限の2か所のクロスオーバー（Linkwitz-Riley 4次）で「低域・対象帯域・高域」に分け、
/// 対象帯域だけを amount 倍ぶん弱めてから合わせ直す。側成分 S=(L-R)/2 は同じ位相変化（全域通過）を通すだけで、音量は変えない。
/// LR4 の各帯域は位相がそろうので、amount=1 のとき対象帯域の中央の音は深く打ち消され、amount=0 のときは音量が変わらない（位相が少し回るだけ）。
/// 対象帯域の外（ベース・キックの低音、高音のシンバルなど）は、中央にあっても残る。
/// チャンネルが2未満のときは何もしない。
/// </summary>
public sealed class VocalReducer
{
    /// <summary>2次のバターワース1段（直接形II転置）。</summary>
    private sealed class Bq
    {
        public double B0 = 1, B1, B2, A1, A2, Z1, Z2;
        public void Set((double, double, double, double, double) c) => (B0, B1, B2, A1, A2) = c;
        public double Tick(double x)
        {
            double y = B0 * x + Z1;
            Z1 = B1 * x - A1 * y + Z2;
            Z2 = B2 * x - A2 * y;
            return y;
        }
        public void Clear() { Z1 = Z2 = 0; }
    }

    /// <summary>LR4 = 同じバターワースの2段重ね。</summary>
    private sealed class Lr4
    {
        private readonly Bq a = new(), b = new();
        public void Set((double, double, double, double, double) c) { a.Set(c); b.Set(c); }
        public double Tick(double x) => b.Tick(a.Tick(x));
        public void Clear() { a.Clear(); b.Clear(); }
    }

    // 中央成分: 下限で低域と上側に分け、上側を上限で対象帯域と高域に分ける。低域には上限の全域通過をそろえて通す。
    private readonly Lr4 lp1 = new(), hp1 = new(), lp2 = new(), hp2 = new(), cLp2 = new(), cHp2 = new();
    // 側成分: 同じ全域通過（下限→上限）を通すだけ。
    private readonly Lr4 sLp1 = new(), sHp1 = new(), sLp2 = new(), sHp2 = new();
    private readonly Lr4[] all;
    private double amount;

    public VocalReducer()
    {
        all = new[] { lp1, hp1, lp2, hp2, cLp2, cHp2, sLp1, sHp1, sLp2, sHp2 };
    }

    /// <summary>設定する。係数だけ更新し、フィルターの状態は保つ（つまみを動かしても音が途切れない）。</summary>
    public void Set(int sampleRate, double amountRatio, double lowHz, double highHz)
    {
        amount = Math.Clamp(amountRatio, 0, 1);
        var lo = EqMath.LowPass(sampleRate, lowHz);
        var hi1 = EqMath.HighPass(sampleRate, lowHz);
        var lo2 = EqMath.LowPass(sampleRate, highHz);
        var hi2 = EqMath.HighPass(sampleRate, highHz);
        lp1.Set(lo); hp1.Set(hi1); sLp1.Set(lo); sHp1.Set(hi1);
        lp2.Set(lo2); hp2.Set(hi2); cLp2.Set(lo2); cHp2.Set(hi2); sLp2.Set(lo2); sHp2.Set(hi2);
    }

    public void Reset()
    {
        foreach (var f in all) f.Clear();
    }

    /// <summary>インターリーブの音を、その場で書き換える。channels は buf のチャンネル数（左＝0番目、右＝1番目。3番目以降は触らない）。</summary>
    public void Process(float[] buf, int frames, int channels)
    {
        if (channels < 2) return;
        for (int f = 0, i = 0; f < frames; f++, i += channels)
        {
            double l = buf[i], r = buf[i + 1];
            double m = (l + r) * 0.5, s = (l - r) * 0.5;

            double low = lp1.Tick(m), up = hp1.Tick(m);
            double mid = lp2.Tick(up), high = hp2.Tick(up);
            double lowAligned = cLp2.Tick(low) + cHp2.Tick(low);     // 低域にも上限の全域通過を通して、位相をそろえる
            double m2 = lowAligned + high + (1 - amount) * mid;

            double s1 = sLp1.Tick(s) + sHp1.Tick(s);
            double s2 = sLp2.Tick(s1) + sHp2.Tick(s1);

            buf[i] = (float)(m2 + s2);
            buf[i + 1] = (float)(m2 - s2);
        }
    }
}
