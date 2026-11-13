using System.Text.Json.Serialization;

namespace Recorder.Core;

/// <summary>
/// 出力デバイス1台ぶんの「最終ミックス」にかけるエフェクト(全体音量のあとに、ローカット → グラフィックEQ → パラメトリックEQ → リミッターの順)。
/// 入力ごとのエフェクトとは別で、その出力にだけかかる。プロファイルの出力カードに入る。
/// </summary>
public sealed class OutputFxSetting
{
    /// <summary>すべてバイパス(設定は残したまま、効果だけ切る)。</summary>
    public bool Bypass { get; set; }

    public bool EqOn { get; set; }
    /// <summary>ローカット(Hz)。0=なし、20〜400。</summary>
    public double HpfHz { get; set; } = 0;
    public double[] Bands { get; set; } = new double[EqBands.Count];
    public bool ParamOn { get; set; }
    public ParamBand[] Params { get; set; } = ParamBand.Defaults();

    // ---- ノイズゲート・エコー・リバーブ（入力のエフェクトと同じもの。11-5確認後に追加）
    public bool GateOn { get; set; }
    public double GateThresholdDb { get; set; } = -45;
    public double GateReleaseMs { get; set; } = 200;
    public double GateReductionDb { get; set; } = 40;
    public bool EchoOn { get; set; }
    public double EchoMs { get; set; } = 300;
    public double EchoFeedback { get; set; } = 0.35;
    public double EchoMix { get; set; } = 0.30;
    public bool ReverbOn { get; set; }
    public double ReverbRoom { get; set; } = 0.5;
    public double ReverbDamp { get; set; } = 0.5;
    public double ReverbMix { get; set; } = 0.25;

    public bool LimiterOn { get; set; }
    /// <summary>リミッターの上限(dB。0=クリップ寸前)。この値を超える音は、先読みして滑らかに下げる。</summary>
    public double CeilingDb { get; set; } = -1.0;
    /// <summary>リミッターが効いたあと、元の音量に戻るまでの時間(ミリ秒)。</summary>
    public double ReleaseMs { get; set; } = 150;

    public const double CeilingMinDb = -24, CeilingMaxDb = 0, ReleaseMinMs = 20, ReleaseMaxMs = 1000;

    [JsonIgnore]
    public bool HasAny => EqOn || HpfHz >= 20 || ParamOn || LimiterOn || GateOn || EchoOn || ReverbOn;
    [JsonIgnore]
    public bool IsActive => !Bypass && HasAny;
    [JsonIgnore]
    public bool IsDefault => SameSound(new OutputFxSetting());

    public OutputFxSetting Clone()
    {
        var c = (OutputFxSetting)MemberwiseClone();
        c.Bands = (double[])(Bands ?? new double[EqBands.Count]).Clone();
        c.Params = (Params ?? ParamBand.Defaults()).Select(p => p.Clone()).ToArray();
        return c;
    }

    private static double C(double v, double lo, double hi, double def) => double.IsFinite(v) ? Math.Clamp(v, lo, hi) : def;

    public void Normalize()
    {
        HpfHz = double.IsFinite(HpfHz) && HpfHz >= 20 ? Math.Clamp(HpfHz, 20, 400) : 0;
        var bd = new double[EqBands.Count];
        for (int i = 0; i < bd.Length; i++) bd[i] = Bands != null && i < Bands.Length ? C(Bands[i], -EqBands.MaxDb, EqBands.MaxDb, 0) : 0;
        Bands = bd;
        var pd = ParamBand.Defaults();
        for (int i = 0; i < pd.Length; i++)
            if (Params != null && i < Params.Length && Params[i] != null) { pd[i] = Params[i]; pd[i].Normalize(i); }
        Params = pd;
        GateThresholdDb = C(GateThresholdDb, -70, -10, -45); GateReleaseMs = C(GateReleaseMs, 20, 1000, 200); GateReductionDb = C(GateReductionDb, 6, 80, 40);
        EchoMs = C(EchoMs, 20, 1000, 300); EchoFeedback = C(EchoFeedback, 0, 0.9, 0.35); EchoMix = C(EchoMix, 0, 1, 0.3);
        ReverbRoom = C(ReverbRoom, 0, 1, 0.5); ReverbDamp = C(ReverbDamp, 0, 1, 0.5); ReverbMix = C(ReverbMix, 0, 1, 0.25);
        CeilingDb = C(CeilingDb, CeilingMinDb, CeilingMaxDb, -1.0);
        ReleaseMs = C(ReleaseMs, ReleaseMinMs, ReleaseMaxMs, 150);
    }

