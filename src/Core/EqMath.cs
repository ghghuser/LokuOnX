namespace Recorder.Core;

/// <summary>パラメトリックEQの1バンド(ピーキング)。Db=0 なら効果なし。</summary>
public sealed class ParamBand
{
    public const int Count = 4;
    public static readonly string[] Names = { Loc.Get("eq.band.low"), Loc.Get("eq.band.lowmid"), Loc.Get("eq.band.highmid"), Loc.Get("eq.band.high") };
    public static readonly double[] DefaultFreqs = { 100, 500, 2000, 8000 };
    public const double MaxDb = 12, MinQ = 0.3, MaxQ = 8, DefaultQ = 1.0, MinHz = 20, MaxHz = 20000;

    public double Freq { get; set; } = 1000;
    public double Db { get; set; }
    public double Q { get; set; } = DefaultQ;

    public static ParamBand[] Defaults() => Enumerable.Range(0, Count).Select(i => new ParamBand { Freq = DefaultFreqs[i] }).ToArray();
    public ParamBand Clone() => new() { Freq = Freq, Db = Db, Q = Q };
    public bool Same(ParamBand o) => Freq == o.Freq && Db == o.Db && Q == o.Q;

    public void Normalize(int i)
    {
        Freq = double.IsFinite(Freq) ? Math.Clamp(Freq, MinHz, MaxHz) : DefaultFreqs[i];
        Db = double.IsFinite(Db) ? Math.Clamp(Db, -MaxDb, MaxDb) : 0;
        Q = double.IsFinite(Q) ? Math.Clamp(Q, MinQ, MaxQ) : DefaultQ;
    }

    public string ToText()
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        return Freq.ToString("0.#", ci) + ":" + Db.ToString("0.#", ci) + ":" + Q.ToString("0.##", ci);
    }

    public static void TryParse(string t, ParamBand into)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var a = t.Split(':', StringSplitOptions.TrimEntries);
        if (a.Length >= 1 && double.TryParse(a[0], System.Globalization.NumberStyles.Float, ci, out var f)) into.Freq = f;
        if (a.Length >= 2 && double.TryParse(a[1], System.Globalization.NumberStyles.Float, ci, out var d)) into.Db = d;
        if (a.Length >= 3 && double.TryParse(a[2], System.Globalization.NumberStyles.Float, ci, out var q)) into.Q = q;
    }
}

