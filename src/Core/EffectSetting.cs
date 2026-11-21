using System.Text.Json.Serialization;

namespace Recorder.Core;

/// <summary>入力1本ぶんのエフェクト設定(ノイズゲート → ローカット/イコライザ → エコー → リバーブ の順に通す)。プロファイルに保存される。</summary>
public sealed class EffectSetting
{
    // ---- 全体
    /// <summary>すべてバイパス(設定は残したまま、効果だけ切る)。</summary>
    public bool Bypass { get; set; }
    /// <summary>エフェクトをかけた音を録音に入れるか(既定: しない=録音は素の音)。</summary>
    public bool ToRecording { get; set; } = false;
    /// <summary>エフェクトをかけた音を出力に流すか(既定: する)。</summary>
    public bool ToOutput { get; set; } = true;

    // ---- ボーカル低減(カラオケ化。いちばん最初にかかる。ステレオの中央に定位した音を弱める。モノラル入力では効果なし)
    public bool VocalOn { get; set; }
    /// <summary>弱める量(0〜1。1=対象帯域の中央成分を完全に消す)。</summary>
    public double VocalAmount { get; set; } = 0.85;
    /// <summary>対象帯域の下限(Hz)。これより低い音(ベース・キックなど)は中央でも残す。50〜1000。</summary>
    public double VocalLowHz { get; set; } = 200;
    /// <summary>対象帯域の上限(Hz)。これより高い音は残す。1000〜12000。</summary>
    public double VocalHighHz { get; set; } = 6000;

    // ---- ノイズゲート(ボーカル低減の次にかかる。小さい音=ノイズを自動で下げ、声などの大きい音のときだけ通す)
    public bool GateOn { get; set; }
    /// <summary>この音量(dB)を超えると開く。-70〜-10。</summary>
    public double GateThresholdDb { get; set; } = -45;
    /// <summary>音が小さくなってから、閉じていく速さ(ミリ秒)。20〜1000。</summary>
    public double GateReleaseMs { get; set; } = 200;
    /// <summary>閉じたときに下げる量(dB)。6〜80。大きいほど無音に近くなる。</summary>
    public double GateReductionDb { get; set; } = 40;

    // ---- イコライザ(10バンドのグラフィックEQ。31Hz〜16kHz、各±12dB)
    public bool EqOn { get; set; }
    /// <summary>ローカット(Hz)。この周波数より低い音を削る(12dB/oct)。0=なし、20〜400。イコライザの前にかかる。</summary>
    public double HpfHz { get; set; } = 0;
    /// <summary>10個の帯域のゲイン(dB)。EqBands.Freqs の順。</summary>
    public double[] Bands { get; set; } = new double[EqBands.Count];

    // ---- パラメトリックEQ(4バンド。位置(周波数)・ゲイン・幅(Q)を自由に決める。グラフィックEQの後にかかる)
    public bool ParamOn { get; set; }
    public ParamBand[] Params { get; set; } = ParamBand.Defaults();

    // ---- エコー
    public bool EchoOn { get; set; }
    public double EchoMs { get; set; } = 300;
    public double EchoFeedback { get; set; } = 0.35;
    public double EchoMix { get; set; } = 0.30;

    // ---- リバーブ
    public bool ReverbOn { get; set; }
    public double ReverbRoom { get; set; } = 0.5;
    public double ReverbDamp { get; set; } = 0.5;
    public double ReverbMix { get; set; } = 0.25;

    // ---- リミッター(いちばん最後にかかる。1.5ms先読みして、上限を超える音を滑らかに下げる)
    public bool LimiterOn { get; set; }
    /// <summary>上限(dB。0=クリップ寸前)。-24〜0。</summary>
    public double CeilingDb { get; set; } = -1.0;
    /// <summary>効いたあと、元の音量に戻るまでの時間(ミリ秒)。20〜1000。</summary>
    public double ReleaseMs { get; set; } = 150;

    /// <summary>効果が実際にかかるか(バイパスでなく、どれかがオン)。</summary>
    [JsonIgnore]
    public bool IsActive => !Bypass && HasAny;
    /// <summary>設定が1つでも入っているか(FXボタンの色づけ用。バイパス中でも true)。</summary>
    [JsonIgnore]
    public bool HasAny => VocalOn || GateOn || EqOn || HpfHz >= 20 || ParamOn || EchoOn || ReverbOn || LimiterOn;

    public EffectSetting Clone()
    {
        var c = (EffectSetting)MemberwiseClone();
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
        VocalAmount = C(VocalAmount, 0, 1, 0.85); VocalLowHz = C(VocalLowHz, 50, 1000, 200); VocalHighHz = C(VocalHighHz, 1000, 12000, 6000);
        GateThresholdDb = C(GateThresholdDb, -70, -10, -45); GateReleaseMs = C(GateReleaseMs, 20, 1000, 200); GateReductionDb = C(GateReductionDb, 6, 80, 40);
        EchoMs = C(EchoMs, 20, 1000, 300); EchoFeedback = C(EchoFeedback, 0, 0.9, 0.35); EchoMix = C(EchoMix, 0, 1, 0.3);
        ReverbRoom = C(ReverbRoom, 0, 1, 0.5); ReverbDamp = C(ReverbDamp, 0, 1, 0.5); ReverbMix = C(ReverbMix, 0, 1, 0.25);
        CeilingDb = C(CeilingDb, -24, 0, -1.0); ReleaseMs = C(ReleaseMs, 20, 1000, 150);
    }

