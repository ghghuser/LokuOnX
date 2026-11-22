namespace Recorder.Core;

/// <summary>
/// コーラス。短いディレイ（約15ms）を LFO（ゆっくりした正弦波）で揺らし、原音に重ねて厚みと広がりを出す。
/// チャンネルごとに LFO の位相をずらす（ステレオでは左右で揺れ方が違うので広がる）。
/// 出力 = (原音 + mix × 揺らした音) ÷ (1 + mix)。mix=0 のときは原音と同じ。
/// </summary>
public sealed class Chorus
{
    public const double BaseMs = 15;
    private const double MaxDepthMs = 10;

    private readonly int sr, ch;
    private readonly float[][] buf;
    private readonly int len;
    private int pos;
    private double phase;
    private double rate = 0.8, depthMs = 3, mix = 0.5;

    public Chorus(int sampleRate, int channels)
    {
        sr = sampleRate; ch = Math.Max(1, channels);
        len = (int)((BaseMs + MaxDepthMs + 5) * 0.001 * sr) + 4;
        buf = new float[ch][];
        for (int c = 0; c < ch; c++) buf[c] = new float[len];
    }

    /// <summary>設定する（係数だけ更新。状態は保つ）。</summary>
    public void Set(double rateHz, double depthMs, double mix)
    {
        rate = Math.Clamp(rateHz, 0.05, 10);
        this.depthMs = Math.Clamp(depthMs, 0, MaxDepthMs);
        this.mix = Math.Clamp(mix, 0, 1);
    }

    public void Reset()
    {
        foreach (var b in buf) Array.Clear(b);
        pos = 0; phase = 0;
    }

    public void Process(float[] data, int frames)
    {
        double inc = rate / sr, baseS = BaseMs * 0.001 * sr, depthS = depthMs * 0.001 * sr;
        float m = (float)mix, norm = 1f / (1f + m);
        for (int f = 0, i = 0; f < frames; f++, i += ch)
        {
            for (int c = 0; c < ch; c++)
            {
                float x = data[i + c];
                buf[c][pos] = x;
                double ph = phase + c * 0.25;
                double d = baseS + depthS * Math.Sin(2 * Math.PI * ph);
                double rp = pos - d;
                if (rp < 0) rp += len;
                int i0 = (int)rp; double fr = rp - i0;
                int i1 = i0 + 1; if (i1 >= len) i1 = 0;
                float wet = (float)(buf[c][i0] * (1 - fr) + buf[c][i1] * fr);
                data[i + c] = (x + m * wet) * norm;
            }
            if (++pos >= len) pos = 0;
            phase += inc; if (phase >= 1) phase -= 1;
        }
    }
}
