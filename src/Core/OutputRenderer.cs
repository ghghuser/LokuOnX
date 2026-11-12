namespace Recorder.Core;

/// <summary>
/// 出力デバイス1台ぶんの音づくり（M11-2。LokuOn の OutputRenderer を LokuOnX 用に移したもの）。
/// 再生デバイスのコールバックから Render が呼ばれ（デバイスのクロックが基準）、各入力の複製先（InputTap）から音を取り出して、
/// デバイスのサンプリング周波数・チャンネル数に合わせ、MixBus で足して返す。
/// 入力デバイスと出力デバイスはクロックが別なので、バッファのたまり具合を見て変換比をわずかに調整する（時計ズレ補正。±0.5%まで）。
/// たまりが足りなければ無音を出し、目標までたまってから再開する（途切れても止まらない）。
/// 録音のミックスの 0.2 秒の遅延は経由せず、目標のたまりは 80ms。
/// この出力だけの最終ミックスのエフェクト（ローカット→グラフィックEQ→パラメトリックEQ→リミッター。M11-5）は、全体音量のあと・±1に切り詰める前に掛ける（SetMasterFx）。
/// 入力ごとのエフェクトは入力側（DeviceStream。M11-4）で掛け済みの音が届く。
/// 入力ゲインは入力側（DeviceStream）で掛け済みの音が届くので、ここでは掛けない。WPF や Windows API には依存しない（§6-3）。
/// </summary>
public sealed class OutputRenderer
{
    public const double TargetSec = 0.080;      // 目標のたまり（50msでは入力の届き方にむらがあると途切れてプツプツ鳴ったため80msに）
    public const double MaxFillSec = 0.400;     // これを超えたら目標まで捨てる
    private const double Kp = 0.1;              // たまりの差(秒)→変換比の調整
    private const double Ki = 0.01;             // たまりの差の積み上げ(ずれが一定でも目標に戻す)
    private const double MaxTrim = 0.005;       // ±0.5% まで
    private const double EmaAlpha = 0.05;

    private sealed class Chan
    {
        public TrimResampler? Rs;
        public SourceBuffer? Tap;               // Rs を作ったときの複製先(入れ替わったら作り直す)
        public readonly List<float> Pend = new();   // 変換済み(入力と同じチャンネル数、デバイスのレート)
        public int PendPos;                     // Pend の読み出し済みサンプル数
        public bool Primed;
        public double Ema;
        public double Integ;                    // 調整量の積み上げ(クロックのずれの推定)
        public float[] Chunk = new float[4096];
    }

    private readonly IReadOnlyList<InputTap> taps;
    private readonly int devRate, devCh;
    private readonly Chan[] chans;
    private float[][] inputs = Array.Empty<float[]>();
    private float[] mix = Array.Empty<float>();

    private volatile OutputFxChain? _masterFx;

    /// <summary>この出力の最終ミックスのエフェクトを渡す（動作中でも変えられる。何も掛けない設定のときは音に触れない）。</summary>
    public void SetMasterFx(OutputFxSetting s)
    {
        var c = _masterFx ?? new OutputFxChain(devRate, devCh);
        c.Update(s);
        _masterFx = c;
    }

    /// <summary>入力ごとの送り（オンオフ・音量）と、この出力の全体音量。</summary>
    public MixBus Bus { get; }

    public int DeviceRate => devRate;
    public int DeviceChannels => devCh;

    /// <summary>足りなくなった回数（診断・表示用）。</summary>
    public long Underruns;
    public long RenderCalls;
    public long Overflows;
    public volatile string LastError = "";
    private volatile float peakL, peakR;
    private volatile bool clipped;

    /// <param name="taps">入力ごとの複製先（InputHub の入力に取り付けたもの）。順番は入力の順。</param>
    public OutputRenderer(IReadOnlyList<InputTap> taps, int deviceRate, int deviceChannels)
    {
        this.taps = taps;
        devRate = deviceRate;
        devCh = Math.Max(1, deviceChannels);
        chans = new Chan[taps.Count];
        for (int i = 0; i < chans.Length; i++) chans[i] = new Chan();
        Bus = new MixBus(taps.Count);
    }

    /// <summary>前回の読み出し以降のピークとクリップ（メーター用）。</summary>
    public (float L, float R, bool Clipped) ReadPeaks()
    {
        var r = (peakL, peakR, clipped);
        peakL = 0; peakR = 0; clipped = false;
        return r;
    }

    /// <summary>デバイスのコールバックから呼ぶ。dest の先頭から frames フレーム（デバイスのチャンネル数）を書く。</summary>
    public void Render(float[] dest, int destOffset, int frames)
    {
        try { RenderCore(dest, destOffset, frames); }
        catch (Exception ex)
        {
            LastError = ex.GetType().Name + ": " + ex.Message;
            Array.Clear(dest, destOffset, frames * devCh);
        }
    }