    public bool SameSound(OutputFxSetting o)
    {
        if (Bypass != o.Bypass || EqOn != o.EqOn || HpfHz != o.HpfHz || ParamOn != o.ParamOn || LimiterOn != o.LimiterOn || CeilingDb != o.CeilingDb || ReleaseMs != o.ReleaseMs) return false;
        if (GateOn != o.GateOn || GateThresholdDb != o.GateThresholdDb || GateReleaseMs != o.GateReleaseMs || GateReductionDb != o.GateReductionDb) return false;
        if (EchoOn != o.EchoOn || EchoMs != o.EchoMs || EchoFeedback != o.EchoFeedback || EchoMix != o.EchoMix) return false;
        if (ReverbOn != o.ReverbOn || ReverbRoom != o.ReverbRoom || ReverbDamp != o.ReverbDamp || ReverbMix != o.ReverbMix) return false;
        for (int i = 0; i < EqBands.Count; i++) if ((Bands?[i] ?? 0) != (o.Bands?[i] ?? 0)) return false;
        for (int i = 0; i < ParamBand.Count; i++) if (!Params[i].Same(o.Params[i])) return false;
        return true;
    }

    /// <summary>EQの部分だけを、入力用のエフェクトチェーンに渡せる形にする。</summary>
    public EffectSetting ToEqSetting() => new()
    {
        EqOn = EqOn, HpfHz = HpfHz, Bands = (double[])Bands.Clone(), ParamOn = ParamOn, Params = Params.Select(p => p.Clone()).ToArray(), ToOutput = true,
    };

    /// <summary>効果すべてを、入力用のエフェクトチェーン（EffectChain）に渡せる形にする（出力も同じチェーンで処理する）。</summary>
    public EffectSetting ToEffectSetting() => new()
    {
        Bypass = Bypass,
        EqOn = EqOn, HpfHz = HpfHz, Bands = (double[])Bands.Clone(), ParamOn = ParamOn, Params = Params.Select(p => p.Clone()).ToArray(), ToOutput = true,
        GateOn = GateOn, GateThresholdDb = GateThresholdDb, GateReleaseMs = GateReleaseMs, GateReductionDb = GateReductionDb,
        EchoOn = EchoOn, EchoMs = EchoMs, EchoFeedback = EchoFeedback, EchoMix = EchoMix,
        ReverbOn = ReverbOn, ReverbRoom = ReverbRoom, ReverbDamp = ReverbDamp, ReverbMix = ReverbMix,
        LimiterOn = LimiterOn, CeilingDb = CeilingDb, ReleaseMs = ReleaseMs,
    };

    /// <summary>画面（入力と共通のエフェクト画面）で編集した設定 e を、この出力の設定へ取り込む（バイパスも含む）。</summary>
    public void CopyFrom(EffectSetting e)
    {
        var c = e.Clone();
        Bypass = c.Bypass;
        EqOn = c.EqOn; HpfHz = c.HpfHz; Bands = c.Bands; ParamOn = c.ParamOn; Params = c.Params;
        GateOn = c.GateOn; GateThresholdDb = c.GateThresholdDb; GateReleaseMs = c.GateReleaseMs; GateReductionDb = c.GateReductionDb;
        EchoOn = c.EchoOn; EchoMs = c.EchoMs; EchoFeedback = c.EchoFeedback; EchoMix = c.EchoMix;
        ReverbOn = c.ReverbOn; ReverbRoom = c.ReverbRoom; ReverbDamp = c.ReverbDamp; ReverbMix = c.ReverbMix;
        LimiterOn = c.LimiterOn; CeilingDb = c.CeilingDb; ReleaseMs = c.ReleaseMs;
        Normalize();
    }

    /// <summary>WriteTo が書く項目名（prefix の後ろ）。初期値に戻したとき ini から消すのに使う。</summary>
    public static readonly string[] IniKeys =
    {
        "Bypass", "EqOn", "HpfHz", "EqBands", "ParamOn", "ParamBands", "LimiterOn", "CeilingDb", "ReleaseMs",
        "GateOn", "GateThresholdDb", "GateReleaseMs", "GateReductionDb", "EchoOn", "EchoMs", "EchoFeedback", "EchoMix", "ReverbOn", "ReverbRoom", "ReverbDamp", "ReverbMix",
    };