/// <summary>グラフィックEQの帯域(31Hz〜16kHz、オクターブ間隔)。</summary>
public static class EqBands
{
    public const int Count = 10;
    public const double MaxDb = 12;
    public const double Q = 1.41;   // 約1オクターブ幅
    public static readonly double[] Freqs = { 31.25, 62.5, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
    public static readonly string[] Labels = { "31", "62", "125", "250", "500", "1k", "2k", "4k", "8k", "16k" };

    /// <summary>よく使う設定(名前, 10帯域のdB)。先頭は「フラット」。</summary>
    public static readonly (string Name, double[] Db)[] Presets =
    {
        (Loc.Get("eq.preset.flat"), new double[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
        (Loc.Get("eq.preset.bass"), new double[] { 6, 6, 5, 3, 1, 0, 0, 0, 0, 0 }),
        (Loc.Get("eq.preset.treble"), new double[] { 0, 0, 0, 0, 0, 1, 2, 4, 5, 6 }),
        (Loc.Get("eq.preset.voice"), new double[] { -6, -5, -3, -1, 1, 3, 4, 3, 1, 0 }),
        (Loc.Get("eq.preset.media"), new double[] { 4, 5, 2, -1, -2, -3, -1, 3, 5, 5 }),
        (Loc.Get("eq.preset.rock"), new double[] { 4, 3, 2, 0, -2, -1, 1, 3, 4, 4 }),
        (Loc.Get("eq.preset.pop"), new double[] { -1, 2, 3, 3, 2, 0, -1, -1, 1, 1 }),
        (Loc.Get("eq.preset.classical"), new double[] { 0, 0, 0, 0, 0, 0, -2, -3, -3, -4 }),
        (Loc.Get("eq.preset.clear"), new double[] { 0, 0, -2, -4, -3, 0, 1, 2, 2, 1 }),
        (Loc.Get("eq.preset.hiss"), new double[] { 0, 0, 0, 0, 0, 0, -1, -3, -6, -9 }),
    };
}

/// <summary>BiQuad係数と周波数特性の計算(RBJ Audio EQ Cookbook)。</summary>
public static class EqMath
{
    /// <summary>ピーキングEQの係数 (b0, b1, b2, a1, a2)(a0で割ってある)。</summary>
    public static (double, double, double, double, double) Peaking(int sr, double f, double q, double db)
    {
        double A = Math.Pow(10, db / 40), w = 2 * Math.PI * Math.Min(f, sr * 0.45) / sr, cs = Math.Cos(w), al = Math.Sin(w) / (2 * q);
        double a0 = 1 + al / A;
        return ((1 + al * A) / a0, -2 * cs / a0, (1 - al * A) / a0, -2 * cs / a0, (1 - al / A) / a0);
    }

    /// <summary>ローカット(2次のバターワース相当)の係数 (b0, b1, b2, a1, a2)。</summary>
    public static (double, double, double, double, double) HighPass(int sr, double f)
    {
        double w = 2 * Math.PI * Math.Min(f, sr * 0.45) / sr, cs = Math.Cos(w), al = Math.Sin(w) / (2 * 0.7071);
        double a0 = 1 + al;
        return ((1 + cs) / 2 / a0, -(1 + cs) / a0, (1 + cs) / 2 / a0, -2 * cs / a0, (1 - al) / a0);
    }

    private static double MagDb(double b0, double b1, double b2, double a1, double a2, double c1, double c2)
    {
        double num = b0 * b0 + b1 * b1 + b2 * b2 + 2 * (b0 * b1 + b1 * b2) * c1 + 2 * b0 * b2 * c2;
        double den = 1 + a1 * a1 + a2 * a2 + 2 * (a1 + a1 * a2) * c1 + 2 * a2 * c2;
        return 10 * Math.Log10(Math.Max(1e-12, num / den));
    }

    /// <summary>ローカットと10バンドを全部通したときの、周波数 hz での音量変化(dB)。画面のカーブ表示用。hpfHz は 0=なし。</summary>
    public static double ResponseDb(double[] bands, double hz, int sr = 48000, double hpfHz = 0, ParamBand[]? param = null)
    {
        double total = 0, w = 2 * Math.PI * hz / sr, c1 = Math.Cos(w), c2 = Math.Cos(2 * w);
        if (hpfHz >= 20) { var (h0, h1, h2, ha1, ha2) = HighPass(sr, hpfHz); total += MagDb(h0, h1, h2, ha1, ha2, c1, c2); }
        for (int i = 0; i < EqBands.Count && i < bands.Length; i++)
        {
            if (Math.Abs(bands[i]) <= 0.01) continue;
            var (b0, b1, b2, a1, a2) = Peaking(sr, EqBands.Freqs[i], EqBands.Q, bands[i]);
            double num = b0 * b0 + b1 * b1 + b2 * b2 + 2 * (b0 * b1 + b1 * b2) * c1 + 2 * b0 * b2 * c2;
            double den = 1 + a1 * a1 + a2 * a2 + 2 * (a1 + a1 * a2) * c1 + 2 * a2 * c2;
            total += 10 * Math.Log10(Math.Max(1e-12, num / den));
        }
        if (param != null)
            foreach (var pb in param)
            {
                if (Math.Abs(pb.Db) <= 0.01) continue;
                var (b0, b1, b2, a1, a2) = Peaking(sr, pb.Freq, pb.Q, pb.Db);
                total += MagDb(b0, b1, b2, a1, a2, c1, c2);
            }
        return total;
    }
}