    /// <summary>診断用の短い説明。</summary>
    public string Describe()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"デバイス {devRate}Hz/{devCh}ch 呼出{Interlocked.Read(ref RenderCalls)}回 途切れ{Interlocked.Read(ref Underruns)} 捨て{Interlocked.Read(ref Overflows)}");
        for (int i = 0; i < chans.Length; i++)
        {
            var t = taps[i].Buffer;
            if (t == null && !Bus.IsSendOn(i)) continue;   // 使っていない枠は出さない
            sb.Append($" | 入力{i + 1}: ");
            if (t == null) { sb.Append("まだ音が届いていません"); continue; }
            sb.Append($"送り{(Bus.IsSendOn(i) ? "オン" : "オフ")} たまり{t.AvailableFrames * 1000 / Math.Max(1, t.SampleRate)}ms {(chans[i].Primed ? "再生中" : "待機")}");
        }
        if (LastError.Length > 0) sb.Append(" | エラー: " + LastError);
        return sb.ToString();
    }

    private void RenderCore(float[] dest, int destOffset, int frames)
    {
        Interlocked.Increment(ref RenderCalls);
        int n = frames * devCh;
        if (mix.Length < n) mix = new float[n];
        if (inputs.Length != chans.Length) inputs = new float[chans.Length][];
        for (int i = 0; i < chans.Length; i++)
        {
            if (inputs[i] == null || inputs[i].Length < n) inputs[i] = new float[n];
            FillInput(i, frames);
        }
        var fx = _masterFx;
        var st = Bus.Process(inputs, frames, devCh, mix, null, fx != null && fx.IsActive ? fx.Process : null);
        Array.Copy(mix, 0, dest, destOffset, n);
        if (st.PeakL > peakL) peakL = st.PeakL;
        if (st.PeakR > peakR) peakR = st.PeakR;
        if (st.Clipped) clipped = true;
    }

    private void FillInput(int i, int frames)
    {
        var buf = inputs[i];
        int n = frames * devCh;
        var c = chans[i];
        var tap = taps[i].Buffer;
        if (tap == null || !Bus.IsSendOn(i))
        {
            Array.Clear(buf, 0, n);
            tap?.Clear();                       // 送らない間はためない
            c.Primed = false; c.Pend.Clear(); c.PendPos = 0;
            return;
        }
        int sc = tap.Channels;
        if (!ReferenceEquals(c.Tap, tap) || c.Rs == null)
        {
            c.Tap = tap;
            c.Rs = new TrimResampler(tap.SampleRate, devRate, sc, allowTrim: true);
            c.Pend.Clear(); c.PendPos = 0; c.Primed = false; c.Integ = 0;
        }

        double inRate = tap.SampleRate;
        double fillSec = tap.AvailableFrames / inRate + (c.Pend.Count - c.PendPos) / (double)sc / devRate;
        if (fillSec > MaxFillSec)
        {
            tap.DiscardTo((int)(TargetSec * inRate));
            c.Pend.Clear(); c.PendPos = 0;
            Interlocked.Increment(ref Overflows);
            fillSec = tap.AvailableFrames / inRate;
        }
        if (!c.Primed)
        {
            if (fillSec < TargetSec) { Array.Clear(buf, 0, n); return; }
            c.Primed = true; c.Ema = fillSec;
        }
        c.Ema += EmaAlpha * (fillSec - c.Ema);
        c.Integ = Math.Clamp(c.Integ + Ki * (c.Ema - TargetSec) * frames / devRate, -MaxTrim, MaxTrim);
        c.Rs.Trim = Math.Clamp(Kp * (c.Ema - TargetSec) + c.Integ, -MaxTrim, MaxTrim);

        // 必要なぶんだけ変換して Pend にためる
        int pendFrames = (c.Pend.Count - c.PendPos) / sc;
        double ratio = inRate / devRate;
        while (pendFrames < frames)
        {
            int avail = tap.AvailableFrames;
            if (avail <= 0) break;
            int want = (int)Math.Ceiling((frames - pendFrames) * ratio * 1.01) + 2;
            int take = Math.Min(Math.Min(want, avail), c.Chunk.Length / sc);
            int got = tap.Read(c.Chunk, 0, take);
            if (got <= 0) break;
            c.Rs.Process(c.Chunk, got, c.Pend);
            pendFrames = (c.Pend.Count - c.PendPos) / sc;
        }

        int use = Math.Min(frames, pendFrames);
        Convert(c.Pend, c.PendPos, use, sc, buf);
        c.PendPos += use * sc;
        if (use < frames)
        {
            Array.Clear(buf, use * devCh, (frames - use) * devCh);
            c.Primed = false;                    // 足りなくなった: たまり直すまで無音
            Interlocked.Increment(ref Underruns);
        }
        if (c.PendPos > 0 && c.PendPos >= c.Pend.Count) { c.Pend.Clear(); c.PendPos = 0; }
        else if (c.PendPos > 16384) { c.Pend.RemoveRange(0, c.PendPos); c.PendPos = 0; }
    }

    /// <summary>入力のチャンネル数からデバイスのチャンネル数へ（モノラルは左右に複製、ステレオ→モノラルは平均、余りは0）。</summary>
    private void Convert(List<float> src, int pos, int frames, int sc, float[] dst)
    {
        int dc = devCh;
        for (int f = 0; f < frames; f++)
        {
            int s = pos + f * sc, d = f * dc;
            if (sc == dc) { for (int k = 0; k < dc; k++) dst[d + k] = src[s + k]; }
            else if (sc == 1)
            {
                float v = src[s];
                dst[d] = v; if (dc > 1) dst[d + 1] = v;
                for (int k = 2; k < dc; k++) dst[d + k] = 0f;
            }
            else if (dc == 1) dst[d] = (src[s] + src[s + 1]) * 0.5f;
            else
            {
                int m = Math.Min(sc, dc);
                for (int k = 0; k < m; k++) dst[d + k] = src[s + k];
                for (int k = m; k < dc; k++) dst[d + k] = 0f;
            }
        }
    }
}