    /// <summary>設定が初期値のままか(iniに書かなくてよいか)。</summary>
    [JsonIgnore]
    public bool IsDefault => SameSound(new EffectSetting()) && ToRecording == false && ToOutput == true;

    /// <summary>効果の中身（ゲート・ローカット・EQ・エコー・リバーブとバイパス）だけを o からコピーする。録音・出力への反映先（ToRecording / ToOutput）は変えない（コピー・貼り付け用）。</summary>
    public void CopySoundFrom(EffectSetting o)
    {
        var c = o.Clone();
        Bypass = c.Bypass;
        VocalOn = c.VocalOn; VocalAmount = c.VocalAmount; VocalLowHz = c.VocalLowHz; VocalHighHz = c.VocalHighHz;
        GateOn = c.GateOn; GateThresholdDb = c.GateThresholdDb; GateReleaseMs = c.GateReleaseMs; GateReductionDb = c.GateReductionDb;
        EqOn = c.EqOn; HpfHz = c.HpfHz; Bands = c.Bands;
        ParamOn = c.ParamOn; Params = c.Params;
        EchoOn = c.EchoOn; EchoMs = c.EchoMs; EchoFeedback = c.EchoFeedback; EchoMix = c.EchoMix;
        ReverbOn = c.ReverbOn; ReverbRoom = c.ReverbRoom; ReverbDamp = c.ReverbDamp; ReverbMix = c.ReverbMix;
        LimiterOn = c.LimiterOn; CeilingDb = c.CeilingDb; ReleaseMs = c.ReleaseMs;
        Normalize();
    }

    /// <summary>WriteTo が書く項目名（prefix の後ろ）。初期値に戻ったとき、ini から消すのに使う。</summary>
    public static readonly string[] IniKeys =
    {
        "Bypass", "ToRecording", "ToOutput", "VocalOn", "VocalAmount", "VocalLowHz", "VocalHighHz", "GateOn", "GateThresholdDb", "GateReleaseMs", "GateReductionDb", "EqOn", "HpfHz", "EqBands",
        "ParamOn", "ParamBands", "EchoOn", "EchoMs", "EchoFeedback", "EchoMix", "ReverbOn", "ReverbRoom", "ReverbDamp", "ReverbMix",
        "LimiterOn", "CeilingDb", "ReleaseMs",
    };

