namespace Recorder.Core;

/// <summary>PCMバイト列から最大ピーク（0.0〜1.0超）を求める。16/24/32bit整数と32bit浮動小数に対応。</summary>
public static class PeakMeter
{
    public static float Compute(byte[] buf, int count, AudioFormatInfo f)
    {
        float peak = 0;
        if (f.IsFloat && f.BitsPerSample == 32)
        {
            for (int i = 0; i + 4 <= count; i += 4)
            {
                var v = Math.Abs(BitConverter.ToSingle(buf, i));
                if (v > peak) peak = v;
            }
        }
        else if (f.BitsPerSample == 16)
        {
            for (int i = 0; i + 2 <= count; i += 2)
            {
                var v = Math.Abs(BitConverter.ToInt16(buf, i) / 32768f);
                if (v > peak) peak = v;
            }
        }
        else if (f.BitsPerSample == 24)
        {
            for (int i = 0; i + 3 <= count; i += 3)
            {
                int s = (buf[i + 2] << 24 | buf[i + 1] << 16 | buf[i] << 8) >> 8;
                var v = Math.Abs(s / 8388608f);
                if (v > peak) peak = v;
            }
        }
        else if (f.BitsPerSample == 32)
        {
            for (int i = 0; i + 4 <= count; i += 4)
            {
                var v = Math.Abs(BitConverter.ToInt32(buf, i) / 2147483648f);
                if (v > peak) peak = v;
            }
        }
        return peak;
    }

    /// <summary>チャンネルごとの最大ピークを peaks へ書き足す（peaks[c] より大きいときだけ更新）。L/R のメーター用。16/24/32bit整数と32bit浮動小数に対応。</summary>
    public static void Channels(byte[] buf, int count, AudioFormatInfo f, float[] peaks)
    {
        int bytes = f.BitsPerSample / 8;
        int ch = Math.Max(1, f.Channels);
        if (bytes <= 0 || peaks.Length == 0) return;
        int n = count / bytes;
        for (int i = 0; i < n; i++)
        {
            int o = i * bytes;
            float v;
            if (f.IsFloat && f.BitsPerSample == 32) v = BitConverter.ToSingle(buf, o);
            else if (f.BitsPerSample == 16) v = BitConverter.ToInt16(buf, o) / 32768f;
            else if (f.BitsPerSample == 24) v = ((buf[o + 2] << 24 | buf[o + 1] << 16 | buf[o] << 8) >> 8) / 8388608f;
            else if (f.BitsPerSample == 32) v = BitConverter.ToInt32(buf, o) / 2147483648f;
            else return;
            v = Math.Abs(v);
            int c = i % ch;
            if (c < peaks.Length && v > peaks[c]) peaks[c] = v;
        }
    }
}