    public void WriteTo(IniSection s, string prefix, bool comment)
    {
        string? C(string t) => comment ? t : null;
        s.Set(prefix + "Bypass", Bypass, C("この出力の最終ミックスのエフェクト(ローカット→グラフィックEQ→パラメトリックEQ→リミッター)。Bypass=すべてバイパス"));
        s.Set(prefix + "EqOn", EqOn);
        s.Set(prefix + "HpfHz", HpfHz, C("ローカット(Hz、0=なし)"));
        s.Set(prefix + "EqBands", string.Join(",", Bands.Select(v => v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))));
        s.Set(prefix + "ParamOn", ParamOn);
        s.Set(prefix + "ParamBands", string.Join(",", Params.Select(b => b.ToText())));
        s.Set(prefix + "LimiterOn", LimiterOn);
        s.Set(prefix + "CeilingDb", CeilingDb, C("リミッターの上限(dB、-24〜0)"));
        s.Set(prefix + "ReleaseMs", ReleaseMs, C("リミッターの戻り時間(ミリ秒、20〜1000)"));
        s.Set(prefix + "GateOn", GateOn); s.Set(prefix + "GateThresholdDb", GateThresholdDb); s.Set(prefix + "GateReleaseMs", GateReleaseMs); s.Set(prefix + "GateReductionDb", GateReductionDb);
        s.Set(prefix + "EchoOn", EchoOn); s.Set(prefix + "EchoMs", EchoMs); s.Set(prefix + "EchoFeedback", EchoFeedback); s.Set(prefix + "EchoMix", EchoMix);
        s.Set(prefix + "ReverbOn", ReverbOn); s.Set(prefix + "ReverbRoom", ReverbRoom); s.Set(prefix + "ReverbDamp", ReverbDamp); s.Set(prefix + "ReverbMix", ReverbMix);
    }

    public static OutputFxSetting ReadFrom(IniSection s, string prefix)
    {
        var d = new OutputFxSetting();
        var e = new OutputFxSetting
        {
            Bypass = s.GetBool(prefix + "Bypass", false), EqOn = s.GetBool(prefix + "EqOn", false), HpfHz = s.GetDouble(prefix + "HpfHz", 0),
            ParamOn = s.GetBool(prefix + "ParamOn", false), LimiterOn = s.GetBool(prefix + "LimiterOn", false),
            CeilingDb = s.GetDouble(prefix + "CeilingDb", d.CeilingDb), ReleaseMs = s.GetDouble(prefix + "ReleaseMs", d.ReleaseMs),
            GateOn = s.GetBool(prefix + "GateOn", false), GateThresholdDb = s.GetDouble(prefix + "GateThresholdDb", d.GateThresholdDb),
            GateReleaseMs = s.GetDouble(prefix + "GateReleaseMs", d.GateReleaseMs), GateReductionDb = s.GetDouble(prefix + "GateReductionDb", d.GateReductionDb),
            EchoOn = s.GetBool(prefix + "EchoOn", false), EchoMs = s.GetDouble(prefix + "EchoMs", d.EchoMs),
            EchoFeedback = s.GetDouble(prefix + "EchoFeedback", d.EchoFeedback), EchoMix = s.GetDouble(prefix + "EchoMix", d.EchoMix),
            ReverbOn = s.GetBool(prefix + "ReverbOn", false), ReverbRoom = s.GetDouble(prefix + "ReverbRoom", d.ReverbRoom),
            ReverbDamp = s.GetDouble(prefix + "ReverbDamp", d.ReverbDamp), ReverbMix = s.GetDouble(prefix + "ReverbMix", d.ReverbMix),
        };
        var parts = s.GetString(prefix + "EqBands").Split(',', StringSplitOptions.TrimEntries);
        for (int i = 0; i < EqBands.Count; i++)
            e.Bands[i] = i < parts.Length && double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
        var pp = s.GetString(prefix + "ParamBands").Split(',', StringSplitOptions.TrimEntries);
        for (int i = 0; i < e.Params.Length && i < pp.Length; i++) ParamBand.TryParse(pp[i], e.Params[i]);
        e.Normalize();
        return e;
    }
}