    public void WriteTo(IniSection s, string prefix, bool comment)
    {
        string? C(string t) => comment ? t : null;
        s.Set(prefix + "Bypass", Bypass, C("入力ごとのエフェクト。Bypass=すべてバイパス / ToRecording=録音に反映 / ToOutput=出力に反映"));
        s.Set(prefix + "ToRecording", ToRecording); s.Set(prefix + "ToOutput", ToOutput);
        s.Set(prefix + "VocalOn", VocalOn, C("ボーカル低減(カラオケ化。弱める量0〜1 / 対象帯域の下限Hz 50〜1000 / 上限Hz 1000〜12000。ステレオのみ)")); s.Set(prefix + "VocalAmount", VocalAmount); s.Set(prefix + "VocalLowHz", VocalLowHz); s.Set(prefix + "VocalHighHz", VocalHighHz);
        s.Set(prefix + "GateOn", GateOn, C("ノイズゲート(閾値dB-70〜-10 / 戻りms / 下げる量dB)")); s.Set(prefix + "GateThresholdDb", GateThresholdDb); s.Set(prefix + "GateReleaseMs", GateReleaseMs); s.Set(prefix + "GateReductionDb", GateReductionDb);
        s.Set(prefix + "EqOn", EqOn);
        s.Set(prefix + "HpfHz", HpfHz, C("ローカット(Hz、0=なし)"));
        s.Set(prefix + "EqBands", string.Join(",", (Bands ?? new double[EqBands.Count]).Select(v => v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))),
            C("イコライザ 31/62/125/250/500/1k/2k/4k/8k/16kHz のゲイン(dB、±12)"));
        s.Set(prefix + "ParamOn", ParamOn);
        s.Set(prefix + "ParamBands", string.Join(",", (Params ?? ParamBand.Defaults()).Select(b => b.ToText())),
            C("パラメトリックEQ 4バンドの 周波数Hz:ゲインdB:幅(Q)"));
        s.Set(prefix + "EchoOn", EchoOn); s.Set(prefix + "EchoMs", EchoMs); s.Set(prefix + "EchoFeedback", EchoFeedback); s.Set(prefix + "EchoMix", EchoMix);
        s.Set(prefix + "ReverbOn", ReverbOn); s.Set(prefix + "ReverbRoom", ReverbRoom); s.Set(prefix + "ReverbDamp", ReverbDamp); s.Set(prefix + "ReverbMix", ReverbMix);
        s.Set(prefix + "LimiterOn", LimiterOn, C("リミッター(上限dB -24〜0 / 戻りms 20〜1000)")); s.Set(prefix + "CeilingDb", CeilingDb); s.Set(prefix + "ReleaseMs", ReleaseMs);
    }

    public static EffectSetting ReadFrom(IniSection s, string prefix)
    {
        var d = new EffectSetting();
        var e = new EffectSetting
        {
            Bypass = s.GetBool(prefix + "Bypass", d.Bypass), ToRecording = s.GetBool(prefix + "ToRecording", d.ToRecording), ToOutput = s.GetBool(prefix + "ToOutput", d.ToOutput),
            VocalOn = s.GetBool(prefix + "VocalOn", d.VocalOn), VocalAmount = s.GetDouble(prefix + "VocalAmount", d.VocalAmount),
            VocalLowHz = s.GetDouble(prefix + "VocalLowHz", d.VocalLowHz), VocalHighHz = s.GetDouble(prefix + "VocalHighHz", d.VocalHighHz),
            GateOn = s.GetBool(prefix + "GateOn", d.GateOn), GateThresholdDb = s.GetDouble(prefix + "GateThresholdDb", d.GateThresholdDb),
            GateReleaseMs = s.GetDouble(prefix + "GateReleaseMs", d.GateReleaseMs), GateReductionDb = s.GetDouble(prefix + "GateReductionDb", d.GateReductionDb),
            EqOn = s.GetBool(prefix + "EqOn", d.EqOn), ParamOn = s.GetBool(prefix + "ParamOn", d.ParamOn), HpfHz = s.GetDouble(prefix + "HpfHz", d.HpfHz),
            EchoOn = s.GetBool(prefix + "EchoOn", d.EchoOn), EchoMs = s.GetDouble(prefix + "EchoMs", d.EchoMs),
            EchoFeedback = s.GetDouble(prefix + "EchoFeedback", d.EchoFeedback), EchoMix = s.GetDouble(prefix + "EchoMix", d.EchoMix),
            ReverbOn = s.GetBool(prefix + "ReverbOn", d.ReverbOn), ReverbRoom = s.GetDouble(prefix + "ReverbRoom", d.ReverbRoom),
            ReverbDamp = s.GetDouble(prefix + "ReverbDamp", d.ReverbDamp), ReverbMix = s.GetDouble(prefix + "ReverbMix", d.ReverbMix),
            LimiterOn = s.GetBool(prefix + "LimiterOn", d.LimiterOn), CeilingDb = s.GetDouble(prefix + "CeilingDb", d.CeilingDb), ReleaseMs = s.GetDouble(prefix + "ReleaseMs", d.ReleaseMs),
        };
        var parts = s.GetString(prefix + "EqBands").Split(',', StringSplitOptions.TrimEntries);
        for (int i = 0; i < EqBands.Count; i++)
            e.Bands[i] = i < parts.Length && double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
        var pp = s.GetString(prefix + "ParamBands").Split(',', StringSplitOptions.TrimEntries);
        for (int i = 0; i < e.Params.Length && i < pp.Length; i++) ParamBand.TryParse(pp[i], e.Params[i]);
        e.Normalize();
        return e;
    }

    private bool SameBands(EffectSetting o)
    {
        if (Bands == null || o.Bands == null) return Bands == o.Bands;
        if (Bands.Length != o.Bands.Length) return false;
        for (int i = 0; i < Bands.Length; i++) if (Bands[i] != o.Bands[i]) return false;
        return true;
    }

    private bool SameParams(EffectSetting o)
    {
        if (Params == null || o.Params == null) return Params == o.Params;
        if (Params.Length != o.Params.Length) return false;
        for (int i = 0; i < Params.Length; i++) if (!Params[i].Same(o.Params[i])) return false;
        return true;
    }

    /// <summary>効果の中身(オン/オフと数値)が同じか。録音・出力への反映先の設定は比べない。</summary>
    public bool SameSound(EffectSetting o) =>
        Bypass == o.Bypass && VocalOn == o.VocalOn && VocalAmount == o.VocalAmount && VocalLowHz == o.VocalLowHz && VocalHighHz == o.VocalHighHz && GateOn == o.GateOn && GateThresholdDb == o.GateThresholdDb && GateReleaseMs == o.GateReleaseMs && GateReductionDb == o.GateReductionDb && EqOn == o.EqOn && HpfHz == o.HpfHz && SameBands(o) && ParamOn == o.ParamOn && SameParams(o) && EchoOn == o.EchoOn && EchoMs == o.EchoMs && EchoFeedback == o.EchoFeedback
        && EchoMix == o.EchoMix && ReverbOn == o.ReverbOn && ReverbRoom == o.ReverbRoom && ReverbDamp == o.ReverbDamp && ReverbMix == o.ReverbMix
        && LimiterOn == o.LimiterOn && CeilingDb == o.CeilingDb && ReleaseMs == o.ReleaseMs;
}
